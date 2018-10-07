#if MODEL_DIAG && ! NETFX_CORE
using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public partial class Diags
    {
        public partial class Model
        {
            public IEnumerable<FormatBase.Model> CheckRoot()
            {
                FileAttributes atts;

                try
                { atts = File.GetAttributes (Data.Root); }
                catch (NotSupportedException)
                {
                    Data.Result = Severity.Fatal;
                    throw new ArgumentException ("Directory name is invalid.");
                }

                if ((atts & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    Data.Result = Severity.NoIssue;
                    foreach (FormatBase.Model fmtModel in CheckRootDir())
                        yield return fmtModel;
                }
                else
                {
                    var access = Data.Response != Interaction.None ? FileAccess.ReadWrite : FileAccess.Read;
                    Stream st = new FileStream (Data.Root, FileMode.Open, access, FileShare.Read);
                    var fmtModel = CheckFile (st, Data.Root, out Severity result);
                    Data.Result = result;
                    yield return fmtModel;
                }
            }

            private IEnumerable<FormatBase.Model> CheckRootDir()
            {
                foreach (string dn in new DirTraverser (Data.Root))
                {
                    var dInfo = new DirectoryInfo (dn);
                    var fileInfos = Data.Filter == null ? dInfo.GetFiles() : dInfo.GetFiles (Data.Filter);

                    foreach (FileInfo fInfo in fileInfos)
                    {
                        FormatBase.Model fmtModel;
                        try
                        {
                            // Many exceptions also caught by outer caller:
                            var access = Data.Response != Interaction.None ? FileAccess.ReadWrite : FileAccess.Read;
                            Stream stream = new FileStream (fInfo.FullName, FileMode.Open, access, FileShare.Read);
                            fmtModel = CheckFile (stream, fInfo.FullName, out Severity badness);
                            if (badness > Data.Result)
                                Data.Result = badness;
                        }
                        catch (FileNotFoundException ex)
                        {
                            Data.Result = Severity.Fatal;
                            Data.OnMessageSend (ex.Message.Trim(), Severity.Fatal);
                            Data.OnMessageSend ("Ignored.", Severity.Advisory);
                            continue;
                        }
                        yield return fmtModel;
                    }
                }
            }


            private FormatBase.Model CheckFile (Stream stream, string path, out Severity resultCode)
            {
                SetCurrentFile (Path.GetDirectoryName (path), Path.GetFileName (path));

                bool isKnownExtension;
                FileFormat trueFormat;
                FormatBase.Model fmtModel = null;
                try
                {
                    fmtModel = FormatBase.CreateModel (Data.FileFormats.Items, stream, path, Data.HashFlags, Data.ValidationFlags,
                                                       Data.Filter, out isKnownExtension, out trueFormat);
                }
#pragma warning disable 0168
                catch (Exception ex)
#pragma warning restore 0168
                {
#if DEBUG
                    throw;
#else
                    Data.OnMessageSend ("Exception: " + ex.Message.TrimEnd (null), Severity.Fatal);
                    ++Data.TotalErrors;
                    resultCode = Severity.Fatal;
                    return null;
#endif
                }

                if (! isKnownExtension)
                {
                    if (Data.Scope <= Granularity.Verbose)
                        Data.OnMessageSend ("Ignored.", Severity.Trivia);
                    resultCode = Severity.NoIssue;
                    return fmtModel;
                }

                if (fmtModel == null)
                {
                    if (trueFormat != null)
                        resultCode = Severity.NoIssue;
                    else
                    {
                        if (Data.Scope <= Granularity.Quiet)
                            Data.OnMessageSend ("Unrecognized contents.", Severity.Error);
                        ++Data.TotalErrors;
                        resultCode = Severity.Fatal;
                    }
                    return null;
                }

                ++Data.TotalFiles;
                trueFormat.TrueTotal += 1;

                FormatBase fmt = fmtModel.Data;

                if (fmt.IsBadHeader)
                    ++trueFormat.TotalHeaderErrors;

                if (fmt.IsBadData)
                    ++trueFormat.TotalDataErrors;

                fmtModel.IssueModel.Escalate (Data.WarnEscalator, Data.ErrEscalator);

                ReportFormat (fmt);

                if (! fmt.Issues.HasError)
                {
                    int startRepairableCount = fmt.Issues.RepairableCount;
                    if (startRepairableCount > 0)
                    {
                        ++Data.TotalRepairable;
                        var didRename = RepairFile (fmtModel);
                        if (didRename)
                            --trueFormat.TotalMisnamed;
                        if (fmt.Issues.RepairableCount == 0)
                            --Data.TotalRepairable;
                        Data.TotalRepairs += startRepairableCount - fmt.Issues.RepairableCount;
                    }
                }

                resultCode = fmt.Issues.MaxSeverity;
                return fmtModel;
            }
        }
    }
}
#endif

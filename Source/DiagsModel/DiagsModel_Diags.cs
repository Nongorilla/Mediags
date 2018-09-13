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
            public IEnumerable<FormatBase.ModelBase> CheckRoot()
            {
                FileAttributes atts;

                try
                { atts = File.GetAttributes (Bind.Root); }
                catch (NotSupportedException)
                {
                    Bind.Result = Severity.Fatal;
                    throw new ArgumentException ("Directory name is invalid.");
                }

                if ((atts & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    Bind.Result = Severity.NoIssue;
                    foreach (FormatBase.ModelBase fmtModel in CheckRootDir())
                        yield return fmtModel;
                }
                else
                {
                    var access = Bind.Response == Interaction.PromptToRepair ? FileAccess.ReadWrite : FileAccess.Read;
                    Stream st = new FileStream (Bind.Root, FileMode.Open, access, FileShare.Read);
                    var fmtModel = CheckFile (st, Bind.Root, out Severity result);
                    Bind.Result = result;
                    yield return fmtModel;
                }
            }

            private IEnumerable<FormatBase.ModelBase> CheckRootDir()
            {
                foreach (string dn in new DirTraverser (Bind.Root))
                {
                    var dInfo = new DirectoryInfo (dn);
                    var fileInfos = Bind.Filter == null ? dInfo.GetFiles() : dInfo.GetFiles (Bind.Filter);

                    foreach (FileInfo fInfo in fileInfos)
                    {
                        FormatBase.ModelBase fmtModel;
                        try
                        {
                            // Many exceptions also caught by outer caller:
                            var access = Bind.Response == Interaction.PromptToRepair ? FileAccess.ReadWrite : FileAccess.Read;
                            Stream stream = new FileStream (fInfo.FullName, FileMode.Open, access, FileShare.Read);
                            fmtModel = CheckFile (stream, fInfo.FullName, out Severity badness);
                            if (badness > Bind.Result)
                                Bind.Result = badness;
                        }
                        catch (FileNotFoundException ex)
                        {
                            Bind.Result = Severity.Fatal;
                            Bind.OnMessageSend (ex.Message.Trim(), Severity.Fatal);
                            Bind.OnMessageSend ("Ignored.", Severity.Advisory);
                            continue;
                        }
                        yield return fmtModel;
                    }
                }
            }


            private FormatBase.ModelBase CheckFile (Stream stream, string path, out Severity resultCode)
            {
                SetCurrentFile (Path.GetDirectoryName (path), Path.GetFileName (path));

                bool isKnownExtension;
                FileFormat trueFormat;
                FormatBase.ModelBase fmtModel = null;
                try
                {
                    fmtModel = FormatBase.CreateModel (Bind.FileFormats.Items, stream, path, Bind.HashFlags, Bind.ValidationFlags,
                                                       Bind.Filter, out isKnownExtension, out trueFormat);
                }
#pragma warning disable 0168
                catch (Exception ex)
#pragma warning restore 0168
                {
#if DEBUG
                    throw;
#else
                    Bind.OnMessageSend ("Exception: " + ex.Message.TrimEnd (null), Severity.Fatal);
                    ++Bind.TotalErrors;
                    resultCode = Severity.Fatal;
                    return null;
#endif
                }

                if (! isKnownExtension)
                {
                    if (Bind.Scope <= Granularity.Verbose)
                        Bind.OnMessageSend ("Ignored.", Severity.Trivia);
                    resultCode = Severity.NoIssue;
                    return fmtModel;
                }

                if (fmtModel == null)
                {
                    if (trueFormat != null)
                        resultCode = Severity.NoIssue;
                    else
                    {
                        if (Bind.Scope <= Granularity.Quiet)
                            Bind.OnMessageSend ("Unrecognized contents.", Severity.Error);
                        ++Bind.TotalErrors;
                        resultCode = Severity.Fatal;
                    }
                    return null;
                }

                ++Bind.TotalFiles;
                trueFormat.TrueTotal += 1;

                FormatBase fmt = fmtModel.BaseBind;

                if (fmt.IsBadHeader)
                    ++trueFormat.TotalHeaderErrors;

                if (fmt.IsBadData)
                    ++trueFormat.TotalDataErrors;

                fmtModel.IssueModel.Escalate (Bind.WarnEscalator, Bind.ErrEscalator);

                ReportFormat (fmt);

                if (! fmt.Issues.HasError)
                {
                    int startRepairableCount = fmt.Issues.RepairableCount;
                    if (startRepairableCount > 0)
                    {
                        ++Bind.TotalRepairable;
                        var didRename = RepairFile (fmtModel);
                        if (didRename)
                            --trueFormat.TotalMisnamed;
                        if (fmt.Issues.RepairableCount == 0)
                            --Bind.TotalRepairable;
                        Bind.TotalRepairs += startRepairableCount - fmt.Issues.RepairableCount;
                    }
                }

                resultCode = fmt.Issues.MaxSeverity;
                return fmtModel;
            }
        }
    }
}
#endif

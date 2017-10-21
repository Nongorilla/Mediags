#if MODEL_DIAG && ! NETFX_CORE
using System;
using System.IO;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public partial class Diags
    {
        public partial class Model
        {
            public Severity CheckArg()
            {
                FileAttributes atts;
                Severity result = Severity.NoIssue;

                try
                {
                    atts = File.GetAttributes (Bind.Root);
                }
                catch (NotSupportedException)
                { throw new ArgumentException ("Directory name is invalid."); }

                if ((atts & FileAttributes.Directory) == FileAttributes.Directory)
                    result = CheckDirs();
                else
                {
                    if (Bind.Filter != null)
                        throw new ArgumentException ("Wildcard not valid with a file argument.");

                    Bind.Filter = Bind.Root;

                    var access = Bind.Response == Interaction.PromptToRepair? FileAccess.ReadWrite : FileAccess.Read;
                    var fInfo = new FileInfo (Bind.Root);
                    var fs0 = new FileStream (fInfo.FullName, FileMode.Open, access, FileShare.Read);

                    result = CheckFile (fs0, fInfo.FullName);
                }

                Bind.OnReportClose();
                return result;
            }


            private Severity CheckDirs()
            {
                Severity theWorst = Severity.NoIssue;

                foreach (string dn in new DirTraverser (Bind.Root))
                {
                    if (! string.IsNullOrEmpty (Bind.Exclusion) && dn.Contains (Bind.Exclusion))
                        continue;

                    var dInfo = new DirectoryInfo (dn);
                    var fileInfos = Bind.Filter == null? dInfo.GetFiles() : dInfo.GetFiles(Bind.Filter);
                    var access = Bind.Response == Interaction.PromptToRepair? FileAccess.ReadWrite : FileAccess.Read;

                    foreach (FileInfo fInfo in fileInfos)
                    {
                        Severity badness;
                        try
                        {
                            var fs0 = new FileStream (fInfo.FullName, FileMode.Open, access, FileShare.Read);
                            badness = CheckFile (fs0, fInfo.FullName);
                        }
                        catch (FileNotFoundException ex)
                        {
                            badness = Severity.Fatal;
                            Bind.OnMessageSend (ex.Message.Trim(), badness);
                            Bind.OnMessageSend ("Ignored.", Severity.Advisory);
                        }
                        if (badness > theWorst)
                            theWorst = badness;
                    }
                }

                return theWorst;
            }


            private Severity CheckFile (Stream stream, string path)
            {
                SetCurrentFile (Path.GetDirectoryName (path), Path.GetFileName (path));

                FileIntent intent = Bind.Response == Interaction.PromptToRepair? FileIntent.Update : FileIntent.ReadOnly;
                bool isKnownExtension;
                FileFormat trueFormat;
                FormatBase.ModelBase formatModel;
                try
                {
                    formatModel = FormatBase.CreateModel (Bind.FileFormats.Items, stream, path, Bind.HashFlags, Bind.ValidationFlags,
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
                    return Severity.Fatal;
#endif
                }

                if (! isKnownExtension)
                {
                    if (Bind.Scope <= Granularity.Verbose)
                        Bind.OnMessageSend ("Ignored.", Severity.Trivia);
                    return Severity.NoIssue;
                }

                if (formatModel == null)
                {
                    if (trueFormat != null)
                        return Severity.NoIssue;

                    if (Bind.Scope <= Granularity.Quiet)
                        Bind.OnMessageSend ("Unrecognized contents.", Severity.Error);
                    ++Bind.TotalErrors;
                    return Severity.Fatal;
                }

                ++Bind.TotalFiles;
                trueFormat.TrueTotal += 1;

                FormatBase fmt = formatModel.BaseBind;

                if (fmt.IsBadHeader)
                    ++trueFormat.TotalHeaderErrors;

                if (fmt.IsBadData)
                    ++trueFormat.TotalDataErrors;

                formatModel.IssueModel.Escalate (Bind.WarnEscalator, Bind.ErrEscalator);

                ReportFormat (fmt);

                if (! fmt.Issues.HasError)
                {
                    int startRepairableCount = fmt.Issues.RepairableCount;
                    if (startRepairableCount > 0)
                    {
                        ++Bind.TotalRepairable;
                        var didRename = RepairFile (formatModel);
                        if (didRename)
                            --trueFormat.TotalMisnamed;
                        if (fmt.Issues.RepairableCount == 0)
                            --Bind.TotalRepairable;
                        Bind.TotalRepairs += startRepairableCount - fmt.Issues.RepairableCount;
                    }
                }

                return fmt.Issues.MaxSeverity;
            }
        }
    }
}
#endif

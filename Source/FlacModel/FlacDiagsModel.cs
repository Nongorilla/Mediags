#if MODEL_FLAC
using System;
using System.IO;
using System.Linq;
using System.Text;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public partial class FlacDiags : Diags
    {
        public new class Model : Diags.Model, IDisposable
        {
            private int consecutiveInvalidations = 0;
            public new FlacDiags Data => (FlacDiags) _data;
            public readonly Issue.Vector.Model IssueModel;

            private FlacRip.Model ripModel;
            public FlacRip.Model RipModel
            {
                get { return ripModel; }
                private set
                {
                    ripModel = value;
                    Data.Rip = ripModel?.Bind;
                }
            }


            public Model (string root, Granularity report) : base()
            {
                IssueModel = new Issue.Vector.Model();
                this._data = new FlacDiags (root, report, FormatModel.Bind, IssueModel.Bind);
            }


            public Severity ValidateFlacsDeep (string signature)
            {
                string err = null;
                var exitCode = Severity.NoIssue;

                SetCurrentDirectory (Data.Root);

                try
                {
                    foreach (var curDir in new DirTraverser (Data.Root))
                    {
                        if (Data.StopAfter > 0 && consecutiveInvalidations >= Data.StopAfter)
                        {
                            char response = Data.InputChar ($"\n{Data.StopAfter} consecutive rips invalidated. Stop (S) / Resume (R) / Don't ask again (D)? ", "srd");
                            if (response == 's')
                            {
                                err = "Stopped by user.";
                                break;
                            }
                            consecutiveInvalidations = 0;
                            if (response == 'd')
                                Data.StopAfter = 0;
                        }

                        var ripStatus = ValidateFlacRip (curDir, signature, false, true);
                        if (exitCode < ripStatus)
                            exitCode = ripStatus;
                    }
                }
                catch (IOException ex)
                { err = ex.Message.Trim (null); }

                if (err != null)
                {
                    ReportLine ($"Bailing out of traversal: {err}");
                    exitCode = Severity.Fatal;
                }

                Data.OnReportClose();
                return exitCode;
            }


            public void ClearRip()
            {
                if (RipModel != null)
                {
                    RipModel.Dispose();
                    RipModel = null;
                }
                Data.GuiIssues = null;
                IssueModel.Clear();
            }


            public void SetGuiIssues (Issue.Vector issues)
            { Data.GuiIssues = issues; }


            public Severity ValidateFlacRip (string arg, string signature, bool allowFileArg, bool prefixDirOnErr)
            {
                string newPath = null;
                string err = null;

                if (signature != null)
                    if (signature.Length == 0)
                        signature = null;
                    else if (Map1252.ToClean1252FileName (signature.Trim(null)) != signature || signature.Any (Char.IsWhiteSpace))
                    {
                        ReportLine ("Invalid signature '" + signature + "'.", Severity.Error, false);
                        return Severity.Fatal;
                    }

                try
                {
                    var fInfo = new FileInfo (arg);
                    if (fInfo.Attributes.HasFlag (FileAttributes.Directory))
                        newPath = fInfo.FullName;
                    else if (allowFileArg)
                        newPath = fInfo.DirectoryName;
                    else
                        err = "Not a directory.";
                }
                catch (ArgumentException ex)
                { err = ex.Message.Trim (null); }
                catch (DirectoryNotFoundException ex)
                { err = ex.Message.Trim (null); }
                catch (IOException ex)
                { err = ex.Message.Trim (null); }
                catch (NotSupportedException)
                { err = "Path is not valid."; }

                RipModel = new FlacRip.Model (this, newPath, Data.Autoname, signature);

                if (err != null)
                {
                    ReportLine (err, Severity.Error, false);
                    SetCurrentFile (null);
                    RipModel.SetStatus (Severity.Error);
                    return Severity.Error;
                }

                RipModel.Bind.IsWip = true;

                try
                {
                    RipModel.Validate();
                }
                catch (IOException ex)
                {
                    err = ex.Message.Trim (null);
                    ReportLine (err);
                    SetCurrentFile (null);
                    RipModel.SetStatus (Severity.Fatal);
                    return Severity.Fatal;
                }

                SetCurrentFile (null);

                if (RipModel.Bind.Status == Severity.NoIssue && RipModel.Bind.Log == null)
                {
                    if (RipModel.Bind.Signature != null)
                        try
                        {
                            var errPath = newPath + Path.DirectorySeparatorChar + Data.NoncompliantName;
                            if (File.Exists (errPath))
                                File.Delete (errPath);
                        }
                        catch (Exception)
                        { /* discard all */ }

                    RipModel.Bind.IsWip = false;
                    ReportLine (RipModel.Bind.Trailer, Severity.Trivia, false);
                    return Severity.Trivia;
                }

                if (RipModel.Bind.Status >= Severity.Error)
                {
                    RipModel.CloseFiles();
                    if (prefixDirOnErr)
                    {
                        var baseName = Path.GetFileName (newPath);
                        if (RipModel.Bind.Signature != null && ! baseName.StartsWith ("!!") && newPath.Length < 235)
                        {
                            var parentPath = Directory.GetParent(newPath).FullName;
                            var errPath = parentPath + Path.DirectorySeparatorChar + Data.FailPrefix + baseName;
                            try
                            { Directory.Move (newPath, errPath); }
                            catch (Exception)
                            { /* discard all */ }

                            ++consecutiveInvalidations;
                        }
                    }
                    RipModel.Bind.IsWip = false;
                }

                if (! RipModel.Bind.IsWip)
                {
                    SetCurrentFile (null);
                    ReportLine (RipModel.Bind.Trailer, RipModel.Bind.Status, false);
                }

                return RipModel.Bind.Status;
            }


            public override void ReportFormat (FormatBase fb, bool logErrorsToFile = false)
            {
                base.ReportFormat (fb);

                if (logErrorsToFile && fb.Issues.HasError)
                    try
                    {
                        using (var sw = new StreamWriter (Data.CurrentDirectory + Path.DirectorySeparatorChar
                                                        + Data.NoncompliantName, false, Encoding.GetEncoding (1252)))
                        {
                            var dn = Path.GetDirectoryName (fb.Path);

                            sw.WriteLine ("Diagnostics by the caustic " + Data.Product + " v" + Data.ProductVersion + ":");
                            sw.WriteLine ();
                            sw.Write     (dn);
                            sw.WriteLine (Path.DirectorySeparatorChar);
                            sw.WriteLine (fb.Name);
                            sw.WriteLine ();

                            foreach (var issue in fb.Issues.Items)
                            {
                                var severity = issue.Level;
                                var prefix = severity < Severity.Warning? "  " : severity == Severity.Warning? "  Warning: " : "* Error: ";
                                sw.Write (prefix);
                                sw.WriteLine (issue.Message);
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    { /* discard */ }
                    catch (IOException)
                    { /* discard */ }
            }

            public void ReportLine (string message, Severity severity = Severity.Error)
            {
                ReportLine (message, severity, RipModel.Bind.Signature != null);
            }

            public override void ReportLine (string message, Severity severity, bool logErrToFile)
            {
                if (Data.CurrentFile == null)
                    IssueModel.Add (message, Severity.NoIssue);

                base.ReportLine (message, severity, logErrToFile);

                if (logErrToFile && severity >= Severity.Error)
                    try
                    {
                        using (var sw = new StreamWriter (Data.CurrentDirectory + Path.DirectorySeparatorChar
                                                        + Data.NoncompliantName, false, Encoding.GetEncoding (1252)))
                        {
                            sw.WriteLine ("Error found by the caustic " + Data.Product + " v" + Data.ProductVersion + ":");
                            sw.WriteLine ();
                            sw.Write (Data.CurrentDirectory);
                            sw.WriteLine (Path.DirectorySeparatorChar);
                            sw.WriteLine (Data.CurrentFile);
                            string prefix = severity < Severity.Warning? "  " : (severity == Severity.Warning? "* Warning: " : "* Error: ");
                            sw.Write (prefix);
                            sw.WriteLine (message);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    { /* discard */ }
                    catch (IOException)
                    { /* discard */ }
            }


#region IDisposable Support
            private bool isDisposed = false;

            protected virtual void Dispose (bool disposing)
            {
                if (! isDisposed)
                {
                    if (disposing && RipModel != null)
                        RipModel.Dispose();
                    isDisposed = true;
                }
            }

            public void Dispose()
            {
                Dispose (true);
            }
#endregion
        }
    }
}
#endif

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
            public new FlacDiags Bind { get; private set; }
            public readonly Issue.Vector.Model IssueModel;

            private FlacRip.Model ripModel;
            public FlacRip.Model RipModel
            {
                get { return ripModel; }
                private set
                {
                    ripModel = value;
                    Bind.Rip = ripModel == null? null : ripModel.Bind;
                }
            }


            public Model (string root, Granularity report) : base()
            {
                IssueModel = new Issue.Vector.Model();
                base.Bind = Bind = new FlacDiags (root, report, FormatModel.Bind, IssueModel.Bind);
            }


            public Severity ValidateFlacsDeep (string signature)
            {
                string err = null;
                var exitCode = Severity.NoIssue;

                SetCurrentDirectory (Bind.Root);

                try
                {
                    foreach (var curDir in new DirTraverser (Bind.Root))
                    {
                        var ripStatus = ValidateFlacRip (curDir, signature, false, true);
                        if (exitCode < ripStatus)
                            exitCode = ripStatus;
                    }
                }
                catch (IOException ex)
                { err = ex.Message.Trim (null); }

                if (err != null)
                {
                    ReportLine ("Error: " + err + " Bailing out of traversal.");
                    exitCode = Severity.Fatal;
                }

                Bind.OnReportClose();
                return exitCode;
            }


            public void ClearRip()
            {
                if (RipModel != null)
                {
                    RipModel.Dispose();
                    RipModel = null;
                }
                Bind.GuiIssues = null;
                IssueModel.Clear();
            }


            public void SetGuiIssues (Issue.Vector issues)
            { Bind.GuiIssues = issues; }


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

                RipModel = new FlacRip.Model (this, newPath, Bind.Autoname, signature);

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
                            var errPath = newPath + Path.DirectorySeparatorChar + Bind.NoncompliantName;
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
                        if (RipModel.Bind.Signature != null && ! baseName.StartsWith("!!") && newPath.Length < 235)
                        {
                            var parentPath = Directory.GetParent(newPath).FullName;
                            var errPath = parentPath + Path.DirectorySeparatorChar + Bind.FailPrefix + baseName;
                            try
                            { Directory.Move (newPath, errPath); }
                            catch (Exception)
                            { /* discard all */ }
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
                        using (var sw = new StreamWriter (Bind.CurrentDirectory + Path.DirectorySeparatorChar
                                                        + Bind.NoncompliantName, false, Encoding.GetEncoding (1252)))
                        {
                            var dn = Path.GetDirectoryName (fb.Path);

                            sw.WriteLine ("Diagnostics by the caustic " + Bind.Product + " v" + Bind.ProductVersion + ":");
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
                if (Bind.CurrentFile == null)
                    IssueModel.Add (message, Severity.NoIssue);

                base.ReportLine (message, severity, logErrToFile);

                if (logErrToFile && severity >= Severity.Error)
                    try
                    {
                        using (var sw = new StreamWriter (Bind.CurrentDirectory + Path.DirectorySeparatorChar
                                                        + Bind.NoncompliantName, false, Encoding.GetEncoding (1252)))
                        {
                            sw.WriteLine ("Error found by the caustic " + Bind.Product + " v" + Bind.ProductVersion + ":");
                            sw.WriteLine ();
                            sw.Write (Bind.CurrentDirectory);
                            sw.WriteLine (Path.DirectorySeparatorChar);
                            sw.WriteLine (Bind.CurrentFile);
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

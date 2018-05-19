#if MODEL_LAME
using System;
using System.IO;
using System.Linq;
using System.Text;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public partial class LameDiags : Diags
    {
        public bool WillProve
        {
            get { return (ErrEscalator & IssueTags.ProveErr) != 0 && (WarnEscalator & IssueTags.ProveWarn) != 0; }
            set
            {
                if (value)
                {
                    WarnEscalator |= IssueTags.ProveWarn;
                     ErrEscalator |= IssueTags.ProveErr;
                }
                else
                {
                    WarnEscalator &= ~ IssueTags.ProveWarn;
                     ErrEscalator &= ~ IssueTags.ProveErr;
                }
            }
        }

        public new class Model : Diags.Model
        {
            public new LameDiags Bind { get; private set; }
            public readonly Issue.Vector.Model IssueModel;

            private LameRip.Model ripModel;
            public LameRip.Model RipModel
            {
                get { return ripModel; }
                private set
                {
                    ripModel = value;
                    Bind.Rip = ripModel?.Bind;
                }
            }


            public Model (string root, Granularity report) : base()
            {
                IssueModel = new Issue.Vector.Model();
                base.Bind = Bind = new LameDiags (root, report, FormatModel.Bind, IssueModel.Bind);
            }


            public Severity ValidateLameRipsDeep (string signature, string logTag)
            {
                string err = null;
                var exitCode = Severity.NoIssue;

                SetCurrentDirectory (Bind.Root);

                try
                {
                    foreach (var curDir in new DirTraverser (Bind.Root))
                    {
                        var ripStatus = ValidateLameRip (curDir, signature, logTag);
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


            public Severity ValidateLameRip (string arg, string signature, string lameTag)
            {
                string err = null;
                string newPath = null;

                if (signature != null)
                    if (signature.Length == 0)
                        signature = null;
                    else if (Map1252.ToClean1252FileName (signature.Trim(null)) != signature || signature.Any (Char.IsWhiteSpace))
                    {
                        ReportLine ("Invalid signature '" + signature + "'.", Severity.Error, false);
                        return Severity.Fatal;
                    }

                if (lameTag != null)
                    if (lameTag.Length == 0)
                        lameTag = null;
                    else if (Map1252.ToClean1252FileName (lameTag.Trim (null)) != lameTag || lameTag.Any (Char.IsWhiteSpace))
                    {
                        ReportLine ("Invalid log tag '" + lameTag + "'.", Severity.Error, false);
                        return Severity.Fatal;
                    }

                try
                {
                    var fInfo = new FileInfo (arg);
                    if (fInfo.Attributes.HasFlag (FileAttributes.Directory))
                        newPath = fInfo.FullName;
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

                RipModel = new LameRip.Model (this, newPath, signature, lameTag);

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

                    var baseName = Path.GetFileName (newPath);
                    if (RipModel.Bind.Signature != null && ! baseName.StartsWith("!!") && newPath.Length < 235)
                    {
                        var errPath = Directory.GetParent(newPath).FullName;
                        if (errPath.Length > 0 && errPath[errPath.Length-1] != Path.DirectorySeparatorChar)
                            errPath += Path.DirectorySeparatorChar;
                        errPath += Bind.FailPrefix + baseName;
                        try
                        { Directory.Move (newPath, errPath); }
                        catch (Exception)
                        { /* discard all */ }
                    }
                }

                if (! RipModel.Bind.IsWip)
                    ReportLine (RipModel.Bind.Trailer, RipModel.Bind.Status, false);

                return RipModel.Bind.Status;
            }


            public override void ReportFormat (FormatBase fb, bool logErrorsToFile = false)
            {
                base.ReportFormat (fb);

                if (logErrorsToFile && fb.Issues.HasError)
                    try
                    {
                        using (var sw = new StreamWriter (Bind.CurrentDirectory + Path.DirectorySeparatorChar
                                                        + Bind.NoncompliantName, false, new UTF8Encoding (true)))
                        {
                            var dn = Path.GetDirectoryName (fb.Path);

                            sw.WriteLine ("Diagnostics by the vitriolic " + Bind.Product + " v" + Bind.ProductVersion + ":");
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
                                                        + Bind.NoncompliantName, false, new UTF8Encoding (true)))
                        {
                            sw.WriteLine ("Error found by the vitriolic " + Bind.Product + " v" + Bind.ProductVersion + ":");
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
        }


        public LameRip Rip { get; private set; }
        public Issue.Vector Issues { get; protected set; }
        public readonly FileFormat Mp3Format, LogFormat, M3uFormat, M3u8Format, Sha1xFormat;


        private LameDiags (string root, Granularity scope, FileFormat.Vector formats, Issue.Vector issues)
        {
            this.Root = root;
            this.Scope = scope;
            this.ErrEscalator = IssueTags.Substandard;
            this.FileFormats = formats;
            this.Issues = issues;

            Mp3Format = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="mp3");
            LogFormat = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="log");
            Sha1xFormat = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="sha1x");
            M3uFormat = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="m3u");
            M3u8Format = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="m3u8");
        }


        public string NoncompliantName { get { return "--NOT COMPLIANT WITH STANDARD--.txt"; } }
        public string FailPrefix { get { return "!!_ERRORS_!!"; } }
    }
}
#endif

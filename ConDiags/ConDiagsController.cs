//
// Product: Mediags
// File:    ConDiagsController.cs
//
// Copyright © 2015-2017 GitHub.com/Nongorilla
// MIT License - Use and redistribute freely
//

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using NongIssue;
using NongMediaDiags;
using NongFormat;

namespace AppController
{
    public interface IConDiagsViewFactory
    {
        void Create (ConDiagsController controller, Diags modelBind);
    }


    public class ConDiagsController
    {
        private IConDiagsViewFactory viewFactory;
        private string[] args;
        private Diags.Model model;

        private bool waitForKeyPress = false;
        private int? notifyEvery = null;
        public int NotifyEvery { get; private set; }

        private string filter = null;
        private string mirrorName = null;
        private string exclusion = null;
        private Interaction action = Interaction.None;
        private Granularity scope = Granularity.Advisory;
        private IssueTags warnEscalator = IssueTags.None;
        private IssueTags errEscalator = IssueTags.None;
        private Hashes hashes = Hashes.Intrinsic;
        private Validations validations = Validations.Exists | Validations.MD5 | Validations.SHA1;


        public ConDiagsController (string[] args, IConDiagsViewFactory viewFactory)
        {
            this.args = args;
            this.viewFactory = viewFactory;
        }

        public int Run()
        {
            int exitCode = ParseArgs (args);
            if (exitCode == 0)
            {
                NotifyEvery =  notifyEvery?? (scope < Granularity.Verbose? 0 : 1);

                if (mirrorName != null)
                    try
                    {
                        var mirrorWriter = new TextWriterTraceListener (mirrorName);
                        mirrorWriter.WriteLine (DateTime.Now);
                        Trace.Listeners.Add (mirrorWriter);
                    }
                    catch (Exception)
                    {
                        Console.Error.WriteLine ("Ignoring malformed <mirror>");
                    }

                if (scope <= Granularity.Verbose)
                { Trace.WriteLine (ProductText + " v" + VersionText); Trace.WriteLine (String.Empty); }

                model = new Diags.Model (args[args.Length-1], filter, exclusion, action, scope, warnEscalator, errEscalator);
                viewFactory.Create (this, model.Bind);

                model.Bind.HashFlags = hashes;
                model.Bind.ValidationFlags = validations;

                exitCode = (int) Severity.NoIssue;
                string err = null;
#if DEBUG
                exitCode = (int) model.CheckArg();
#else
                try
                {
                    exitCode = (int) model.CheckArg();
                }
                catch (IOException ex)
                { err = ex.Message; }
                catch (ArgumentException ex)
                { err = ex.Message; }

#endif
                if (err != null)
                {
                    exitCode = (int) Severity.Fatal;
                    Console.Error.WriteLine ("* Error: " + err);
                }
            }

            if (waitForKeyPress)
            {
                Console.WriteLine();
                Console.Write ("Press the escape key to exit...");
                while (Console.ReadKey().Key != ConsoleKey.Escape)
                { }
            }

            return exitCode;
        }


        private int ParseArgs (string[] Args)
        {

            if (args.Length==0 || args[0]=="/?" || args[0]=="/help" || args[0]=="-help")
            {
                ShowUsage();
                return 1;
            }

            for (int an = 0; an < args.Length-1; ++an)
            {
                bool argOk = false;

                if (args[an]==@"/R")
                {
                    action = Interaction.PromptToRepair;
                    argOk = true;
                }
                else if (args[an].StartsWith ("/f:"))
                {
                    filter = args[an].Substring (3);
                    argOk = true;
                }
                else if (args[an].StartsWith ("/g:"))
                {
                    var arg = Granularity.Summary;
                    argOk = Enum.TryParse<Granularity> (args[an].Substring (3), true, out arg);
                    argOk = argOk && Enum.IsDefined (typeof (Granularity), arg);
                    if (argOk)
                        scope = arg;
                }
                else if (args[an].StartsWith ("/h:"))
                {
                    var arg = Hashes.None;
                    argOk = Enum.TryParse<Hashes> (args[an].Substring (3), true, out arg);
                    if (argOk)
                        hashes = arg;
                }
                else if (args[an].StartsWith ("/out:"))
                {
                    mirrorName = args[an].Substring (5).Trim(null);
                    argOk = mirrorName.Length > 0;
                }
                else if (args[an].StartsWith ("/v:"))
                {
                    var arg = Validations.None;
                    argOk = Enum.TryParse<Validations> (args[an].Substring (3), true, out arg);
                    if (argOk)
                        validations = arg;
                }
                else if (args[an].StartsWith ("/w:"))
                {
                    var arg = IssueTags.None;
                    argOk = Enum.TryParse<IssueTags> (args[an].Substring (3), true, out arg);
                    if (argOk)
                        warnEscalator = arg;
                }
                else if (args[an].StartsWith ("/e:"))
                {
                    var arg = IssueTags.None;
                    argOk = Enum.TryParse<IssueTags> (args[an].Substring (3), true, out arg);
                    if (argOk)
                        errEscalator = arg;
                }
                else if (args[an].StartsWith ("/p:"))
                {
                    int arg;
                    argOk = int.TryParse (args[an].Substring (3), out arg);
                    if (argOk)
                        notifyEvery = arg;
                }
                else if (args[an].StartsWith ("/x:"))
                {
                    var arg = args[an].Substring (3);
                    argOk = ! String.IsNullOrWhiteSpace (arg);
                    if (argOk)
                        exclusion = arg;
                }
                else if (args[an] == "/k")
                {
                    waitForKeyPress = true;
                    argOk = true;
                }

                if (! argOk)
                {
                    Console.Error.WriteLine ("Invalid argument: " + args[an]);
                    return 1;
                }
            }

            return 0;
        }


        public static string ProductText
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                object[] attributes = assembly.GetCustomAttributes (typeof (AssemblyProductAttribute), false);
                return attributes.Length == 0? "" : ((AssemblyProductAttribute) attributes[0]).Product;
            }
        }


        public static string VersionText
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                string result = assembly.GetName().Version.ToString();
                if (result.Length > 3 && result.EndsWith (".0"))
                    result = result.Substring (0, result.Length-2);
                return result;
            }
        }


        private static void ShowUsage()
        {
            string exe = Process.GetCurrentProcess().ProcessName;

            Console.WriteLine (ProductText + " v" + VersionText);
            Console.WriteLine ();
            Console.WriteLine ("Usage:");
            Console.WriteLine (exe + " [/R] [/f:<wildcard>] [/g:<granularity>] [/h:<hashes>] [/v:<validations>] [/w:<escalators>] [/e:<escalators>] [/out:<mirror>] [/p:<counter>] [/x:<exclusion>] [/k] <fileOrDirectory>");

            Console.WriteLine();
            Console.WriteLine("Where <fileOrDirectory> is a file or directory name without wildcards.");
            Console.WriteLine();

            Console.Write ("Where <granularity> from");
            foreach (var name in Enum.GetNames (typeof (Granularity)))
                Console.Write (" " + name);
            Console.WriteLine();

            Console.WriteLine();
            Console.Write ("Where <hashes> is list of");
            foreach (var name in Enum.GetNames (typeof (Hashes)))
                Console.Write (" " + name);
            Console.WriteLine();

            Console.WriteLine();
            Console.Write ("Where <validations> is list of");
            foreach (var name in Enum.GetNames (typeof (Validations)))
                Console.Write (" " + name);
            Console.WriteLine();

            Console.WriteLine();
            Console.Write ("Where <escalators> is list of");
            var groupNames = Enum.GetNames (typeof (IssueTags));
            for (var di = 1; di < groupNames.Length; ++di)
            {
                IssueTags tag;
                Enum.TryParse<IssueTags> (groupNames[di], true, out tag);
                if (((int) tag & 0x00FFFFFF) != 0)
                    Console.Write (" " + groupNames[di]);
            }
            Console.WriteLine ();
            Console.WriteLine ();
            Console.WriteLine ("Example switches:");

            Console.WriteLine ();
            Console.WriteLine ("Use /e:substandard to error on lower quality encodings.");

            Console.WriteLine ();
            Console.WriteLine ("Use /f:*.log to only diagnose files with .log ending.");

            Console.WriteLine ();
            Console.WriteLine ("Use /g:detail to display maximum diagnostics.");

            Console.WriteLine ();
            Console.WriteLine ("Use /h:FileMD5,FileSHA1 to calculate file MD5 and SHA1 hashes.");

            Console.WriteLine ();
            Console.WriteLine ("Use /k to wait for keypress before exiting.");

            Console.WriteLine ();
            Console.WriteLine ("Use /out:results.txt to copy output to the results.txt file.");

            Console.WriteLine ();
            Console.WriteLine ("Use /p:0 to suppress the progress counter.");

            Console.WriteLine ();
            Console.WriteLine ("Use /v:0 to only parse digests and perform no hash checks.");

            Console.WriteLine ();
            Console.WriteLine ("Use /x:@ to ignore all paths that include the ampersand.");

            Console.WriteLine ();
            Console.WriteLine ("Description:");
            Console.WriteLine ();

            foreach (var line in helpText)
                Console.WriteLine (line);

            // Create a dummy model just to get a format list.
            var model = new Diags.Model (null);

            Console.WriteLine ();
            Console.WriteLine ("The following file extensions are supported:");
            Console.WriteLine (model.Bind.FormatListText);
        }


        private static readonly string[] helpText =
{
"This program performs diagnostics on the supplied file or on all eligible",
"files in the supplied directory and its subdirectories.  Diagnostics may be",
"extensive or just a simple magic number test.  The most extensive diagnostics",
"are performed on .mp3 and .flac files which include CRC verification.",
"",
"Some issues may be repaired.  No repairs will be made unless the /R switch is",
"given *and* each repair is confirmed.  These are the repairable issues:",
"1. A phantom .mp3 ID3v1 tag.",
"2. EAC-induced bug that sometimes creates an .mp3 with a bad ID3v2 tag size.",
"3. End-of-file watermarks on .avi, .mp4, .mkv files.",
"4. Incorrect extensions.",
"",
"Use the /h switch to generate any combination of hashes or use /h:None to",
"disable all hash calculations including CRC verifications."
};
    }
}

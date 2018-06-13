//
// Product: UberFLAC
// File:    ConFlacController.cs
//
// Copyright © 2015-2018 github.com/Nongorilla
// MIT License - Use and redistribute freely
//

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NongIssue;
using NongMediaDiags;
using NongFormat;

namespace AppController
{
    public interface IConFlacViewFactory
    {
        void Create (ConFlacController controller, FlacDiags modelBind);
    }


    public class ConFlacController
    {
        private static readonly Granularity maxGranularity = Granularity.Quiet;

        private IConFlacViewFactory viewFactory;
        private string[] args;
        private FlacDiags.Model model;

        private string mirrorName;
        private bool waitForKeyPress = false;
        private string signature = null;
        private int? notifyEvery = null;
        public int NotifyEvery { get; private set; }
        public string DetailSeparator => "---- ---- ---- ---- ---- ----";
        public string SessionSeparator => "==== ==== ==== ==== ==== ====";


        public ConFlacController (string[] args, IConFlacViewFactory viewFactory)
        {
            this.args = args;
            this.viewFactory = viewFactory;
        }


        public int Run()
        {
            model = new FlacDiags.Model (args.Length == 0? null : args[args.Length-1], Granularity.Advisory);
            viewFactory.Create (this, model.Bind);

            int exitCode = ParseArgs();
            if (exitCode == 0)
            {
                NotifyEvery = notifyEvery?? (model.Bind.Scope <= Granularity.Verbose? 0 : 1);

                if (mirrorName != null)
                    try
                    {
                        var mirrorWriter = new TextWriterTraceListener (mirrorName);
                        mirrorWriter.WriteLine (String.Empty);
                        mirrorWriter.WriteLine (SessionSeparator);
                        mirrorWriter.WriteLine (DateTime.Now);
                        Trace.Listeners.Add (mirrorWriter);
                    }
                    catch (Exception)
                    {
                        Console.Error.WriteLine ("Ignoring malformed <mirror>");
                    }

                if (model.Bind.Scope <= Granularity.Verbose)
                { Trace.WriteLine (ProductText + " v" + VersionText); Trace.WriteLine (String.Empty); }

                Severity worstDiagnosis = model.ValidateFlacsDeep (signature);
                exitCode = worstDiagnosis < Severity.Warning? 0 : (int) worstDiagnosis;

                if (model.Bind.TotalSignable > 0)
                {
                    var prefix = model.Bind.TotalSignable == 1? "+ Rip is" : "+ " + model.Bind.TotalSignable + " rips are";
                    Trace.WriteLine (String.Empty);
                    Trace.Write (prefix);
                    Trace.WriteLine (" uber but cannot sign without /sig:<signature>");
                }
            }

            model.Dispose();
            if (waitForKeyPress)
            {
                Console.WriteLine();
                Console.Write ("Press the escape key to exit...");
                while (Console.ReadKey().Key != ConsoleKey.Escape)
                { }
            }

            return exitCode;
        }


        private int ParseArgs()
        {
            if (args.Length==0 || args[0]=="/?" || args[0]=="/help" || args[0]=="-help")
            {
                ShowUsage();
                return 1;
            }

            for (var an = 0; an < args.Length-1; ++an)
            {
                var argOK = false;

                if (args[an].StartsWith ("/sig:"))
                {
                    signature = args[an].Substring (5);
                    argOK = signature.Length > 0
                            && ! Char.IsPunctuation (signature[0])
                            && ! signature.Any (ch => Char.IsWhiteSpace (ch))
                            && signature.IndexOfAny (Path.GetInvalidFileNameChars()) < 0;
                }
                else if (args[an] == "/autoname")
                {
                    model.Bind.Autoname = NamingStrategy.ArtistTitle;
                    argOK = true;
                }
                else if (args[an].StartsWith ("/autoname:"))
                {
                    argOK = Enum.TryParse<NamingStrategy> (args[an].Substring (10), true, out NamingStrategy arg);
                    argOK = argOK && Enum.IsDefined (typeof (NamingStrategy), arg);
                    if (argOK)
                        model.Bind.Autoname = arg;
                }
                else if (args[an].StartsWith ("/g:"))
                {
                    argOK = Enum.TryParse<Granularity> (args[an].Substring (3), true, out Granularity arg);
                    argOK = argOK && Enum.IsDefined (typeof (Granularity), arg) && arg <= maxGranularity;
                    if (argOK)
                        model.Bind.Scope = arg;
                }
                else if (args[an] == "/md5")
                {
                    argOK = true;
                    model.Bind.IsParanoid = true;
                }
                else if (args[an].StartsWith ("/out:"))
                {
                    mirrorName = args[an].Substring (5).Trim(null);
                    argOK = mirrorName.Length > 0;
                }
                else if (args[an].StartsWith ("/p:"))
                {
                    argOK = int.TryParse (args[an].Substring (3), out int arg);
                    if (argOK)
                        notifyEvery = arg;
                }
                else if (args[an] == "/k")
                {
                    argOK = true;
                    waitForKeyPress = true;
                }
                else if (args[an] == "/rg")
                {
                    argOK = true;
                    model.Bind.ApplyRG = true;
                }
                else if (args[an] == "/prove")
                {
                    argOK = true;
                    model.Bind.WillProve = true;
                }
                else if (args[an] == "/prove:web")
                {
                    argOK = true;
                    model.Bind.WillProve = true;
                    model.Bind.IsWebCheckEnabled = true;
                }
                else if (args[an].StartsWith ("/safety:"))
                {
                    argOK = int.TryParse (args[an].Substring (8), out int arg);
                    if (argOK)
                        model.Bind.StopAfter = arg;
                }
                else if (args[an] == "/ubertags")
                {
                    argOK = true;
                    model.Bind.IsBestTags = true;
                }
                else if (args[an] == "/fussy")
                {
                    argOK = true;
                    model.Bind.IsFussy = true;
                }

                if (! argOK)
                {
                    Console.Error.WriteLine ("Invalid argument: " + args[an]);
                    return 1;
                }
            }

            if (args.Length > 0 && args[args.Length-1].StartsWith ("/") && Path.DirectorySeparatorChar != '/')
            {
                Console.Error.WriteLine ("Invalid argument, expecting a directory last.");
                return 1;
            }

            if (signature == null)
            {
                if (model.Bind.Autoname != NamingStrategy.Manual)
                    Console.Error.WriteLine ("/autoname without /sig ignored.");
            }
            else if (model.Bind.Scope > Granularity.Advisory)
            {
                Console.Error.WriteLine ("/g:" + model.Bind.Scope + " with /sig ignored.");
                model.Bind.Scope = Granularity.Advisory;
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
            Console.WriteLine (exe + " [/autoname[:<strategy>]] [/fussy] [/g:<granularity>] [/k] [/md5] [/out:<mirror>] [/p:<n>] [/prove[:web]] [/rg] [/safety:<n>] [/sig:<signature>] [/ubertags] <directory>");

            Console.WriteLine ();
            Console.WriteLine ("Where <directory> is a relative or absolute directory name without wildcards.");
            Console.WriteLine ("Where <strategy> from " + String.Join (", ", Enum.GetNames (typeof (NamingStrategy))) + ".");
            Console.Write ("Where <granularity> from ");
            for (Granularity gx = Granularity.Detail; gx < maxGranularity; ++gx)
            { Console.Write (gx.ToString()); Console.Write (", "); }
            Console.Write (maxGranularity.ToString());
            Console.WriteLine (".");

            Console.WriteLine ();
            Console.WriteLine ("Use /autoname to rename directory and files based on tags.");

            Console.WriteLine ();
            Console.WriteLine ("Use /fussy to escalate rip acceptability issues.");

            Console.WriteLine ();
            Console.WriteLine ("Use /g:verbose to get more feedback.");

            Console.WriteLine ();
            Console.WriteLine ("Use /k to wait for keypress before exiting.");

            Console.WriteLine ();
            Console.WriteLine ("Use /md5 to additionally perform MD5 checks of FLACs.");

            Console.WriteLine ();
            Console.WriteLine ("Use /out:results.txt to mirror output to results.txt.");

            Console.WriteLine ();
            Console.WriteLine ("Use /p:0 to suppress the progress counter.");

            Console.WriteLine ();
            Console.WriteLine ("Use /prove to escalate rip integrity issues.");

            Console.WriteLine ();
            Console.WriteLine ("Use /prove:web to escalate rip issues and verify EAC 1.x log self-hashes online.");

            Console.WriteLine ();
            Console.WriteLine ("Use /rg to add ReplayGain (using metaflac.exe) on first signing.");

            Console.WriteLine ();
            Console.WriteLine ("Use /safety:0 to disable prompting after consecutive invalidations (default is 3).");

            Console.WriteLine ();
            Console.WriteLine ("Use /sig:SIG to sign (rename) EAC .log and create .md5 digest.");

            Console.WriteLine ();
            Console.WriteLine ("Use /ubertags to escalate substandard tagging issues.");

            Console.WriteLine ();
            Console.WriteLine ("Description:");
            Console.WriteLine ();

            foreach (var line in helpText)
                Console.WriteLine (line);
        }


        private static readonly string[] helpText =
{
"This program analyzes directories containing FLAC files that were ripped",
"from CDs using the EAC program.  Deep analysis is performed on the EAC",
"log file and the FLAC files for correctness.  Directories may be marked as",
"enforced by renaming the log file to include a signature and generating an",
"MD5 hash file.",
"",
"EAC correctness requires that CDs are ripped in secure mode with no C2 and",
"every rip is error free.  If test CRCs are generated they must match their",
"copy CRCs.  There must be only one log file per directory.",
"",
"FLAC correctness requires valid internal CRC checks and that the CRC-32 of",
"the decompressed audio matches the CRC-32 given in the EAC log.  All tracks",
"listed in the log must sequentially match a corresponding FLAC file without",
"gaps.  There must be no extra FLAC files in the directory."
};
    }
}

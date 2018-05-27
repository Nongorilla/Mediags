//
// Product: UberLAME
// File:    ConLameController.cs
//
// Copyright © 2017-2018 GitHub.com/Nongorilla
// MIT License - Use and redistribute freely
//

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NongIssue;
using NongMediaDiags;

namespace AppController
{
    public interface IConLameViewFactory
    {
        void Create (ConLameController controller, Diags modelBind);
    }


    public class ConLameController
    {
        private static readonly Granularity maxGranularity = Granularity.Quiet;

        private IConLameViewFactory viewFactory;
        private string[] args;
        private LameDiags.Model model = null;

        private string mirrorName;
        private bool waitForKeyPress = false;
        private string signature = null;
        private string logTag = null;
        private int? notifyEvery = null;
        public int NotifyEvery { get; private set; }


        public ConLameController (string[] args, IConLameViewFactory viewFactory)
        {
            this.args = args;
            this.viewFactory = viewFactory;
        }


        public int Run()
        {
            model = new LameDiags.Model (args.Length == 0? null : args[args.Length-1], Granularity.Advisory);
            model.Bind.ErrEscalator = IssueTags.Substandard|IssueTags.Overstandard;
            viewFactory.Create (this, model.Bind);

            int exitCode = ParseArgs();
            if (exitCode == 0)
            {
                NotifyEvery = notifyEvery?? (model.Bind.Scope <= Granularity.Verbose? 0 : 1);

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

                if (model.Bind.Scope <= Granularity.Verbose)
                { Trace.WriteLine (ProductText + " v" + VersionText); Trace.WriteLine (String.Empty); }

                Severity worstDiagnosis = model.ValidateLameRipsDeep (signature, logTag);
                exitCode = worstDiagnosis < Severity.Warning? 0 : (int) worstDiagnosis;
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
                else if (args[an] == "/fussy")
                {
                    argOK = true;
                    model.Bind.IsFussy = true;
                }
                else if (args[an].StartsWith ("/g:"))
                {
                    argOK = Enum.TryParse<Granularity> (args[an].Substring (3), true, out Granularity arg);
                    argOK = argOK && Enum.IsDefined (typeof (Granularity), arg) && arg <= maxGranularity;
                    if (argOK)
                        model.Bind.Scope = arg;
                }
                else if (args[an] == "/k")
                {
                    argOK = true;
                    waitForKeyPress = true;
                }
                else if (args[an].StartsWith ("/logtag:"))
                {
                    logTag = args[an].Substring (8).Trim(null);
                    argOK = ! logTag.Any (ch => Char.IsWhiteSpace (ch))
                            && logTag.IndexOfAny (Path.GetInvalidFileNameChars()) < 0;
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
                else if (args[an] == "/verify")
                {
                    argOK = true;
                    model.Bind.WillProve = true;
                }
                else if (args[an] == "/verify:web")
                {
                    argOK = true;
                    model.Bind.WillProve = true;
                    model.Bind.IsWebCheckEnabled = true;
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
                if (logTag != null)
                    Console.Error.WriteLine ("/logtag without /sig ignored.");
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
            Console.WriteLine (exe + " [/g:<granularity>] [/k] [/out:<mirror>] [/p:<counter>] [/sig:<signature>] [/verify[:web]] <directory>");
            Console.WriteLine ();
            Console.WriteLine ("Where <directory> is a relative or absolute directory name without wildcards.");
            Console.Write ("Where <granularity> from ");
            for (Granularity gx = Granularity.Detail; gx < maxGranularity; ++gx)
            { Console.Write (gx.ToString()); Console.Write (", "); }
            Console.Write (maxGranularity.ToString());
            Console.WriteLine (".");

            Console.WriteLine ();
            Console.WriteLine ("Use /g:verbose to get more feedback.");

            Console.WriteLine ();
            Console.WriteLine ("Use /fussy to escalate rip acceptability issues.");

            Console.WriteLine ();
            Console.WriteLine ("Use /k to wait for keypress before exiting.");

            Console.WriteLine ();
            Console.WriteLine ("Use /out:results.txt to mirror output to results.txt.");

            Console.WriteLine ();
            Console.WriteLine ("Use /p:0 to suppress the progress counter.");

            Console.WriteLine ();
            Console.WriteLine ("Use /sig:<signature> to sign (rename) log file and create md5 file.");

            Console.WriteLine ();
            Console.WriteLine ("Use /logtag:<tag> to include <tag> in EAC log file name.");

            Console.WriteLine ();
            Console.WriteLine ("Use /verify:web to verify EAC 1.x log self-hash online.");

            Console.WriteLine ();
            Console.WriteLine ("Description:");
            Console.WriteLine ();

            foreach (var line in helpText)
                Console.WriteLine (line);
        }


        private static readonly string[] helpText =
{
"This program analyzes directories containing MP3 files that were ripped from",
"CDs using the EAC program.  Deep analysis is performed on the EAC log file and",
"the MP3 files for correctness.  The contents of any .m3u or .m3u8 file is also",
"verified.  If the /sig switch is supplied, directories will be marked as valid",
"by generating a .sha1x hash file for each rip.",
"",
"EAC correctness requires that CDs are ripped in secure mode with no C2 and",
"every rip is error free.  There must be only one log file per directory."
};
    }
}

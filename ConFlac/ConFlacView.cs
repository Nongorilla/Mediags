//
// Product: UberFLAC
// File:    ConFlacView.cs
//
// Copyright © 2015-2018 github.com/Nongorilla
// MIT License - Use and redistribute freely
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NongIssue;
using NongFormat;
using NongMediaDiags;
using AppController;

namespace AppView
{
    public class ConFlacViewFactory : IConFlacViewFactory
    {
        public void Create (ConFlacController controller, FlacDiags modelBind)
        {
            var view = new ConFlacView (controller, modelBind);
        }
    }


    public class ConFlacView
    {
        private ConFlacController controller;
        private FlacDiags modelBind;

        private bool isProgressLast = false;
        private int totalFilesReported = 0;
        private string curDir = null, curFile = null;
        private bool dirShown = false, fileShown = false;

        public string ProgressEraser => "\r              \r";


        static int Main (string[] args)
        {
            var consoleWriter = new ConsoleTraceListener (false);
            Trace.Listeners.Add (consoleWriter);
            Trace.AutoFlush = true;

            var controller = new ConFlacController (args, new ConFlacViewFactory());
            return controller.Run();
        }


        public ConFlacView (ConFlacController controller, FlacDiags modelBind)
        {
            this.controller = controller;
            this.modelBind = modelBind;
            this.modelBind.QuestionAsk = Question;
            this.modelBind.InputLine = Input;
            this.modelBind.MessageSend += Logger;
            this.modelBind.ReportClose += Summarize;
            this.modelBind.FileVisit += FileProgress;
            this.modelBind.Product = ConFlacController.ProductText;
            this.modelBind.ProductVersion = ConFlacController.VersionText;
        }


        private void Logger (string message, Severity severity, Likeliness repairability)
        {
            string prefix;
            if (severity == Severity.NoIssue)
                prefix = String.Empty;
            else if (severity <= Severity.Trivia || (severity <= Severity.Advisory && ! String.IsNullOrEmpty (modelBind.CurrentFile)))
                prefix = "  ";
            else if (severity <= Severity.Advisory)
                prefix = "+ ";
            else
                prefix = severity <= Severity.Warning? "- " : "* ";
            if (! String.IsNullOrEmpty (modelBind.CurrentFile) && severity >= Severity.Warning)
                prefix += Enum.GetName (typeof (Severity), severity) + ": ";

            if (isProgressLast)
            {
                Console.Error.Write (ProgressEraser);
                isProgressLast = false;
            }

            if (! fileShown)
            {
                fileShown = true;

                if (totalFilesReported != 0)
                {
                    if (modelBind.Scope < Granularity.Verbose)
                    { Trace.WriteLine (String.Empty); Trace.WriteLine (controller.DetailSeparator); }
                    else if (! dirShown)
                        Trace.WriteLine (String.Empty);
                }

                if (! dirShown)
                {
                    dirShown = true;

                    if (modelBind.CurrentDirectory.Length > 0)
                    {
                        Trace.Write (modelBind.CurrentDirectory);
                        if (modelBind.CurrentDirectory[modelBind.CurrentDirectory.Length-1] != Path.DirectorySeparatorChar)
                            Trace.Write (Path.DirectorySeparatorChar);
                    }
                    Trace.WriteLine (String.Empty);
                }

                Trace.WriteLine (modelBind.CurrentFile);
            }

            if (message != null)
            {
                if (prefix != null)
                    Trace.Write (prefix);
                Trace.WriteLine (message);
            }
            else if (controller.NotifyEvery != 0 && modelBind.TotalFiles % controller.NotifyEvery == 0)
                WriteChecked();

            ++totalFilesReported;
        }


        private void Summarize()
        {
            if (controller.NotifyEvery != 0)
                Console.Error.Write (ProgressEraser);

            if (totalFilesReported != 0)
            { Trace.WriteLine (String.Empty); Trace.WriteLine (controller.DetailSeparator); }

            var rollups = modelBind.GetRollups (new List<string>(), "checked");
            foreach (var lx in rollups)
                Trace.WriteLine (lx);
        }


        private void FileProgress (string dirName, string fileName)
        {
            if (curDir != dirName)
            {
                curDir = dirName;
                dirShown = false;
                curFile = fileName;
                fileShown = false;
            } else if (curFile != fileName)
            {
                curFile = fileName;
                fileShown = false;
            }
            else
                return;

            if (controller.NotifyEvery != 0 && modelBind.TotalFiles % controller.NotifyEvery == 0)
                WriteChecked();
        }


        private void WriteChecked()
        {
            Console.Error.Write ("Checked ");
            Console.Error.Write (modelBind.TotalFiles);
            Console.Error.Write ('\r');
            isProgressLast = true;
        }


        private string previousResponse = null;
        private int sameInputCount = 0;
        private bool inputAll = false;

        public string Input (string dialogText, string prompt, string reallyAllPrompt)
        {
            if (dialogText != null)
            {
                Trace.WriteLine (String.Empty);
                Trace.Write ("+ ");
                Trace.WriteLine (dialogText);
            }

            if (inputAll)
            {
                Trace.WriteLine (String.Empty);
                Trace.Write ("Reusing previous response: ");
                Trace.WriteLine (previousResponse);
                return previousResponse;
            }

            if (sameInputCount >= 2)
            {
                Trace.WriteLine (String.Empty);
                Trace.WriteLine ("Previous responses were identical:");
                Trace.WriteLine (String.Empty);
                Trace.WriteLine (previousResponse);

                for (;;)
                {
                    Trace.WriteLine (String.Empty);
                    Trace.Write ("Don't use it this time (N) / Use it again (Y) / Use it for all remaining (A)? ");
                    string choice = Console.ReadLine().ToUpper();
                    if (choice == "N")
                        break;
                    else if (choice == "Y")
                        return previousResponse;
                    else if (choice == "A")
                    {
                        Trace.Write (reallyAllPrompt);
                        choice = Console.ReadLine().ToUpper();
                        if (choice != "Y")
                            break;

                        inputAll = true;
                        return previousResponse;
                    }

                    Trace.WriteLine ("Invalid response.");
                }
            }

            Console.Error.WriteLine();
            Console.Error.Write (prompt);
            Console.Error.Write (": ");

            string response = Console.ReadLine();
            if (response == previousResponse)
                ++sameInputCount;
            else
            {
                previousResponse = response;
                sameInputCount = 1;
            }

            return response;
        }


        public bool? Question (string prompt)
        {
            for (;;)
            {
                if (prompt != null)
                    Console.Error.Write (prompt);

                string response = Console.ReadLine().ToLower();

                if (response == "n" || response == "no")
                    return false;
                if (response == "y" || response == "yes")
                    return true;
            }
        }
    }
}

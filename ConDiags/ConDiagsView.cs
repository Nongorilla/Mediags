//
// Product: Mediags
// File:    ConDiagsView.cs
//
// Copyright © 2015-2017 GitHub.com/Nongorilla
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
    public class ConDiagsViewFactory : IConDiagsViewFactory
    {
        public void Create (ConDiagsController controller, Diags modelBind)
        {
            var view = new ConDiagsView (controller, modelBind);
        }
    }


    public class ConDiagsView
    {
        private ConDiagsController controller;
        private Diags modelBind;

        private bool isProgressLast = false;
        private int totalFilesReported = 0;
        private string curDir = null, curFile = null;
        private bool dirShown = false, fileShown = false;

        public string DetailSeparator
        { get { return "---- ---- ---- ---- ----"; } }

        public string ProgressEraser
        { get { return "\r              \r"; } }


        static int Main (string[] args)
        {
            var consoleWriter = new ConsoleTraceListener (false);
            Trace.Listeners.Add (consoleWriter);
            Trace.AutoFlush = true;

            var controller = new ConDiagsController (args, new ConDiagsViewFactory());
            return controller.Run();
        }


        public ConDiagsView (ConDiagsController controller, Diags modelBind)
        {
            this.controller = controller;
            this.modelBind = modelBind;
            this.modelBind.QuestionAsk = Question;
            this.modelBind.MessageSend += Logger;
            this.modelBind.ReportClose += Summarize;
            this.modelBind.FileVisit += FileProgress;
        }


        private void Logger (string message, Severity severity, Likeliness repairability)
        {
            string prefix;
            if (repairability == Likeliness.Probable)
                prefix = "~ ";
            else if (severity == Severity.NoIssue)
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
                    if (modelBind.Scope < Granularity.Verbose)
                    { Trace.WriteLine (String.Empty); Trace.WriteLine (DetailSeparator); }
                    else if (! dirShown)
                        Trace.WriteLine (String.Empty);

                if (! dirShown)
                {
                    dirShown = true;

                    if (! String.IsNullOrEmpty (modelBind.CurrentDirectory))
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

            if (totalFilesReported > 1 || modelBind.Scope >= Granularity.Verbose)
            {
                if (totalFilesReported > 0)
                { Trace.WriteLine (String.Empty); Trace.WriteLine (DetailSeparator); }

                var rollups = modelBind.GetRollups (new List<string>(), "diagnosed");
                foreach (var lx in rollups)
                    Trace.WriteLine (lx);
            }
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


        public bool? Question (string prompt)
        {
            for (;;)
            {
                if (prompt != null)
                    Trace.Write (prompt);

                string response = Console.ReadLine().ToLower();

                if (response == "n" || response == "no")
                    return false;
                if (response == "y" || response == "yes")
                    return true;
            }
        }
    }
}

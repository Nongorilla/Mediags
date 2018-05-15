//
// Product: UberFLAC
// File:    WpfFlacView.xaml.cs
//
// Copyright © 2015-2018 Nongorilla GitHub.com/Nongorilla
// MIT License - Use and redistribute freely
//

using System;
using System.IO;
using System.Windows;
using NongMediaDiags;
using NongIssue;
using NongFormat;
using AppController;

namespace AppView
{
    public partial class App : Application
    {
        protected override void OnStartup (StartupEventArgs supArgs)
        {
            base.OnStartup (supArgs);
            MainWindow = new WpfFlacController (supArgs.Args, new WpfFlacViewFactory());
            MainWindow.Show();
        }
    }


    public class WpfFlacViewFactory : IWpfFlacViewFactory
    {
        public void Create (WpfFlacController controller, FlacDiags modelBind)
        {
            var view = new WpfFlacView (controller, modelBind);
        }
    }


    public class WpfFlacView
    {
        private WpfFlacController controller;
        private FlacDiags modelBind;

        private string curDir = null, curFile = null;
        private bool dirShown = false, fileShown = false;

        public WpfFlacView (WpfFlacController controller, FlacDiags modelBind)
        {
            this.controller = controller;
            this.modelBind = modelBind;

            this.modelBind.InputLine = this.QuestionText;
            this.modelBind.MessageSend += this.Logger;
            this.modelBind.FileVisit += this.FileProgress;

            this.modelBind.Product = WpfFlacController.ProductText;
            this.modelBind.ProductVersion = WpfFlacController.VersionText;
        }


        private void Logger (string message, Severity severity, Likeliness repairability)
        {
            if (! controller.Dispatcher.CheckAccess())
            {
                controller.Dispatcher.Invoke
                (
                    new Action<string, Severity, Likeliness> ((m,s,r) => Logger (m,s,r)),
                    new object[] { message, severity, repairability }
                );
                return;
            }

            string prefix;
            if (severity == Severity.NoIssue)
                prefix = String.Empty;
            else if (severity <= Severity.Trivia || (severity <= Severity.Advisory && ! String.IsNullOrEmpty (modelBind.CurrentFile)))
                prefix = "  ";
            else if (severity <= Severity.Advisory)
                prefix = "+ ";
            else
                prefix = severity <= Severity.Warning? "- " : "* ";

            if (! fileShown)
            {
                fileShown = true;

                if (! String.IsNullOrEmpty (controller.consoleBox.Text))
                    if (modelBind.Scope < Granularity.Verbose)
                        controller.consoleBox.AppendText ("\n-- -- -- -- -- -- -- --\n");
                    else if (! dirShown)
                        controller.consoleBox.AppendText ("\n");

                if (! dirShown)
                {
                    dirShown = true;

                    if (! String.IsNullOrEmpty (modelBind.CurrentDirectory))
                    {
                        controller.consoleBox.AppendText (modelBind.CurrentDirectory);
                        if (! modelBind.CurrentDirectory.EndsWith (Path.DirectorySeparatorChar.ToString()))
                            controller.consoleBox.AppendText (Path.DirectorySeparatorChar.ToString());
                    }
                    controller.consoleBox.AppendText ("\n");
                }

                controller.consoleBox.AppendText (modelBind.CurrentFile);
                controller.consoleBox.AppendText ("\n");
            }

            if (message != null)
            {
                controller.consoleBox.AppendText (prefix);
                if (! String.IsNullOrEmpty (modelBind.CurrentFile) && severity >= Severity.Warning)
                    controller.consoleBox.AppendText (Enum.GetName (typeof (Severity), severity) + ": ");
                controller.consoleBox.AppendText (message);
                controller.consoleBox.AppendText ("\n");
            }

            controller.consoleBox.ScrollToEnd();
        }


        int prevToGo = 0;
        private void FileProgress (string dirName, string fileName)
        {
            if (! controller.Dispatcher.CheckAccess())
            {
                controller.Dispatcher.Invoke
                (
                    new Action<string,string> ((d,f) => FileProgress (d,f)),
                    new object[] { dirName, fileName }
                );
                return;
            }

            if (curDir != modelBind.CurrentDirectory)
            {
                curDir = modelBind.CurrentDirectory;
                dirShown = false;
            }
            curFile = modelBind.CurrentFile;
            fileShown = false;

            if (modelBind.Rip != null)
            {
                modelBind.NotifyPropertyChanged (null);
                int toGo = modelBind.Rip.Status >= Severity.Error? 0 : modelBind.ExpectedFiles - modelBind.TotalFiles;
                if (toGo != prevToGo)
                {
                    prevToGo = toGo;
                    controller.signal1Box.Text = toGo <= 0? String.Empty : toGo.ToString();
                }
            }
        }


        private string QuestionText (string s1, string s2, string s3)
        {
            if (! controller.Dispatcher.CheckAccess())
            {
                var result = controller.Dispatcher.Invoke
                (
                    new Action<string,string,string> ((t1,t2,t3) => QuestionText (t1,t2,t3)),
                    new object[] { s1, s2, s3 }
                );

                return (string) result;
            }

            controller.commentPnl.Visibility = Visibility.Visible;
            controller.commentBox.Focusable = true;
            controller.commentBox.Focus();
            controller.CommitBtn.IsDefault = true;
            return null;
        }
    }
}

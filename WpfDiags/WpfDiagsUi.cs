using System;
using System.Windows.Controls;
using AppViewModel;
using NongFormat;
using NongIssue;

namespace AppController
{
    public partial class WpfDiagsUi : IUi
    {
        private readonly DiagsPresenter.Model model;
        private readonly WpfDiagsController view;
        private int totalLinesReported = 0;
        private string curDir = null, curFile = null;
        private bool dirShown = false, fileShown = false;

        public WpfDiagsUi (WpfDiagsController view, DiagsPresenter.Model model)
        {
            this.model = model;
            this.view = view;
        }

        public string CurrentFormat()
        {
            var tabHeaderText = (String) ((TabItem) ((TabControl) view.infoTabs).SelectedItem).Header;
            if (tabHeaderText.StartsWith ("."))
                return tabHeaderText.Substring (1);
            else
                return null;
        }

        public string BrowseFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog() { Filter="Media files (*.*)|*.*" };
            dlg.ShowDialog();
            return dlg.FileName;
        }

        public void FileProgress (string dirName, string fileName)
        {
            if (curDir != dirName)
            {
                curDir = dirName;
                dirShown = false;
                curFile = fileName;
                fileShown = false;
            }
            else if (curFile != fileName)
            {
                curFile = fileName;
                fileShown = false;
            }
        }

        public void ShowLine (string message, Severity severity, Likeliness repairability)
        {
            if (! fileShown)
            {
                fileShown = true;

                if (totalLinesReported != 0)
                    if (model.View.Scope < Granularity.Verbose)
                        view.consoleBox.AppendText ("\n\n---- ---- ----\n");
                    else if (! dirShown)
                        view.consoleBox.AppendText ("\n");

                if (! dirShown)
                {
                    dirShown = true;

                    if (! String.IsNullOrEmpty (model.View.CurrentDirectory))
                    {
                        view.consoleBox.AppendText (model.View.CurrentDirectory);
                        if (model.View.CurrentDirectory[model.View.CurrentDirectory.Length-1] != System.IO.Path.DirectorySeparatorChar)
                            view.consoleBox.AppendText (System.IO.Path.DirectorySeparatorChar.ToString());
                    }
                    view.consoleBox.AppendText ("\n");
                }

                view.consoleBox.AppendText (model.View.CurrentFile);
            }

            view.consoleBox.AppendText ("\n");
            view.consoleBox.AppendText (message);
            ++totalLinesReported;
        }

        public void SetText (string message)
        {
            view.consoleBox.Text = message;
            curDir = null; curFile = null;
            dirShown = false; fileShown = false;
            totalLinesReported = 0;
        }

        public void ConsoleZoom (int delta)
        {
            var newZoom = unchecked (view.consoleBox.FontSize + delta);
            if (newZoom >= 4 && newZoom <= 60)
                view.consoleBox.FontSize = newZoom;
        }
    }
}

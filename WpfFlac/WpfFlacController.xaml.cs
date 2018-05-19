//
// Product: UberFLAC
// File:    WpfFlacController.xaml.cs
//
// Copyright © 2015-2018 GitHub.com/Nongorilla
// MIT License - Use and redistribute freely
//

using System;
using System.Reflection;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using NongIssue;
using NongFormat;
using NongMediaDiags;

namespace AppController
{
    public class ComparisonConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        { return value.Equals (param); }

        public object ConvertBack (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        { return value.Equals (true) ? param : Binding.DoNothing; }
    }


    public class FlacJobArgs
    {
        public string Path;
        public string Signature;

        public FlacJobArgs (string path, string signature)
        { this.Path = path; this.Signature = signature; }
    }


    public interface IWpfFlacViewFactory
    {
        void Create (WpfFlacController controller, FlacDiags modelBind);
    }


    public partial class WpfFlacController : Window
    {
        private string[] args;
        private IWpfFlacViewFactory viewFactory;
        private FlacDiags.Model model;

        public static string UberGuide { get { return @"TRACKNUMBER
Required.  Must be digits only with no leading zeroes.  Must be sequential and the order must match the order of tracks in the EAC log.

ALBUM
Required.  Must be consistent for all tracks.

DATE
Required.  Must be consistent for all tracks. Must be in the form YYYY or YYYY-MM-DD where YYYY between 1900 and 2099.

RELEASE DATE
Optional.  If present, must be consistent for all tracks and in the form YYYY or YYYY-MM-DD where YYYY between 1900 and 2099.

ORIGINAL RELEASE DATE
Optional.  If present, must be in the form YYYY or YYYY-MM-DD where YYYY between 1900 and 2099.

TRACKTOTAL
Optional.  If present, must be digits only with no leading zeroes, consistent for all tracks, and equal to the number of tracks in the EAC log.

DISCNUMBER, DISCTOTAL
Optional.  If present, must be digits only with no leading zeroes and consistent for all tracks.

ALBUMARTIST
Required if ARTIST tags are missing or not consistent.  If present, must be consistent for all tracks.

ALBUMARTISTSORTORDER
Optional.  May contain multiple entries.  If present, all entries must be consistent for all tracks.

BARCODE, CATALOGNUMBER, COMPILATION, ORGANIZATION
Optional.  If present, must be consistent for all tracks.";
 } }

        public WpfFlacController (string[] args, IWpfFlacViewFactory viewFactory)
        {
            this.args = args;
            this.viewFactory = viewFactory;
            InitializeComponent();
        }


        void Job (object sender, DoWorkEventArgs jobArgs)
        {
            var args = (FlacJobArgs) jobArgs.Argument;
            jobArgs.Result = model.ValidateFlacRip (args.Path, args.Signature, true, false);
        }


        void JobCompleted (object sender, RunWorkerCompletedEventArgs args)
        {
            var rip = model.RipModel;
            if (rip == null)
                return;

            if (rip.LogModel != null)
            {
                rip.LogModel.SetGuiTracks();
                rip.LogModel.Bind.NotifyPropertyChanged ("GuiTracks");
            }

            if (! rip.Bind.IsWip)
            {
                rip.CloseFiles();
                model.SetCurrentDirectory (null);
            }
            else
                model.SetCurrentFile (null);

            ShowIssues (infoTabs.SelectedItem as TabItem);

            if (rip.Bind.Md5 != null)
                md5Ctrl.history.ScrollToEnd();
        }


        //
        // Constructor continuation:
        //
        private void Controller_Loaded (object sender, RoutedEventArgs e)
        {
            Title = ProductText + " v" + VersionText;

            string exeDir = Path.GetDirectoryName (Assembly.GetExecutingAssembly().Location);
            Directory.SetCurrentDirectory (exeDir);

            model = new FlacDiags.Model (null, Granularity.Verbose);
            viewFactory.Create (this, model.Bind);

            model.SetGuiIssues (model.Bind.Issues);
            ParseArgs();
            pathBox.Text = userPath;
            DataContext = model.Bind;
            pathBox.Focus();
        }


        string userPath;
        private int ParseArgs()
        {
            for (int ai = 0; ai < args.Length; ++ai)
            {
                string arg = args[ai];
                bool argOK = false;

                if (arg.StartsWith ("/sig:"))
                {
                    model.Bind.UserSig = arg.Substring (5);
                    argOK = true;
                }
                else if (arg == "/autoname")
                {
                    model.Bind.Autoname = NamingStrategy.ArtistTitle;
                    argOK = true;
                }
                else if (arg.StartsWith ("/autoname:"))
                {
                    argOK = Enum.TryParse<NamingStrategy> (arg.Substring (10), true, out NamingStrategy ns);
                    argOK = argOK && Enum.IsDefined (typeof (NamingStrategy), ns);
                    if (argOK)
                        model.Bind.Autoname = ns;
                }
                else if (arg.StartsWith ("/g:"))
                {
                    argOK = Enum.TryParse<Granularity> (arg.Substring (3), true, out Granularity gran);
                    argOK = argOK && Enum.IsDefined (typeof (Granularity), gran) && gran <= Granularity.Advisory;
                    if (argOK)
                        model.Bind.Scope = gran;
                }
                else if (arg == "/md5")
                {
                    model.Bind.IsParanoid = true;
                    argOK = true;
                }
                else if (arg == "/rg")
                {
                    model.Bind.ApplyRG = true;
                    argOK = true;
                }
                else if (arg == "/prove")
                {
                    model.Bind.WillProve = true;
                    argOK = true;
                }
                else if (arg == "/prove:web")
                {
                    model.Bind.WillProve = true;
                    model.Bind.IsWebCheckEnabled = true;
                    argOK = true;
                }
                else if (arg == "/ubertags")
                {
                    model.Bind.IsBestTags = true;
                    argOK = true;
                }
                else if (ai == args.Length-1)
                {
                    userPath = arg;
                    argOK = true;
                }

                if (! argOK)
                    model.IssueModel.Add ("Ignoring argument '" + arg + "'.", Severity.Warning);
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
                return result + "w";
            }
        }


        private NamingStrategy GetUserStrategy()
        {
            var result = NamingStrategy.Manual;
            foreach (RadioButton btn in stratWrap.Children)
                if (btn.IsChecked == true)
                {
                    System.Diagnostics.Debug.Assert (btn.Tag.GetType() == typeof (int));

                    var userStrat = (NamingStrategy) btn.Tag;
                    if (Enum.IsDefined (typeof (NamingStrategy), userStrat))
                    { result = userStrat; break; }
                }
            return result;
        }


        private void Sign_Click (object sender, RoutedEventArgs e)
        {
            model.ResetTotals();
            model.ClearRip();

            string userFile = pathBox.Text.Trim (null);
            model.Bind.Autoname = GetUserStrategy();

            var jobArgs = new FlacJobArgs (userFile, model.Bind.UserSig.Trim(null));
            var bg = new BackgroundWorker();
            bg.DoWork += Job;
            bg.RunWorkerCompleted += JobCompleted;
            bg.RunWorkerAsync (jobArgs);
        }


        private void CommitBtn_Click (object sender, RoutedEventArgs e)
        {
            if (model.RipModel != null && model.RipModel.Bind.IsWip)
            {
                model.RipModel.Commit (commentBox.Text);
                model.Bind.OnMessageSend (model.RipModel.Bind.Trailer, model.RipModel.Bind.Status);
                model.SetCurrentDirectory (null);
                md5Ctrl.history.ScrollToEnd();
                var tab = infoTabs.SelectedItem as TabItem;
                if (tab == null || tab.Visibility != Visibility.Visible)
                    tab = mainTab;
                tab.Focus();
            }
        }


        private FormatBase GetFormatFromTab (TabItem tab)
        {
            if (model.RipModel != null && tab != null)
            {
                if (tab.Content is UserControl userControl)
                {
                    if (userControl.DataContext is FormatBase fmt)
                        return fmt;
                }
            }
            return null;
        }


        private void ShowIssues (TabItem tabItem)
        {
            if (model == null)
                return;

            FormatBase fmt = GetFormatFromTab (tabItem);
            if (fmt != null)
                model.SetGuiIssues (fmt.Issues);
            else
                model.SetGuiIssues (model.Bind.Issues);

            model.Bind.NotifyPropertyChanged ("GuiIssues");

            if (diagsList.Items.Count > 1)
                diagsList.ScrollIntoView (diagsList.Items[diagsList.Items.Count-1]);
        }


        private void infoTabs_SelectionChanged (object sender, SelectionChangedEventArgs args)
        {
            if (args.Source is TabControl tabControl)
                foreach (var added in args.AddedItems)
                {
                    var tab = added as TabItem;
                    if (model != null && model.RipModel != null && model.RipModel.LogModel != null)
                    {
                        var gt = model.RipModel.LogModel.Bind.GuiTracks;
                        if (gt != null)
                        {
                            if (tab == logTab)
                            {
                                var kt = gt.Items.Where (it => it.TestCRC != null).Count ();
                                logCtrl.logColumnsGv.Columns[3].Width = kt == 0 ? 0 : Double.NaN;
                            }
                            ShowIssues (tab);
                        }
                    }
                }
        }


        private WrapPanel stratWrap;
        private void strategyWrap_Loaded (object sender, RoutedEventArgs rea)
        {
            stratWrap = (WrapPanel) sender;
            string[] stratNames = Enum.GetNames (typeof (NamingStrategy));
            for (int ix = 0; ix < stratNames.Length; ++ix)
            {
                var btn = new RadioButton();
                btn.Tag = ix;
                btn.Content = stratNames[ix];
                btn.Margin = new Thickness (3, 0, 6, 0);
                btn.GroupName = "NamingStrategy";
                btn.IsChecked = model != null && ix == (int) model.Bind.Autoname;
                stratWrap.Children.Add (btn);
            }
        }


        private void Browse_Click (object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "EAC log files (*.log)|*.log";
            bool? result = dlg.ShowDialog();
            if (! String.IsNullOrWhiteSpace (dlg.FileName))
                pathBox.Text = dlg.FileName;
        }


        private void ConsoleClearBtn_Click (object sender, RoutedEventArgs e)
        {
            model.FormatModel.ResetTotals();
            consoleBox.Text = null;
        }


        private void ConsoleMinusBtn_Click (object sender, RoutedEventArgs e)
        {
            var newSize = consoleBox.FontSize - 1;
            if (newSize >= 4)
                consoleBox.FontSize = newSize;
        }


        private void ConsolePlusBtn_Click (object sender, RoutedEventArgs e)
        {
            var newSize = consoleBox.FontSize + 1;
            if (newSize <= 60)
                consoleBox.FontSize = newSize;
        }


        private void FileLbl_Drop (object sender, DragEventArgs e)
        {
            if (e.Data.GetData (DataFormats.FileDrop, true) is string[] drops)
                if (drops.Length > 0)
                    pathBox.Text = drops[0];
        }


        private void ProveBlock_MouseDown (object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var bx = new TextBox();
            bx.TextWrapping = TextWrapping.Wrap;
            bx.AcceptsReturn = true;
            bx.Text = @"1. Disc must be ripped with EAC version 1 or later.
2. EAC log self-hash must exist and pass on-line verification.
3. CUETools verification must be attempted and no track may fail.
4. Disc must have at least one of:
   AcurrateRip confidence 1,
   CUETools DB confidence 1,
   or successful test pass.
5. Tracks must be encoded with FLAC 1.2.1 or greater.
6. Disc must pass the standard battery of UberFLAC verifications.

Steps 3 and 4 are confirmed by analyzing the EAC log contents.
Steps 5 and 6 are confirmed by analyzing the log and FLAC files.";
            bx.IsReadOnly = true;
            var pp = new Popup();

            pp.Child = bx;
            pp.Placement = PlacementMode.MousePoint;
            pp.StaysOpen = false;
            pp.IsOpen = true;
        }


        private void GuideBlock_MouseDown (object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var bx = new TextBox();
            bx.TextWrapping = TextWrapping.Wrap;
            bx.AcceptsReturn = true;
            bx.Text = UberGuide;
            bx.IsReadOnly = true;
            var pp = new Popup();

            pp.Child = bx;
            pp.Placement = PlacementMode.MousePoint;
            pp.StaysOpen = false;
            pp.IsOpen = true;
        }
    }
}

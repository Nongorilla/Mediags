using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using NongFormat;
using NongIssue;
using NongMediaDiags;
using Nong.Mvvm;

namespace AppViewModel
{
    public class TabInfo
    {
        private List<FormatBase> parsings;
        public int TabPosition { get; private set; }
        public int Index { get; private set; }
        public int Count => parsings.Count;
        public FormatBase Current => parsings[Index];

        public TabInfo (int tabPosition)
        { this.TabPosition = tabPosition; this.Index = 0; this.parsings = new List<FormatBase>(); }

        public void Add (FormatBase fmt)
        { Index = parsings.Count; parsings.Add (fmt); }

        public void SetIndex (int index)
        {
            if (index >= 0 && index < parsings.Count)
                Index = index;
        }
    }

    // The ViewModel data of Model-View-ViewModel.
    public class DiagsPresenter : Diags, INotifyPropertyChanged
    {
        private SortedList<string,TabInfo> tabInfo = new SortedList<string,TabInfo>();

        public int CurrentTabNumber { get; set; }
        public M3uFormat M3u { get; private set; }
        public Mp3Format Mp3 { get; private set; }
        public OggFormat Ogg { get; private set; }
        public Sha1Format Sha1 { get; private set; }

        private TabInfo CurrentTabFormatInfo
         => tabInfo.Values.FirstOrDefault (v => v.TabPosition == CurrentTabNumber);

        private void AddTabInfo (string formatName, int tabPosition)
         => tabInfo.Add (formatName, new TabInfo (tabPosition));

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChangedEvent (string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler (this, new PropertyChangedEventArgs (propertyName));
        }

        public ICommand DoBrowse { get; private set; }
        public ICommand DoParse { get; private set; }
        public ICommand NavFirst { get; private set; }
        public ICommand NavNext { get; private set; }
        public ICommand DoConsoleClear { get; private set; }
        public ICommand DoConsoleZoomMinus { get; private set; }
        public ICommand DoConsoleZoomPlus { get; private set; }

        private DiagsPresenter (DiagsPresenter.Model model) : base (model)
        {
            this.Scope = Granularity.Verbose;
            this.HashFlags = Hashes.Intrinsic;
            this.ValidationFlags = Validations.Exists;

            this.DoBrowse = new RelayCommand (() => model.Data.Root = model.Ui.BrowseFile());
            this.DoParse = new RelayCommand (() => model.Parse());
            this.NavFirst = new RelayCommand (() => model.GetFirst());
            this.NavNext = new RelayCommand (() => model.GetNext());
            this.DoConsoleClear = new RelayCommand (() => model.Ui.SetText (""));
            this.DoConsoleZoomMinus = new RelayCommand (() => model.Ui.ConsoleZoom (-1));
            this.DoConsoleZoomPlus = new RelayCommand (() => model.Ui.ConsoleZoom (+1));
        }

        public Hashes HashToggle
        {
            get { return HashFlags; }
            set { HashFlags = value < 0 ? (HashFlags & (Hashes) value) : (HashFlags | (Hashes) value); }
        }

        public Validations ValidationToggle
        {
            get { return ValidationFlags; }
            set { ValidationFlags = value < 0 ? (ValidationFlags & value) : (ValidationFlags | (Validations) value); }
        }


        // The ViewModel API of Model-View-ViewModel.
        public new class Model : Diags.Model
        {
            public IDiagsUi Ui { get; private set; }
            public new DiagsPresenter Data => (DiagsPresenter) _data;

            public Model (IDiagsUi ui)
            {
                this.Ui = ui;
                this._data = new DiagsPresenter (this);

                int ix = 0;
                foreach (string tabHeader in Ui.GetHeadings())
                {
                    if (tabHeader.StartsWith ("."))
                        Data.AddTabInfo (tabHeader.Substring (1), ix);
                    ++ix;
                }

                base.Data.FileVisit += Ui.FileProgress;
                base.Data.MessageSend += Ui.ShowLine;
            }

            public void GetFirst()
            {
                TabInfo ti = Data.CurrentTabFormatInfo;
                if (ti != null && ti.Count > 0)
                {
                    ti.SetIndex (0);
                    RefreshTab (ti.Current);
                }
            }

            public void GetNext()
            {
                TabInfo ti = Data.CurrentTabFormatInfo;
                if (ti != null && ti.Count > 0)
                {
                    ti.SetIndex (ti.Index + 1);
                    RefreshTab (ti.Current);
                }
            }

            public void RefreshTab (FormatBase fmt)
            {
                if (fmt is M3uFormat m3u)
                    Data.M3u = m3u;
                else if (fmt is Mp3Format mp3)
                    Data.Mp3 = mp3;
                else if (fmt is OggFormat ogg)
                    Data.Ogg = ogg;
                else if (fmt is Sha1Format sha1)
                    Data.Sha1 = sha1;

                TabInfo ti = Data.tabInfo[fmt.ValidNames[0]];
                Data.CurrentTabNumber = ti.TabPosition;

                Data.RaisePropertyChangedEvent (null);
            }

            public void Parse()
            {
                string err = null;
                TabInfo firstTInfo = null;
                int firstParsingIx = 0;

                try
                {
                    foreach (FormatBase.ModelBase parsing in CheckRoot())
                        if (parsing != null)
                            if (Data.tabInfo.TryGetValue (parsing.Data.NamedFormat, out TabInfo tInfo))
                            {
                                if (firstTInfo == null)
                                { firstTInfo = tInfo; firstParsingIx = tInfo.TabPosition; }
                                tInfo.Add (parsing.Data);
                            }
                }
                catch (IOException ex)
                { err = ex.Message; }
                catch (ArgumentException ex)
                { err = ex.Message; }

                if (err != null)
                    Ui.ShowLine (err, Severity.Error, Likeliness.None);

                if (firstTInfo != null)
                {
                    firstTInfo.SetIndex (firstParsingIx);
                    RefreshTab (firstTInfo.Current);
                }
            }
        }
    }
}

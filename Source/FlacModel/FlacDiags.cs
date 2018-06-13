#if MODEL_FLAC
using System;
using System.ComponentModel;
using System.Linq;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public partial class FlacDiags : Diags, INotifyPropertyChanged, IDataErrorInfo
    {
        public FlacRip Rip { get; private set; }
        public NamingStrategy Autoname { get; set; }
        public bool ApplyRG { get; set; }
        public string UserSig { get; set; }
        public int StopAfter { get; set; } = 3;

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

        public Issue.Vector Issues { get; protected set; }
        public Issue.Vector GuiIssues { get; private set; }

        public readonly FileFormat FlacFormat, LogFormat, M3uFormat, Md5Format;

        public string NoncompliantName { get { return "--NOT COMPLIANT WITH STANDARD--.txt"; } }
        public string FailPrefix { get { return "!!_ERRORS_!!"; } }

        string IDataErrorInfo.Error
        { get { return "Nevermore"; } }

        string IDataErrorInfo.this[string propName]
        {
            get
            {
                if (propName == "UserSig")
                {
                    string cleanSig = Map1252.ToClean1252FileName (UserSig);
                    if (cleanSig != null && (cleanSig != UserSig || cleanSig.Any (Char.IsWhiteSpace)))
                        return "cannot have spaces or characters not allowed in file names";
                }

                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged (string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged (this, new PropertyChangedEventArgs (propName));
        }

        private FlacDiags (string root, Granularity scope, FileFormat.Vector formats, Issue.Vector issues)
        {
            this.Root = root;
            this.UserSig = String.Empty;
            this.Scope = scope;
            this.ErrEscalator = IssueTags.Substandard;
            this.FileFormats = formats;
            this.Issues = issues;

            FlacFormat = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="flac");
            LogFormat = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="log");
            Md5Format = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="md5");
            M3uFormat = FileFormats.Items.FirstOrDefault (it => it.PrimaryName=="m3u");
        }
    }
}
#endif

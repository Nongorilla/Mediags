#if MODEL_FLAC
using System;
using System.ComponentModel;
using System.IO;
using NongIssue;
using NongFormat;
using System.Collections.Generic;

namespace NongMediaDiags
{
    public partial class FlacRip : INotifyPropertyChanged
    {
        private static readonly string[] filterOnConversion = new string[] { ".gif", ".jpeg", ".jpg", ".png" };

        private readonly FlacDiags BindOwner;

        private FileInfo[] logInfos, flacInfos;
        private IList<FlacFormat.Model> flacModels;

        public LogEacFormat Log { get; private set; }
        public M3uFormat M3u { get; private set; }
        public Md5Format Md5 { get; private set; }

        public int TrackEditCount { get; private set; }
        public int TrackRenameCount { get; private set; }
        public int AlbumRenameCount { get; private set; }
        private bool isWip;
        private string path;

        public DirectoryInfo Dir { get; private set; }
        public string WorkName { get; private set; }
        public string Signature { get; private set; }
        public NamingStrategy Autoname { get; private set; }

        public Severity Status { get; private set; }
        public Severity MaxFlacSeverity { get; private set; }
        public string Message { get; private set; }
        public string LogName { get; private set; }
        public string LogRipper { get; private set; }
        public string NewComment {get; private set; }
        public bool IsCheck { get; private set; }
        public bool IsProven { get; private set; }

        public string DirPath
        {
            get { return path; }
            private set { path = value; this.Dir = value==null? null : new DirectoryInfo (value); }
        }

        public bool IsWip
        {
            get { return isWip; }
            set { isWip = value; NotifyPropertyChanged (null); }
        }

        public bool IsCommentable => Signature != null && NewComment != String.Empty;
        public bool IsCommitable => isWip && NewComment == null;
        public bool HasChange => TrackEditCount > 0 || TrackRenameCount > 0 || AlbumRenameCount > 0;

        private FlacRip (FlacDiags owner, string path, NamingStrategy autoname, string signature)
        {
            this.BindOwner = owner;
            this.isWip = false;
            this.NewComment = String.Empty;
            this.DirPath = path;
            this.Signature = signature;
            this.Autoname = signature==null? NamingStrategy.Manual : autoname;
            this.isWip = false;
            this.NewComment = String.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged (string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged (this, new PropertyChangedEventArgs (propName));
        }

        public string Trailer
        {
            get
            {
                if (Log == null)
                    return "No EAC-to-FLAC rip found.";
                else if (Status >= Severity.Error)
                    return "EAC rip is not uber.";
                else if (Signature != null)
                    if (HasChange)
                        return "EAC rip changes logged and rip is uber!";
                    else if (LogRipper != null)
                        return "EAC rip is uber!";
                    else
                        return "EAC log is signed and uber!";
                else if (Md5 == null)
                    return "EAC rip is OK!";
                else
                    return "EAC rip is still uber!";
            }
        }
    }
}
#endif

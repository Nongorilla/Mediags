#if MODEL_LAME
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public class LameRip
    {
        public class Model
        {
            private readonly LameDiags.Model Owner;
            public LameRip Bind { get; private set; }
            public LogEacFormat.Model LogModel;
            public Sha1xFormat.Model Sha1xModel = null;

            public Model (LameDiags.Model model, string path, string signature, string logTag)
            {
                if (model == null)
                    throw new NullReferenceException ("LameRip constructor missing model.");

                this.Owner = model;
                this.Bind = new LameRip (path, signature, logTag);
            }


            public void SetStatus (Severity status)
            {
                if (Bind.Status < status)
                    Bind.Status = status;
            }


            public void Validate()
            {
                ValidateDirectory();
                if (Bind.Status >= Severity.Error)
                    return;
                else if (Bind.Status == Severity.NoIssue && Bind.Log == null)
                    return;

                ValidateLog();
                ++Owner.Bind.LogFormat.TrueTotal;
                ++Owner.Bind.TotalFiles;
                Owner.SetCurrentFile (Bind.LogName);

                if (Bind.Status < Bind.Log.Issues.MaxSeverity)
                    Bind.Status = Bind.Log.Issues.MaxSeverity;
                if (Bind.Status >= Severity.Error)
                {
                    Owner.ReportFormat (Bind.Log, Bind.Signature != null);
                    return;
                }

                if (Bind.Ripper == null)
                    if (Bind.Signature == null)
                    {
                        Owner.ReportFormat (Bind.Log, false);
                        Bind.IsWip = false;
                        Bind.Status = Bind.Log.Issues.MaxSeverity;
                        return;
                    }
                    else if (Bind.DotLogTag.Length != 0 && ! Bind.Log.Name.EndsWith (Bind.DotLogTag + ".log"))
                    {
                        string err = LogModel.Rename (Bind.WorkName + Bind.DotLogTag + ".log");
                        if (err != null)
                            LogModel.IssueModel.Add ("Log rename failed: " + err, Severity.Error);
                    }

                ValidateAlbum();
                if (Bind.Status >= Severity.Error)
                    return;

                ValidateDigest();
                ++Owner.Bind.Sha1xFormat.TrueTotal;
                ++Owner.Bind.TotalFiles;

                var playlistStatus = ValidatePlaylists();
                if (Bind.Status < playlistStatus)
                    Bind.Status = playlistStatus;

                if (Bind.Sha1x != null)
                {
                    string firstSig = Bind.Ripper ?? Bind.Signature;

                    if (Bind.Signature == null)
                        Sha1xModel.IssueModel.Escalate (IssueTags.None, IssueTags.AlbumChange|IssueTags.NameChange);

                    if (Sha1xModel.IssueModel.Bind.MaxSeverity > Bind.Status)
                        Bind.Status = Bind.Sha1x.Issues.MaxSeverity;

                    Owner.SetCurrentFile (Bind.Sha1x.Name);
                    Owner.ReportFormat (Bind.Sha1x, Bind.Signature != null);
                }
                else if (Bind.Status < Severity.Error)
                    Bind.Status = Severity.Error;

                if (Bind.Status >= Severity.Error)
                    CloseFiles();
                else
                    Commit();

                return;
            }


            private void ValidateDirectory()
            {
                Owner.SetCurrentDirectory (Bind.DirPath);
                try
                {
                    Bind.logInfos = Bind.Dir.GetFiles ("*.log").Where (lf => lf.Name.EndsWith (".log")).ToArray();
                    Bind.mp3Infos = Bind.Dir.GetFiles ("*.mp3").Where (lf => lf.Name.EndsWith (".mp3")).ToArray();
                    Bind.digInfos = Bind.Dir.GetFiles ("*.sha1x");
                    Bind.m3uInfos = Bind.Dir.GetFiles ("*.m3u").Where (lf => lf.Name.EndsWith (".m3u")).ToArray();
                    Bind.m3u8Infos = Bind.Dir.GetFiles ("*.m3u8");
                }
                catch (IOException ex)
                {
                    Owner.ReportLine (ex.Message.Trim (null), Severity.Fatal);
                    Bind.Status = Severity.Fatal;
                    return;
                }

                if (Bind.logInfos.Length == 0)
                {
                    if (Bind.mp3Infos.Length > 0)
                    {
                        ++Owner.Bind.Mp3Format.TrueTotal;
                        ++Owner.Bind.TotalFiles;
                        Owner.ReportLine ("Found .mp3 file(s) without a .log file in same directory.", Severity.Error, Bind.Signature != null);
                        Bind.Status = Severity.Error;
                    }

                    return;
                }

                if (Bind.logInfos.Length > 1)
                {
                    ++Owner.Bind.LogFormat.TrueTotal;
                    ++Owner.Bind.TotalFiles;
                    Owner.ReportLine ("Directory has more than 1 .log file.", Severity.Error, Bind.Signature != null);
                    Bind.Status = Severity.Error;
                    return;
                }

                if (Bind.mp3Infos.Length == 0)
                {
                    ++Owner.Bind.LogFormat.TrueTotal;
                    ++Owner.Bind.TotalFiles;
                    Owner.ReportLine ("Directory has .log file yet has no .mp3 files.", Severity.Error, Bind.Signature != null);
                    Bind.Status = Severity.Error;
                    return;
                }

                Array.Sort (Bind.mp3Infos, (f1, f2) => f1.Name.CompareTo (f2.Name));
                Owner.Bind.ExpectedFiles = 1 + Bind.mp3Infos.Length;
                Bind.LogName = Bind.logInfos[0].Name;

                if (Bind.digInfos.Length == 0)
                {
                    Bind.WorkName = Path.GetFileNameWithoutExtension (Bind.LogName);
                    if (Bind.WorkName.EndsWith (Bind.DotLogTag))
                        Bind.WorkName = Bind.WorkName.Substring (0, Bind.WorkName.Length - Bind.DotLogTag.Length);
                    Bind.DigName = Bind.WorkName + Bind.DotLogTag + ".LAME." + Bind.Signature + ".sha1x";
                    Bind.DigPath = Bind.DirPath + Bind.DigName;
                }
                else
                {
                    ++Owner.Bind.ExpectedFiles;
                    var digRE = new Regex (@"(.+)\.LAME\.(.+)\.sha1x");
                    Bind.DigName = Bind.digInfos[0].Name;
                    Bind.DigPath = Bind.digInfos[0].FullName;
                    Owner.SetCurrentFile (Bind.DigName);

                    MatchCollection digMat = digRE.Matches (Bind.DigName);
                    if (digMat.Count != 1)
                        Bind.WorkName = Path.GetFileNameWithoutExtension (Bind.LogName);
                    else
                    {
                        Match m1 = digMat[0];
                        if (m1.Groups.Count != 3)
                        {
                            ++Owner.Bind.LogFormat.TrueTotal;
                            ++Owner.Bind.TotalFiles;
                            Owner.ReportLine ("Too confused by digest name, bailing out.", Severity.Error, Bind.Signature != null);
                            Bind.Status = Severity.Error;
                            return;
                        }
                        else
                        {
                            Bind.WorkName = m1.Groups[1].ToString();
                            Bind.Ripper = m1.Groups[2].ToString();
                        }
                    }
                }

                using (var logfs = new FileStream (Bind.logInfos[0].FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var hdr = new byte[0x26];
                    int got = logfs.Read (hdr, 0, hdr.Length);
                    LogModel = LogEacFormat.CreateModel (logfs, hdr, Bind.logInfos[0].FullName);
                }

                if (LogModel == null)
                {
                    ++Owner.Bind.LogFormat.TrueTotal;
                    ++Owner.Bind.TotalFiles;
                    Owner.ReportLine ("Invalid EAC log file or unknown layout.", Severity.Error, Bind.Signature != null);
                    Bind.Status = Severity.Error;
                    return;
                }

                LogModel.ClearFile();
                Bind.Log = LogModel.Bind;

                if (Bind.Ripper == null && (Owner.Bind.HashFlags & Hashes.WebCheck) != 0)
                    LogModel.CalcHashWebCheck();

                if (Bind.mp3Infos.Length != Bind.Log.Tracks.Items.Count)
                {
                    var sb = new StringBuilder ("Directory has ");
                    sb.Append (Bind.mp3Infos.Length);
                    sb.Append (" MP3");
                    if (Bind.mp3Infos.Length != 1)
                        sb.Append ("s");
                    sb.Append (", EAC log has ");
                    sb.Append (Bind.Log.Tracks.Items.Count);
                    sb.Append (" track");
                    sb.Append (Bind.Log.Tracks.Items.Count == 1? "." : "s.");
                    LogModel.TkIssue = LogModel.IssueModel.Add (sb.ToString(), Severity.Error, IssueTags.Failure);
                }

                IssueTags errEscalator = Owner.Bind.ErrEscalator;
                if (Owner.Bind.WillProve && Bind.Ripper == null)
                    errEscalator |= IssueTags.MissingHash;
                LogModel.IssueModel.Escalate (Owner.Bind.WarnEscalator, errEscalator);
            }


            private void ValidateLog()
            {
                Hashes mp3Hash = Bind.Signature==null && Bind.Ripper==null? Hashes.None : Hashes.MediaSHA1;
                ValidateMp3s (mp3Hash);

                if (Bind.Status < Bind.MaxTrackSeverity)
                    Bind.Status = Bind.MaxTrackSeverity;
                if (Bind.Status >= Severity.Error)
                    return;

                Hashes logHash = Bind.Signature==null && Bind.Ripper==null? Hashes.None : Hashes.FileSHA1;
                LogModel.CalcHashes (logHash, Validations.None);

                if (! Bind.Log.Issues.HasError)
                {
                    string msg = "CRC-16 checks of " + Bind.mp3Infos.Length + (Bind.mp3Infos.Length == 1? " MP3" : " MP3s")  + " successful.";
                    LogModel.TkIssue = LogModel.IssueModel.Add (msg, Severity.Advisory, IssueTags.Success);
                }
            }


            private void ValidateMp3s (Hashes fileHash)
            {
                Bind.mp3Models = new List<Mp3Format.Model>();
                int id3v1Count = 0, id3v23Count = 0, id3v24Count = 0;

                foreach (FileInfo mp3Info in Bind.mp3Infos)
                {
                    Owner.SetCurrentFile (mp3Info.Name, Granularity.Verbose);
                    using (var ffs = new FileStream (mp3Info.FullName, FileMode.Open, FileAccess.Read))
                    {
                        var buf = new byte[0x2C];
                        int got = ffs.Read (buf, 0, buf.Length);
                        Mp3Format.Model mp3Model = Mp3Format.CreateModel (ffs, buf, mp3Info.FullName);
                        if (mp3Model == null)
                        {
                            Bind.MaxTrackSeverity = Severity.Error;
                            Owner.ReportLine ("Doesn't seem to be a MP3.", Severity.Error, Bind.Signature != null);
                        }
                        else
                        {
                            Mp3Format mp3 = mp3Model.Bind;
                            mp3Model.CalcHashes (Hashes.Intrinsic|Owner.Bind.HashFlags|fileHash, Owner.Bind.ValidationFlags);
                            if (mp3.IsBadHeader)
                                ++Owner.Bind.Mp3Format.TotalHeaderErrors;
                            if (mp3.IsBadData)
                                ++Owner.Bind.Mp3Format.TotalDataErrors;

                            if (mp3.HasId3v1)
                                ++id3v1Count;
                            if (mp3.HasId3v2)
                                if (mp3.Id3v2Major == 3)
                                    ++id3v23Count;
                                else if (mp3.Id3v2Major == 4)
                                    ++id3v24Count;

                            IssueTags tags = Owner.Bind.IsFussy? IssueTags.Fussy : IssueTags.None;
                            mp3Model.IssueModel.Escalate (IssueTags.HasApe, tags|IssueTags.Substandard|IssueTags.Overstandard);

                            if (Bind.MaxTrackSeverity < mp3.Issues.MaxSeverity)
                                Bind.MaxTrackSeverity = mp3.Issues.MaxSeverity;

                            Bind.mp3Models.Add (mp3Model);
                            Owner.ReportFormat (mp3, Bind.Signature != null);
                        }

                        ++Owner.Bind.Mp3Format.TrueTotal;
                        ++Owner.Bind.TotalFiles;

                        if (mp3Model != null)
                            mp3Model.ClearFile();
                        if (Bind.MaxTrackSeverity >= Severity.Fatal)
                            return;
                    }
                }

                if (id3v1Count > 0 && id3v1Count != Bind.mp3Infos.Length)
                    LogModel.IssueModel.Add ("Tracks have incomplete ID3v1 tagging.");

                if (id3v23Count > 0 && id3v24Count > 0)
                    LogModel.IssueModel.Add ("Tracks inconsistently tagged both ID3v2.3 and ID3v2.4.");
            }


            private void ValidateAlbum()
            {
                LogModel.IssueModel.Escalate (Owner.Bind.WarnEscalator, IssueTags.None);

                Owner.ReportFormat (Bind.Log, Bind.Signature != null);
                if (Bind.Status < Bind.Log.Issues.MaxSeverity)
                    Bind.Status = Bind.Log.Issues.MaxSeverity;
            }


            private Severity ValidatePlaylists()
            {
                Severity maxSeverity = Severity.NoIssue;

                foreach (var info in Bind.m3uInfos)
                    using (var stream = new FileStream (info.FullName, FileMode.Open, FileAccess.Read))
                    {
                        var hdr = new byte[7];
                        stream.Read (hdr, 0, hdr.Length);
                        var m3uModel = M3uFormat.CreateModel (stream, hdr, info.FullName);
                        m3uModel.SetAllowRooted (false);
                        m3uModel.CalcHashes (Hashes.None, Validations.Exists);

                        Owner.SetCurrentFile (info.Name);
                        ++Owner.Bind.M3uFormat.TrueTotal;
                        ++Owner.Bind.TotalFiles;
                        Owner.ReportFormat (m3uModel.Bind, Bind.Signature != null);
                        if (maxSeverity < m3uModel.IssueModel.Bind.MaxSeverity)
                            maxSeverity = m3uModel.IssueModel.Bind.MaxSeverity;
                    }

                foreach (var info in Bind.m3u8Infos)
                    using (var stream = new FileStream (info.FullName, FileMode.Open, FileAccess.Read))
                    {
                        var hdr = new byte[7];
                        stream.Read (hdr, 0, hdr.Length);
                        var m3u8Model = M3u8Format.CreateModel (stream, hdr, info.FullName);
                        m3u8Model.SetAllowRooted (false);
                        m3u8Model.CalcHashes (Hashes.None, Validations.Exists);

                        Owner.SetCurrentFile (info.Name);
                        ++Owner.Bind.M3u8Format.TrueTotal;
                        ++Owner.Bind.TotalFiles;
                        Owner.ReportFormat (m3u8Model.Bind, Bind.Signature != null);
                        if (maxSeverity < m3u8Model.IssueModel.Bind.MaxSeverity)
                            maxSeverity = m3u8Model.IssueModel.Bind.MaxSeverity;
                    }

                return maxSeverity;
            }


            private void ValidateDigest()
            {
                var firstSig = Bind.Ripper ?? Bind.Signature;
                var newDigName = Bind.WorkName + Bind.DotLogTag + ".LAME." + firstSig + ".sha1x";
                var newDigPath = Owner.Bind.CurrentDirectory + Path.DirectorySeparatorChar + newDigName;

                if (Bind.Ripper == null)
                {
                    if (Bind.Signature == null)
                        return;

                    try
                    {
                        using (var fs0 = new FileStream (newDigPath, FileMode.CreateNew))
                        {
                            Sha1xModel = new Sha1xFormat.Model (fs0, newDigPath, Bind.Log, Bind.mp3Models, Bind.Signature);
                            Bind.Sha1x = Sha1xModel.Bind;

                            if (Owner.Bind.WillProve && LogModel.Bind.ShIssue != null && LogModel.Bind.ShIssue.Success)
                                Sha1xModel.HistoryModel.Add ("verified", Bind.Signature);

                            Sha1xModel.WriteFile (Owner.Bind.Product + " v" + Owner.Bind.ProductVersion, Encoding.UTF8);
                            Sha1xModel.ClearFile();
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Owner.ReportLine (ex.Message, Severity.Error, Bind.Signature != null);
                        return;
                    }

                    ++Owner.Bind.Sha1xFormat.TotalCreated;
                    Sha1xModel.IssueModel.Add ("Digest created.", Severity.Advisory);

                    return;
                }

                try
                {
                    var fs0 = new FileStream (Bind.DigPath, FileMode.Open, Bind.Signature == null? FileAccess.Read : FileAccess.ReadWrite, FileShare.Read|FileShare.Delete);
                    var hdr = new byte[0x2C];
                    int got = fs0.Read (hdr, 0, hdr.Length);
                    Sha1xModel = Sha1xFormat.CreateModel (fs0, hdr, Bind.DigPath);
                    Bind.Sha1x = Sha1xModel.Bind;
                    Sha1xModel.CalcHashes (Hashes.Intrinsic, Validations.None);
                    if (Bind.Sha1x.Issues.MaxSeverity >= Severity.Error)
                        return;

                    if (Sha1xModel.HistoryModel != null && Sha1xModel.HistoryModel.Bind.Prover != null)
                        Sha1xModel.IssueModel.Add ("EAC log self-hash previously verified.", Severity.Trivia);
                    else if (Owner.Bind.WillProve && LogModel.Bind.ShIssue == null)
                    {
                        LogModel.CalcHashWebCheck();
                        if (LogModel.Bind.ShIssue.Failure)
                            Sha1xModel.IssueModel.Add ("EAC log self-hash verify failed!");
                        else
                            Sha1xModel.IssueModel.Add ("EAC log self-hash verify successful.", Severity.Advisory);
                    }
                    else
                    {
                        Severity sev = Severity.Noise;
                        if (Owner.Bind.WillProve && (LogModel.Bind.ShIssue == null || ! LogModel.Bind.ShIssue.Success))
                            sev = Severity.Error;
                        Sha1xModel.IssueModel.Add ("EAC log self-hash not previously verified.", sev);
                    }

                    var logHashLine = Bind.Sha1x.HashedFiles.Items[0];

                    Sha1xModel.HashedModel.SetActualHash (0, LogModel.Bind.FileSHA1);
                    if (logHashLine.IsMatch == false)
                        Sha1xModel.IssueModel.Add ("EAC log has been modified. Rip is not valid.");

                    if (logHashLine.FileName != LogModel.Bind.Name)
                        Sha1xModel.IssueModel.Add ("EAC log has been renamed.", Severity.Advisory, IssueTags.AlbumChange);

                    for (int ix = 0; ix < Bind.mp3Models.Count; ++ix)
                    {
                        var hashLine = Bind.Sha1x.HashedFiles.Items[ix+1];
                        Sha1xModel.HashedModel.SetActualHash (ix+1, Bind.mp3Models[ix].Bind.MediaSHA1);

                        var actualName = Bind.mp3Models[ix].Bind.Name;
                        if (hashLine.IsMatch == false)
                            Sha1xModel.IssueModel.Add ("Audio SHA-1 mismatch on '" + actualName + "'.");
                        if (hashLine.FileName != actualName)
                            Sha1xModel.IssueModel.Add ("Track renamed to '" + actualName + "'.", Severity.Advisory, IssueTags.NameChange);
                    }
                }
                catch (FileNotFoundException)
                {
                    Owner.SetCurrentFile (Bind.DigName);
                    ++Owner.Bind.Sha1xFormat.TotalMissing;
                    Owner.ReportLine ("File missing.", Severity.Error, Bind.Signature != null);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Owner.SetCurrentFile (Bind.DigName);
                    Owner.ReportLine ("Open failed: " + ex.Message, Severity.Error, Bind.Signature != null);
                }

                Bind.AlbumRenameCount = Bind.Sha1x.Issues.Items.Count (x => (x.Tag & IssueTags.AlbumChange) != 0);
                Bind.TrackRenameCount = Bind.Sha1x.Issues.Items.Count (x => (x.Tag & IssueTags.NameChange) != 0);

                if (! Sha1xModel.IssueModel.Bind.HasError)
                {
                    var msg = "SHA-1 checks of log, " + Bind.mp3Infos.Length + " MP3 audio";
                    if (Bind.mp3Infos.Length != 1) msg += 's';
                    Sha1xModel.IssueModel.Add (msg + " successful.", Severity.Advisory);

                    if (Bind.Signature != null)
                    {
                        if (Owner.Bind.WillProve && Sha1xModel.Bind.History.Prover == null
                                && LogModel.Bind.ShIssue != null && LogModel.Bind.ShIssue.Success)
                        {
                            Sha1xModel.HistoryModel.Add ("verified", Bind.Signature);
                            Sha1xModel.IssueModel.Add ("Change log updated with 'verified'.", Severity.Advisory);
                        }

                        if (Bind.AlbumRenameCount > 0 || Bind.TrackRenameCount > 0)
                        {
                            Sha1xModel.HistoryModel.Add ("renamed", Bind.Signature);
                            Sha1xModel.IssueModel.Add ("Change log updated with 'renamed'.", Severity.Advisory);
                        }

                        if (! Sha1xModel.HistoryModel.Bind.IsDirty && Sha1xModel.HistoryModel.Bind.LastSig != Bind.Signature)
                        {
                            Sha1xModel.HistoryModel.Add ("checked", Bind.Signature);
                            Sha1xModel.IssueModel.Add ("Change log updated with 'checked'.", Severity.Advisory);
                        }
                    }
                }
            }


            public void Commit()
            {
                if (! Bind.IsWip)
                    return;
                Bind.IsWip = false;

                if (Bind.Signature == null)
                    return;

                if (Bind.AlbumRenameCount > 0)
                        Sha1xModel.HashedModel.SetFileName (0, Bind.Log.Name);

                if (Bind.TrackRenameCount > 0)
                    for (var ix = 0; ix < Bind.mp3Models.Count; ++ix)
                        Sha1xModel.HashedModel.SetFileName (ix+1, Bind.mp3Models[ix].Bind.Name);

                if (Sha1xModel.HistoryModel != null && Sha1xModel.HistoryModel.Bind.IsDirty)
                    Sha1xModel.WriteFile (Owner.Bind.Product + " v" + Owner.Bind.ProductVersion, Encoding.UTF8);
                CloseFiles();

                var errInfo = new FileInfo (Bind.DirPath + Path.DirectorySeparatorChar + Owner.Bind.NoncompliantName);
                if (File.Exists (errInfo.FullName))
                    try
                    { File.Delete (errInfo.FullName); }
                    catch (Exception)
                    { /* and why not */ }

                if (Bind.Dir.Name.StartsWith (Owner.Bind.FailPrefix))
                {
                    try
                    {
                        var dirName = Bind.Dir.Parent.FullName;
                        if (dirName.Length > 0 && dirName[dirName.Length-1] != Path.DirectorySeparatorChar)
                            dirName += Path.DirectorySeparatorChar;
                        dirName += Bind.Dir.Name.Substring (Owner.Bind.FailPrefix.Length);
                        Bind.Dir.MoveTo (dirName);
                    }
                    catch (Exception)
                    { /* ignore all */ }
                }
           }


            public void CloseFiles()
            {
                Bind.IsWip = false;
                if (LogModel != null) LogModel.CloseFile();
                if (Sha1xModel != null) Sha1xModel.CloseFile();
            }
        }


        private IList<Mp3Format.Model> mp3Models;
        private FileInfo[] logInfos, mp3Infos, digInfos, m3uInfos, m3u8Infos;
        public LogEacFormat Log { get; private set; }
        public Sha1xFormat Sha1x { get; private set; }

        public int AlbumRenameCount { get; private set; }
        public int TrackRenameCount { get; private set; }

        public DirectoryInfo Dir { get; private set; }
        public string WorkName { get; private set; }
        public string Signature { get; private set; }
        public string DotLogTag { get; private set; }

        public Severity Status { get; private set; }
        public Severity MaxTrackSeverity { get; private set; }
        public string Message { get; private set; }
        public string LogName { get; private set; }
        public string DigName { get; private set; }
        public string DigPath { get; private set; }
        public string Ripper { get; private set; }
        public bool IsWip { get; set; }

        private string path;
        public string DirPath
        {
            get { return path; }
            private set { path = value; this.Dir = value==null? null : new DirectoryInfo (value); }
        }

        private LameRip (string path, string signature, string logTag)
        {
            this.DirPath = path;
            this.Signature = signature;
            this.DotLogTag = logTag == null ? String.Empty : "." + logTag;
        }


        public string Trailer
        {
            get
            {
                if (Log == null)
                    return "No EAC-to-MP3 rip found.";
                else if (Status >= Severity.Error)
                    return "EAC rip is not uber.";
                else if (Signature != null)
                    if (Ripper != null)
                        return "EAC rip is uber!";
                    else
                        return "EAC rip is signed and uber!";
                else if (Sha1x == null)
                    return "EAC rip is OK!";
                else
                    return "EAC rip is still uber!";
            }
        }
    }
}
#endif

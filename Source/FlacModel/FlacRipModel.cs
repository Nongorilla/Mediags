#if MODEL_FLAC
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public partial class FlacRip
    {
        public class Model : IDisposable
        {
            private readonly FlacDiags.Model Owner;
            public FlacRip Bind { get; private set; }
            public LogEacFormat.Model LogModel;
            public M3uFormat.Model M3uModel = null;
            public Md5Format.Model Md5Model = null;

            public Model (FlacDiags.Model model, string path, NamingStrategy autoname, string signature)
            {
                if (model == null)
                    throw new NullReferenceException ("FlacRip constructor missing model.");

                this.Owner = model;
                this.Bind = new FlacRip (model.Data, path, autoname, signature);
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
                Owner.SetCurrentFile (Bind.LogName);

                if (Bind.Status < Bind.Log.Issues.MaxSeverity)
                    Bind.Status = Bind.Log.Issues.MaxSeverity;
                if (Bind.Status >= Severity.Error)
                {
                    Owner.ReportFormat (Bind.Log, Bind.Signature != null);
                    return;
                }

                if (Bind.Signature != null && Bind.LogRipper == null && Owner.Data.ApplyRG)
                    LogModel.IssueModel.Add ("ReplayGain added.", Severity.Advisory);

                LogModel.SetWorkName (Bind.WorkName);

                if (Bind.LogRipper == null && Bind.Signature == null)
                {
                    // Lite validation.  Verify Log/FLACs and optional playlist only.
                    var oldM3uInfo = new FileInfo (Bind.DirPath + Path.DirectorySeparatorChar + Bind.Log.WorkName + ".m3u");
                    Owner.ReportFormat (Bind.Log, false);
                    Owner.SetCurrentFile (oldM3uInfo.Name);
                    ValidateM3u (oldM3uInfo.FullName);
                    if (Bind.M3u != null)
                    {
                        Owner.ReportFormat (Bind.M3u, false);
                        if (! Bind.M3u.Issues.HasError)
                            ++Owner.Data.TotalSignable;
                    }
                    Bind.IsWip = false;
                    Bind.Status = Bind.Log.Issues.MaxSeverity;
                    Bind.IsProven = LogModel.Bind.ShIssue != null && LogModel.Bind.ShIssue.Success;
                    return;
                }

                ValidateAlbum();
                if (Bind.Status >= Severity.Error)
                    return;

                ValidateMd5();
                ++Owner.Data.Md5Format.TrueTotal;
                ++Owner.Data.TotalFiles;

                if (Bind.M3u != null)
                {
                    Owner.SetCurrentFile (Bind.M3u.Name);
                    Owner.ReportFormat (Bind.M3u, Bind.Signature != null);
                    if (Bind.Status < Bind.M3u.Issues.MaxSeverity)
                        Bind.Status = Bind.M3u.Issues.MaxSeverity;
                }
                else if (Bind.Status < Severity.Error)
                    Bind.Status = Severity.Error;

                if (Bind.Md5 != null)
                {
                    string md5WorkName;
                    string firstSig;

                    if (Md5Model.IssueModel.Data.MaxSeverity > Bind.Status)
                        Bind.Status = Bind.Md5.Issues.MaxSeverity;

                    if (Bind.LogRipper != null)
                    { md5WorkName = Bind.WorkName; firstSig = Bind.LogRipper; }
                    else
                    { md5WorkName = Bind.Log.WorkName; firstSig = Bind.Signature; }

                    Owner.SetCurrentFile (md5WorkName + ".FLAC." + firstSig + ".md5");
                    Owner.ReportFormat (Bind.Md5, Bind.Signature != null);
                }
                else if (Bind.Status < Severity.Error)
                    Bind.Status = Severity.Error;

                if (Bind.Status >= Severity.Error)
                    CloseFiles();
                else
                {
                    string comment = ValidateGetComment();
                    if (comment == null)
                        Bind.NewComment = null;
                    else
                        Commit (comment);
                }

                return;
            }


            private void ValidateDirectory()
            {
                Owner.SetCurrentDirectory (Bind.DirPath);
                try
                {
                    Bind.logInfos = Bind.Dir.GetFiles ("*.log").Where (lf => lf.Name.EndsWith (".log")).ToArray();
                    Bind.flacInfos = Bind.Dir.GetFiles ("*.flac");
                }
                catch (IOException ex)
                {
                    Owner.ReportLine (ex.Message.Trim (null), Severity.Fatal);
                    Bind.Status = Severity.Fatal;
                    return;
                }

                if (Bind.logInfos.Length == 0)
                {
                    if (Bind.flacInfos.Length > 0)
                    {
                        ++Owner.Data.FlacFormat.TrueTotal;
                        ++Owner.Data.TotalFiles;
                        ++Owner.Data.LogFormat.TotalMissing;
                        ++Owner.Data.TotalErrors;
                        Owner.ReportLine ("Found .flac file(s) without a .log file in same directory.", Severity.Error, Bind.Signature != null);
                        Bind.Status = Severity.Error;
                    }

                    return;
                }

                if (Bind.logInfos.Length > 1)
                {
                    Owner.Data.LogFormat.TrueTotal += Bind.logInfos.Length;
                    Owner.Data.TotalFiles += Bind.logInfos.Length;
                    Owner.Data.TotalErrors += Bind.logInfos.Length - 1;
                    Owner.ReportLine ("Directory has more than 1 .log file.", Severity.Error, Bind.Signature != null);
                    Bind.Status = Severity.Error;
                    return;
                }

                if (Bind.flacInfos.Length == 0)
                {
                    ++Owner.Data.LogFormat.TrueTotal;
                    ++Owner.Data.TotalFiles;
                    ++Owner.Data.TotalErrors;
                    ++Owner.Data.FlacFormat.TotalMissing;
                    Owner.ReportLine ("Directory has .log file yet has no .flac files.", Severity.Error, Bind.Signature != null);
                    Bind.Status = Severity.Error;
                    return;
                }

                Array.Sort (Bind.flacInfos, (f1, f2) => f1.Name.CompareTo (f2.Name));

                var logRE = new Regex (@"(.+)\.FLAC\.(.+)\.log");
                Bind.LogName = Bind.logInfos[0].Name;
                Owner.SetCurrentFile (Bind.LogName);

                MatchCollection logMat = logRE.Matches (Bind.LogName);
                if (logMat.Count != 1)
                    Bind.WorkName = Path.GetFileNameWithoutExtension (Bind.LogName);
                else
                {
                    Match m1 = logMat[0];
                    if (m1.Groups.Count != 3)
                    {
                        ++Owner.Data.LogFormat.TrueTotal;
                        ++Owner.Data.TotalFiles;
                        Owner.ReportLine ("Too confused by .log name, bailing out.", Severity.Error, Bind.Signature != null);
                        Bind.Status = Severity.Error;
                        return;
                    }
                    else
                    {
                        Bind.WorkName = m1.Groups[1].ToString();
                        Bind.LogRipper = m1.Groups[2].ToString();
                    }
                }

                Owner.Data.ExpectedFiles = 1 + Bind.flacInfos.Length;
                if (Bind.Signature != null || Bind.LogRipper != null)
                    Owner.Data.ExpectedFiles += 2;

                using (var logfs = new FileStream (Bind.logInfos[0].FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var hdr = new byte[0x26];
                    int got = logfs.Read (hdr, 0, hdr.Length);
                    LogModel = LogEacFormat.CreateModel (logfs, hdr, Bind.logInfos[0].FullName);
                }

                Owner.SetCurrentFile (Bind.LogName);  // just for rebinding
                if (LogModel == null)
                {
                    ++Owner.Data.LogFormat.TrueTotal;
                    ++Owner.Data.TotalFiles;
                    Owner.ReportLine ("Invalid log file or unknown layout.", Severity.Error, Bind.Signature != null);
                    Bind.Status = Severity.Error;
                    return;
                }

                LogModel.ClearFile();
                Bind.Log = LogModel.Bind;

                if (Bind.LogRipper == null && Owner.Data.IsWebCheckEnabled)
                    LogModel.CalcHashWebCheck();

                if (Bind.flacInfos.Length != Bind.Log.Tracks.Items.Count)
                {
                    var sb = new StringBuilder ("Directory has ");
                    sb.Append (Bind.flacInfos.Length);
                    sb.Append (" FLAC");
                    if (Bind.flacInfos.Length != 1)
                        sb.Append ("s");
                    sb.Append (", EAC log has ");
                    sb.Append (Bind.Log.Tracks.Items.Count);
                    sb.Append (" track");
                    sb.Append (Bind.Log.Tracks.Items.Count == 1? "." : "s.");
                    LogModel.TkIssue = LogModel.IssueModel.Add (sb.ToString(), Severity.Error, IssueTags.Failure);
                }

                IssueTags errEscalator = Owner.Data.ErrEscalator;
                if (Owner.Data.WillProve && Bind.LogRipper == null)
                    errEscalator |= IssueTags.MissingHash;
                LogModel.IssueModel.Escalate (Owner.Data.WarnEscalator, errEscalator);

                if (Bind.Log.Issues.HasError)
                {
                    ++Owner.Data.LogFormat.TrueTotal;
                    ++Owner.Data.TotalFiles;
                    CloseFiles();
                    Owner.ReportFormat (Bind.Log, Bind.Signature != null);
                    if (Bind.Status < Bind.Log.Issues.MaxSeverity)
                        Bind.Status = Bind.Log.Issues.MaxSeverity;
                    return;
                }

                if (Bind.Signature != null && Bind.LogRipper == null && Owner.Data.ApplyRG)
                {
                    try
                    { FlacFormat.Model.ApplyReplayGain (Bind.flacInfos); }
                    catch (Exception ex)
                    {
                        ++Owner.Data.LogFormat.TrueTotal;
                        ++Owner.Data.TotalFiles;
                        CloseFiles();
                        Owner.ReportLine ("ReplayGain add failed: " + ex.Message.Trim (null), Severity.Error);
                        Bind.Status = Severity.Error;
                        return;
                    }
                }

                return;
            }


            private void ValidateLog()
            {
                Hashes fileHash = Bind.Signature==null && Bind.LogRipper==null? Hashes.None : Hashes.FileMD5;

                ValidateFlacs (fileHash);

                if (Bind.Status < Bind.MaxFlacSeverity)
                    Bind.Status = Bind.MaxFlacSeverity;
                if (Bind.Status >= Severity.Error)
                    return;

                LogModel.CalcHashes (fileHash, Validations.None);

                LogModel.MatchFlacs (Bind.flacModels);
                if (Bind.MaxFlacSeverity < Severity.Error && Bind.Log.Issues.MaxSeverity < Severity.Error)
                    LogModel.ValidateFlacTags();

                if (! Bind.Log.Issues.HasError)
                {
                    var sb2 = new StringBuilder ("CRC-32, CRC-16");
                    if ((Owner.Data.HashFlags & Hashes.PcmMD5) != 0)
                        sb2.Append (", MD5");
                    sb2.Append (" & tag checks of " + Bind.flacModels.Count + " FLAC");
                        if (Bind.flacModels.Count != 1)
                            sb2.Append ("s");
                    sb2.Append (" successful.");
                    LogModel.TkIssue = LogModel.IssueModel.Add (sb2.ToString(), Severity.Advisory, IssueTags.Success);
                    LogModel.Bind.NotifyPropertyChanged (null);
                }

                ++Owner.Data.LogFormat.TrueTotal;
                ++Owner.Data.TotalFiles;
            }


            private void ValidateFlacs (Hashes fileHash)
            {
                Bind.flacModels = new List<FlacFormat.Model>();
                foreach (var flacInfo in Bind.flacInfos)
                {
                    Owner.SetCurrentFile (flacInfo.Name, Granularity.Verbose);
                    using (var ffs = new FileStream (flacInfo.FullName, FileMode.Open, FileAccess.Read))
                    {
                        var buf = new byte[0x28];
                        int got = ffs.Read (buf, 0, buf.Length);
                        FlacFormat.Model flacModel = FlacFormat.CreateModel (ffs, buf, flacInfo.FullName);
                        if (flacModel == null)
                        {
                            Bind.MaxFlacSeverity = Severity.Error;
                            var err = got>=3 && buf[0]=='I' && buf[1]=='D' && buf[2]=='3'? "Has unallowed ID3v2 tag."
                                                                                         : "Doesn't seem to be a FLAC.";
                            Owner.ReportLine (err, Severity.Error, Bind.Signature != null);
                        }
                        else
                        {
                            FlacFormat flac = flacModel.Bind;
                            flacModel.CalcHashes (Hashes.Intrinsic|Hashes.PcmCRC32|Owner.Data.HashFlags|fileHash, Owner.Data.ValidationFlags);
                            if (flac.IsBadHeader)
                                ++Owner.Data.FlacFormat.TotalHeaderErrors;
                            if (flac.IsBadData)
                                ++Owner.Data.FlacFormat.TotalDataErrors;

                            if (Bind.MaxFlacSeverity < flac.Issues.MaxSeverity)
                                Bind.MaxFlacSeverity = flac.Issues.MaxSeverity;

                            Bind.flacModels.Add (flacModel);
                            Owner.ReportFormat (flac, Bind.Signature != null);
                        }

                        ++Owner.Data.FlacFormat.TrueTotal;
                        ++Owner.Data.TotalFiles;

                        if (flacModel != null)
                            flacModel.ClearFile();
                        if (Bind.MaxFlacSeverity >= Severity.Error)
                            return;
                    }
                }
            }


            private void ValidateAlbum()
            {
                LogModel.SetWorkName (LogModel.Bind.GetCleanWorkName (Bind.Autoname));
                if (Bind.Autoname == NamingStrategy.Manual && Bind.Log.WorkName != Bind.WorkName)
                {
                    LogModel.IssueModel.Add ("Album file names are not Windows-1252 clean.");
                    Owner.ReportFormat (Bind.Log, Bind.Signature != null);
                    Bind.Status = Severity.Error;
                    return;
                }

                var newMd5Name = Bind.Log.WorkName + ".FLAC." + (Bind.LogRipper?? Bind.Signature) + ".md5";
                var newMd5Path = Bind.DirPath + Path.DirectorySeparatorChar + newMd5Name;

                if (Bind.LogRipper == null)
                {
                    if (File.Exists (newMd5Path))
                    {
                        ++Owner.Data.Md5Format.TrueTotal;
                        ++Owner.Data.TotalFiles;
                        Owner.SetCurrentFile (newMd5Name);
                        Owner.ReportLine ("Digest already exists.", Severity.Error, Bind.Signature != null);
                        Bind.Status = Severity.Error;
                        return;
                    }

                    if (! Bind.Log.Issues.HasError && Bind.Autoname != NamingStrategy.Manual)
                    {
                        var trackWidth = Math.Max (Bind.Log.Tracks.WidestTrackWidth, 2);
                        for (int logIx = 0; logIx < LogModel.TracksModel.Bind.Items.Count; ++logIx)
                        {
                            var flacModel = LogModel.TracksModel.GetMatch (logIx);
                            var newName = flacModel.Bind.GetCleanFileName (Owner.Data.Autoname, Bind.Log.CalcedAlbumArtist, trackWidth);
                            if (newName != flacModel.Bind.Name)
                            {
                                var err = flacModel.Rename (newName);
                                if (err != null)
                                {
                                    LogModel.IssueModel.Add ("Rename of track " + flacModel.Bind.GetTag ("TRACKNUMBER") + " failed: " + err);
                                    break;
                                }
                                else
                                    LogModel.IssueModel.Add ("FLAC autonamed to '" + newName + "'.", Severity.Advisory, IssueTags.NameChange);
                            }
                        }
                    }
                }
                LogModel.IssueModel.Escalate (Owner.Data.WarnEscalator, IssueTags.None);

                Owner.ReportFormat (Bind.Log, Bind.Signature != null);
                if (Bind.Status < Bind.Log.Issues.MaxSeverity)
                    Bind.Status = Bind.Log.Issues.MaxSeverity;
                return;
            }


            private void ValidateM3u (string m3uPath, bool nukePrevious = false)
            {
                bool m3uExists = File.Exists (m3uPath);
                if (m3uExists && ! nukePrevious)
                {
                    using (var fs0 = new FileStream (m3uPath, FileMode.Open, FileAccess.Read))
                    {
                        var hdr = new byte[7];
                        fs0.Read (hdr, 0, hdr.Length);
                        M3uModel = M3uFormat.CreateModel (fs0, hdr, m3uPath);
                        Bind.M3u = M3uModel.Bind;

                        if (! Bind.M3u.Issues.HasError)
                            if (Bind.M3u.Files.Items.Count != Bind.Log.Tracks.Items.Count)
                                M3uModel.IssueModel.Add ("Wrong item count in .m3u (expecting "
                                                  + Bind.Log.Tracks.Items.Count + ", got " + Bind.M3u.Files.Items.Count + ").");
                            else
                            {
                                int trackCount = Bind.M3u.Files.Items.Count;
                                for (var ix = 0; ix < trackCount; ++ix)
                                {
                                    bool isFound = Bind.M3u.Files.Items[ix].Name == Bind.Log.Tracks.Items[ix].Match.Name;
                                    M3uModel.FilesModel.SetIsFound (ix, isFound);
                                    if (! isFound)
                                        M3uModel.IssueModel.Add ("Nonexistent file '" + Bind.M3u.Files.Items[ix].Name + "'.");
                                }

                                if (Bind.M3u.Issues.Items.Count == 0)
                                {
                                    var sfx = trackCount == 1 ? String.Empty : "s";
                                    M3uModel.IssueModel.Add ("Existence check" + sfx + " of " + trackCount + " file" + sfx + " successful.", Severity.Trivia);
                                }
                            }

                        ++Owner.Data.M3uFormat.TrueTotal;
                        ++Owner.Data.TotalFiles;
                        if (Bind.M3u.Issues.HasError)
                            return;

                        M3uModel.CalcHashes (Hashes.FileMD5, Validations.None);
                    }
                }
                else if (Bind.Signature != null)
                {
                    using (var fs0 = new FileStream (m3uPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
                    {
                        M3uModel = new M3uFormat.Model (fs0, fs0.Name, Bind.Log);
                        Bind.M3u = M3uModel.Bind;
                        M3uModel.WriteFile();
                        M3uModel.CalcHashes (Hashes.FileMD5, Validations.None);
                    }

                    M3uModel.IssueModel.Add (m3uExists? "Playlist rewritten." : "Playlist created.", Severity.Advisory);
                    if (! m3uExists)
                        ++Owner.Data.M3uFormat.TotalCreated;
                    ++Owner.Data.M3uFormat.TrueTotal;
                    ++Owner.Data.TotalFiles;
                }

                return;
            }


            // Validate .md5 & .m3u.  Rewrite if updates.
            // Case when logRipper==null && Signature==null already handled.
            private void ValidateMd5()
            {
                var firstSig = Bind.LogRipper ?? Bind.Signature;
                var newLogName = Bind.Log.WorkName + ".FLAC." + firstSig + ".log";
                var newMd5Name = Bind.Log.WorkName + ".FLAC." + firstSig + ".md5";
                var newMd5Path = Owner.Data.CurrentDirectory + Path.DirectorySeparatorChar + newMd5Name;
                var oldM3uName = Bind.WorkName + ".m3u";
                var newM3uName = Bind.Log.WorkName + ".m3u";
                var oldM3uPath = Owner.Data.CurrentDirectory + Path.DirectorySeparatorChar + oldM3uName;
                var newM3uPath = Owner.Data.CurrentDirectory + Path.DirectorySeparatorChar + Bind.Log.WorkName + ".m3u";

                if (Bind.LogRipper == null)
                {
                    System.Diagnostics.Debug.Assert (Bind.Signature != null);
                    try
                    {
                        using (var fs0 = new FileStream (newMd5Path, FileMode.CreateNew))
                        {
                            var flacRenameCount = Bind.Log.Issues.Items.Count (x => (x.Tag & IssueTags.NameChange) != 0);

                            if (Bind.Log.WorkName != Bind.WorkName && File.Exists (oldM3uPath))
                                try
                                { File.Move (oldM3uPath, newM3uPath); }
                                catch (Exception ex)
                                {
                                    Owner.SetCurrentFile (oldM3uName);
                                    Owner.ReportLine ("Write to playlist failed: " + ex.Message.Trim(null), Severity.Error, true);
                                    return;
                                }

                            ValidateM3u (newM3uPath, flacRenameCount > 0);
                            if (Bind.M3u == null || Bind.M3u.Issues.HasError)
                                return;

                            Md5Model = new Md5Format.Model (fs0, newMd5Path, Bind.Log, newLogName, Bind.M3u, Bind.Signature);
                            Bind.Md5 = Md5Model.Bind;

                            if (Owner.Data.WillProve && LogModel.Bind.ShIssue != null && LogModel.Bind.ShIssue.Success)
                            {
                                Md5Model.HistoryModel.Add ("proved", Bind.Signature);
                                Bind.IsProven = true;
                            }

                            Md5Model.WriteFile (Owner.Data.Product + " v" + Owner.Data.ProductVersion, LogBuffer.cp1252);
                            Md5Model.ClearFile();
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Owner.ReportLine (ex.Message, Severity.Error, Bind.Signature != null);
                        return;
                    }
                    finally
                    {
                        if (Bind.Md5 == null && File.Exists (newMd5Path))
                            File.Delete (newMd5Path);
                    }

                    ++Owner.Data.Md5Format.TotalCreated;
                    Md5Model.IssueModel.Add ("Digest created.", Severity.Advisory);

                    return;
                }

                var oldMd5Name = Bind.WorkName + ".FLAC." + Bind.LogRipper + ".md5";
                var oldMd5Path = Owner.Data.CurrentDirectory + Path.DirectorySeparatorChar + oldMd5Name;

                try
                {
                    var fs0 = new FileStream (oldMd5Path, FileMode.Open, Bind.Signature == null? FileAccess.Read : FileAccess.ReadWrite, FileShare.Read|FileShare.Delete);
                    var hdr = new byte[0x2C];
                    int got = fs0.Read (hdr, 0, hdr.Length);
                    Md5Model = Md5Format.CreateModel (fs0, hdr, oldMd5Path);
                    Bind.Md5 = Md5Model.Bind;
                    Md5Model.CalcHashes (Hashes.Intrinsic, Validations.None);
                    if (Bind.Md5.Issues.MaxSeverity >= Severity.Error)
                        return;

                    if (Md5Model.HistoryModel != null && Md5Model.HistoryModel.Bind.Prover != null)
                    {
                        Md5Model.IssueModel.Add ("Highest quality previously proven.", Severity.Trivia);
                        Bind.IsProven = true;
                    }
                    else if (Owner.Data.IsWebCheckEnabled && LogModel.Bind.ShIssue == null)
                    {
                        LogModel.CalcHashWebCheck();
                        if (LogModel.Bind.ShIssue.Failure)
                            Md5Model.IssueModel.Add ("EAC log self-hash verify failed!");
                        else
                            Md5Model.IssueModel.Add ("EAC log self-hash verify successful.", Severity.Advisory);
                    }
                    else
                    {
                        Severity sev = Severity.Noise;
                        if (Owner.Data.WillProve && (LogModel.Bind.ShIssue == null || !LogModel.Bind.ShIssue.Success
                                                    || LogModel.Bind.TpIssue == null || LogModel.Bind.TpIssue.Failure))
                            sev = Severity.Error;
                        Md5Model.IssueModel.Add ("Highest quality not previously proven.", sev);
                    }

                    HashedFile m3uFile = null, logFile = null;
                    int m3uIndex = -1, logIndex = -1;
                    int ignoredDigestLineCount = 2;
                    bool isTrackBlockComplete = false;
                    int flacBaseIndex = -1;

                    for (var ix = 0; ix < Bind.Md5.HashedFiles.Items.Count; ++ix)
                    {
                        var hashLine = Bind.Md5.HashedFiles.Items[ix];
                        var ext = Path.GetExtension(hashLine.FileName).ToLower();

                        if (ext == ".flac")
                        {
                            if (isTrackBlockComplete)
                            { Md5Model.IssueModel.Add ("FLACs must be in a single block."); return; }
                            if (flacBaseIndex < 0)
                                flacBaseIndex = ix;
                        }
                        else
                        {
                            if (! isTrackBlockComplete)
                            {
                                if (flacBaseIndex >= 0)
                                    isTrackBlockComplete = true;
                            }
                            if (ext == ".log")
                            { logFile = hashLine; logIndex = ix; }
                            else if (ext == ".m3u")
                            { m3uFile = hashLine; m3uIndex = ix; }
                            else if (Bind.Md5.History == null && filterOnConversion.Contains (ext))
                            {
                                // Legacy workaround.
                                Md5Model.IssueModel.Add ("Ignoring '" + hashLine.FileName + "'.", Severity.Warning);
                                ++ignoredDigestLineCount;
                            }
                            else
                            {
                                Md5Model.IssueModel.Add ("Bad file type on '" + hashLine.FileName + "'.");
                                return;
                            }
                        }
                    }

                    if (logIndex < 0)
                    { Md5Model.IssueModel.Add ("Missing .log file."); return; }
                    Md5Model.HashedModel.SetActualHash (logIndex, Bind.Log.FileMD5);

                    if (m3uIndex < 0)
                    { Md5Model.IssueModel.Add ("Missing .m3u file."); return; }

                    if (Bind.Md5.HashedFiles.Items.Count - ignoredDigestLineCount != Bind.Log.Tracks.Items.Count)
                    { Md5Model.IssueModel.Add ("Wrong number of .flac files (Expecting " + Bind.Log.Tracks.Items.Count + ")."); return; }

                    if (! Bind.Log.FileMD5Equals (logFile.StoredHash))
                        Md5Model.IssueModel.Add ("MD5 mismatch on EAC log.");
                    else if (! logFile.FileName.EndsWith (".FLAC." + Bind.LogRipper + ".log"))
                    {
                        Md5Model.IssueModel.Add ("EAC log name has been invalidated.");
                        return;
                    }

                    var trackWidth = Math.Max (Bind.Log.Tracks.WidestTrackWidth, 2);
                    for (var ix = 0; ix < Bind.Log.Tracks.Items.Count; ++ix)
                    {
                        FlacFormat.Model flacMod = LogModel.TracksModel.GetMatch (ix);
                        if (flacMod != null)
                        {
                            FlacFormat flac = Bind.Log.Tracks.Items[ix].Match;
                            HashedFile hashFlac = Bind.Md5.HashedFiles.Items[ix + flacBaseIndex];

                            if (Bind.Autoname != NamingStrategy.Manual)
                            {
                                var isRename = false;
                                var newName = flac.GetCleanFileName (Owner.Data.Autoname, Bind.Log.CalcedAlbumArtist, trackWidth);
                                if (newName != flac.Name)
                                {
                                    var err = flacMod.Rename (newName);
                                    if (err != null)
                                    {
                                        Md5Model.IssueModel.Add ("FLAC rename failed on '" + newName + "': " + err);
                                        return;
                                    }
                                    isRename = true;
                                }
                                if (newName != hashFlac.FileName)
                                {
                                    Md5Model.IssueModel.Add ("FLAC renamed to '" + newName + "'.", Severity.Advisory, IssueTags.NameChange);
                                    Md5Model.HashedModel.SetFileName (ix + flacBaseIndex, flac.Name);
                                }
                                else if (isRename)
                                    Md5Model.IssueModel.Add ("FLAC name repaired to '" + newName + "'.", Severity.Advisory);
                            }
                            else if (hashFlac.FileName != flac.Name)
                            {
                                Md5Model.IssueModel.Add ("FLAC was renamed to '" + flac.Name + "'.", Severity.Advisory, IssueTags.NameChange);
                                var newName = Map1252.ToClean1252FileName (flac.Name);
                                if (newName != flac.Name)
                                    Md5Model.IssueModel.Add ("FLAC file name is not Windows-1252 clean.");
                                else
                                    Md5Model.HashedModel.SetFileName (ix + flacBaseIndex, flac.Name);
                            }

                            // If the MD5 is different but the CRC-32 is same, must be metadata change.
                            if (! flac.FileMD5Equals (hashFlac.StoredHash))
                                Md5Model.IssueModel.Add ("MD5 mismatch on '" + flac.Name + "'.", Severity.Advisory, IssueTags.MetaChange);
                            Md5Model.HashedModel.SetActualHash (ix + flacBaseIndex, flac.FileMD5);
                        }
                    }

                    if (Bind.Md5.Issues.HasError)
                        return;

                    if (Bind.Log.WorkName != m3uFile.FileName.Substring (0, m3uFile.FileName.Length-4))
                    {
                        Md5Model.IssueModel.Add ("Album files " + (Bind.Autoname != NamingStrategy.Manual? "will be renamed." : "were renamed."), Severity.Advisory, IssueTags.AlbumChange);
                        Md5Model.HashedModel.SetFileName (m3uIndex, newM3uName);
                        Md5Model.HashedModel.SetFileName (logIndex, newLogName);
                    }
                    else if (Bind.Log.WorkName != Bind.WorkName)
                    {
                        System.Diagnostics.Debug.Assert (Bind.Autoname != NamingStrategy.Manual);

                        string err = null;
                        try
                        { File.Move (oldM3uPath, newM3uPath); }
                        catch (Exception ex)
                        { err = ex.Message.Trim(null); }
                        if (err == null)
                        {
                            err = Md5Model.Rename (newMd5Name);
                            if (err == null)
                                err = LogModel.Rename (newLogName);
                        }
                        if (err != null)
                        {
                            // This may be unexpectedly hit over a network connection to an 'older' OS.
                            // However, FileShare.Delete is supposed to allow renaming an open file.
                            Md5Model.IssueModel.Add ("Album file names repair failed: " + err);
                            return;
                        }

                        oldM3uPath = newM3uPath;
                        Md5Model.IssueModel.Add ("Album file names were repaired.", Severity.Advisory);
                    }

                    Bind.AlbumRenameCount = Bind.Md5.Issues.Items.Count (x => (x.Tag & (IssueTags.AlbumChange)) != 0);
                    Bind.TrackEditCount = Bind.Md5.Issues.Items.Count (x => (x.Tag & (IssueTags.MetaChange)) != 0);
                    Bind.TrackRenameCount = Bind.Md5.Issues.Items.Count (x => (x.Tag & IssueTags.NameChange) != 0);

                    try
                    {
                        var fs2 = new FileStream (oldM3uPath, FileMode.Open, Bind.Signature==null || Bind.TrackRenameCount==0? FileAccess.Read : FileAccess.ReadWrite, FileShare.Read|FileShare.Delete);
                        var buf = new byte[7];
                        fs2.Read (buf, 0, buf.Length);
                        M3uModel = M3uFormat.CreateModel (fs2, buf, oldM3uPath);
                        Bind.M3u = M3uModel.Bind;
                        ++Owner.Data.M3uFormat.TrueTotal;
                        ++Owner.Data.TotalFiles;

                        M3uModel.CalcHashes (Hashes.FileMD5, 0);
                        Md5Model.HashedModel.SetActualHash (m3uIndex, Bind.M3u.FileMD5);
                        if (! Bind.M3u.FileMD5Equals (m3uFile.StoredHash))
                        { Md5Model.IssueModel.Add ("MD5 mismatch on playlist."); return; }
                        if (Bind.M3u.Files.Items.Count != Bind.Log.Tracks.Items.Count)
                        { M3uModel.IssueModel.Add ("Wrong item count in .m3u (expecting " + Bind.Log.Tracks.Items.Count + ")."); return; }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Md5Model.IssueModel.Add ("Cannot open playlist: " + ex.Message.Trim());
                        return;
                    }
                    catch (FileNotFoundException)
                    {
                        ++Owner.Data.M3uFormat.TotalMissing;
                        Md5Model.IssueModel.Add ("Playlist missing.");
                        Md5Model.HashedModel.SetIsFound (m3uIndex, false);
                        return;
                    }

                    if (! Bind.HasChange)
                    {
                        var trailer = "MD5 checks of EAC log, playlist, " + Bind.Log.Tracks.Items.Count + " FLAC";
                        if (Bind.Log.Tracks.Items.Count != 1)
                            trailer += "s";
                        Md5Model.IssueModel.Add (trailer + " successful.", Severity.Trivia);
                    }
                    else if (Bind.Signature == null)
                        Md5Model.IssueModel.Escalate (IssueTags.None, IssueTags.AlbumChange|IssueTags.NameChange|IssueTags.MetaChange);

                    if (Bind.Signature == null)
                        return;

                    if (Bind.TrackEditCount > 0 && Bind.Md5.History != null && Bind.Md5.History.LastSig != Bind.Signature)
                    {
                        Md5Model.IssueModel.Add ("Non-audio edits made after signed by " + Bind.Md5.History.LastSig + ". You must sign before editing.");
                        return;
                    }

                    if (Bind.Md5.History == null)
                    {
                        Md5Model.CreateHistory();
                        Md5Model.HistoryModel.Add ("converted", Bind.Signature);
                        Md5Model.IssueModel.Add ("Will convert to include change log & self-hash.", Severity.Advisory);
                        Bind.IsCheck = true;
                        ++Owner.Data.Md5Format.TotalConverted;
                    }
                    else if (Bind.TrackEditCount == 0)
                    {
                        bool addProver = false;
                        if (Owner.Data.WillProve && Bind.Md5.History.Prover == null)
                            if (LogModel.Bind.ShIssue != null && LogModel.Bind.ShIssue.Success)
                                if (LogModel.Bind.TpIssue != null && ! LogModel.Bind.TpIssue.Failure)
                                    addProver = true;
                        string action = addProver? "proved" : "checked";

                        if (Bind.Signature != Bind.Md5.History.LastSig || addProver)
                        {
                            if (Bind.Md5.History.LastAction.ToLower().StartsWith ("checked"))
                                Md5Model.HistoryModel.Replace (action, Bind.Signature);
                            else
                                Md5Model.HistoryModel.Add (action, Bind.Signature);

                            Md5Model.IssueModel.Add ("Change log updated with '" + action + "'.", Severity.Advisory);
                            Bind.IsCheck = true;
                            if (! Bind.IsProven && addProver)
                                Bind.IsProven = true;
                        }
                    }
                }
                catch (FileNotFoundException)
                {
                    Owner.SetCurrentFile (oldMd5Name);
                    ++Owner.Data.Md5Format.TotalMissing;
                    Owner.ReportLine ("File missing.", Severity.Error, Bind.Signature != null);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Owner.SetCurrentFile (oldMd5Name);
                    Owner.ReportLine ("Open failed: " + ex.Message, Severity.Error, Bind.Signature != null);
                }
            }


            private string ValidateGetComment()
            {
                string newComment = String.Empty;

                if (! Bind.IsCheck && Bind.LogRipper != null && Bind.HasChange)
                {
                    var sb = new StringBuilder();
                    if (Bind.TrackEditCount > 0)
                    {
                        sb.Append (Bind.TrackEditCount);
                        sb.Append (" FLAC");
                        sb.Append (Bind.TrackEditCount == 1? " has" : "s have");
                        sb.Append (" changes (probably the tags)");
                    }
                    if (Bind.TrackRenameCount > 0)
                    {
                        if (sb.Length > 0)
                            sb.Append (", ");
                        sb.Append (Bind.TrackRenameCount);
                        sb.Append (" FLAC");
                        sb.Append (Bind.TrackRenameCount == 1? " was renamed" : "s were renamed");
                    }
                    if (Bind.AlbumRenameCount > 0)
                    {
                        if (sb.Length > 0)
                            sb.Append (", ");
                        sb.Append  ("3 album files " + (Bind.Autoname != NamingStrategy.Manual? "will be renamed" : "were renamed"));
                    }
                    sb.Append ('.');

                    var reallyPrompt = "Are you sure this should be applied to all rips signed in this batch (Y/N)? ";

                    newComment = Owner.Data.InputLine (sb.ToString(), "Comment", reallyPrompt);
                }
                return newComment;
            }


            public void Commit (string comment)
            {
                if (! Bind.IsWip)
                    return;
                Bind.IsWip = false;

                if (Bind.Signature == null)
                    return;

                string firstSig = Bind.LogRipper ?? Bind.Signature;
                var newLogName = Bind.Log.WorkName + ".FLAC." + firstSig + ".log";
                var newMd5Name = Bind.Log.WorkName + ".FLAC." + firstSig + ".md5";
                if (comment != null) comment = comment.Trim(null);

                if (Bind.HasChange && ! Bind.IsCheck)
                {
                    if (String.IsNullOrWhiteSpace (comment))
                    {
                        Bind.NewComment = String.Empty;
                        Md5Model.IssueModel.Escalate (IssueTags.None, IssueTags.MetaChange);
                        Md5Model.IssueModel.Add ("Changes made after last signed but empty response cannot be logged.");
                        Owner.ReportFormat (Bind.Md5, true);
                        ++Owner.Data.TotalErrors;
                        Bind.Status = Severity.Error;
                        CloseFiles();
                        return;
                    }

                    Bind.NewComment = comment;
                    Md5Model.HistoryModel.Add ("changed: " + comment, Bind.Signature);

                    if (Owner.Data.WillProve && Bind.Md5.History.Prover == null)
                        if (LogModel.Bind.ShIssue != null && LogModel.Bind.ShIssue.Success)
                            if (LogModel.Bind.TpIssue != null && ! LogModel.Bind.TpIssue.Failure)
                                Md5Model.HistoryModel.Add ("proved", Bind.Signature);
                }

                string workErr = null;
                if (Bind.Md5.History != null && Bind.Md5.History.IsDirty)
                {
                    if (Bind.TrackRenameCount > 0)
                    {
                        for (var ix = 0; ix < Bind.M3u.Files.Items.Count; ++ix)
                            M3uModel.FilesModel.SetName (ix, Bind.Log.Tracks.Items[ix].Match.Name);
                        M3uModel.WriteFile();
                    }
                    M3uModel.CalcHashes (Hashes.FileMD5, 0);
                    var m3uHashedIndex = Bind.Md5.HashedFiles.LookupIndexByExtension (".m3u");
                    Md5Model.HashedModel.SetActualHash (m3uHashedIndex, Bind.M3u.FileMD5);
                    Md5Model.WriteFile (Owner.Data.Product + " v" + Owner.Data.ProductVersion, LogBuffer.cp1252);

                    if (Bind.TrackEditCount > 0 || Bind.TrackRenameCount > 0)
                        for (var ix = 0; ix < Bind.Md5.HashedFiles.Items.Count; ++ix)
                            Md5Model.HashedModel.SetStoredHashToActual (ix);

                    if (Bind.Log.WorkName != Bind.WorkName)
                    {
                        workErr = Md5Model.Rename (newMd5Name);
                        if (workErr != null)
                            workErr = "MD5 digest rename failed on '" + newMd5Name + "': " + workErr;
                        else
                        {
                            var newM3uName = Bind.Log.WorkName + ".m3u";
                            workErr = M3uModel.Rename (newM3uName);
                            if (workErr != null)
                                workErr = "Playlist rename failed on '" + newM3uName + "': " + workErr;
                            else
                                try
                                {
                                    File.Move (Bind.Log.Path, Bind.DirPath + Path.DirectorySeparatorChar + newLogName);
                                    Md5Model.IssueModel.Add ("3 album files were renamed.", Severity.Advisory);
                                    Bind.Md5.Issues.NotifyPropertyChanged ("Items");
                                }
                                catch (Exception ex)
                                { workErr = "Log rename failed on '" + newLogName + "': " + ex.Message.Trim (null); }
                        }
                    }
                }

                CloseFiles();
                Owner.SetCurrentFile (null);

                if (workErr != null)
                {
                    Owner.SetCurrentFile (null);
                    Owner.ReportLine (workErr, Severity.Error, true);
                    Bind.Status = Severity.Error;
                    return;
                }

                var errInfo = new FileInfo (Bind.DirPath + Path.DirectorySeparatorChar + Owner.Data.NoncompliantName);
                if (File.Exists (errInfo.FullName))
                    try
                    { File.Delete (errInfo.FullName); }
                    catch (Exception)
                    { /* and why not */ }

                if (Bind.LogRipper == null)
                {
                    try
                    { File.Move (Bind.Log.Path, Owner.Data.CurrentDirectory + Path.DirectorySeparatorChar + newLogName); }
                    catch (Exception ex)
                    {
                        Owner.SetCurrentFile (null);
                        var err = "EAC log rename failed: " + ex.Message.Trim (null);
                        Owner.ReportLine (err, Severity.Error, true);
                        LogModel.IssueModel.Add (err);
                        Bind.Status = Severity.Error;
                        return;
                    }

                    ++Owner.Data.LogFormat.TotalSigned;
                }

                if (Bind.Autoname == NamingStrategy.Manual)
                {
                    if (Bind.Dir.Name.StartsWith (Owner.Data.FailPrefix))
                    {
                        try
                        {
                            var newName = Bind.Dir.Parent.FullName + Path.DirectorySeparatorChar + Bind.Dir.Name.Substring (Owner.Data.FailPrefix.Length);
                            Bind.Dir.MoveTo (newName);
                        }
                        catch (Exception)
                        { /* ignore all */ }
                    }
                }
                else if (Bind.Log.WorkName != Bind.Dir.Name)
                {
                    try
                    {
                        var newName = Bind.Dir.Parent.FullName + Path.DirectorySeparatorChar + Bind.Log.WorkName;

                        if (Bind.Dir.Name.ToLower() == Bind.Log.WorkName.ToLower())
                        {
                            // Work around Windows case insensitivity with a trailing macron:
                            var tempName = newName + '¯';
                            Bind.Dir.MoveTo (tempName);
                            Directory.Move (tempName, newName);
                        }
                        else
                            Bind.Dir.MoveTo (newName);
                    }
                    catch (Exception ex)
                    {
                        // Not worth fretting over.
                        Owner.SetCurrentFile (null);
                        Owner.ReportLine ("Directory rename failed: " + ex.Message.Trim (null), Severity.Warning);
                        if (Bind.Status < Severity.Warning)
                            Bind.Status = Severity.Warning;
                    }
                }
            }


            public void CloseFiles()
            {
                Bind.IsWip = false;

                if (LogModel != null) LogModel.CloseFile();
                if (M3uModel != null) M3uModel.CloseFile();
                if (Md5Model != null) Md5Model.CloseFile();
            }


#region IDisposable Support
            private bool isDisposed = false;

            protected virtual void Dispose (bool disposing)
            {
                if (! isDisposed)
                {
                    if (disposing)
                        CloseFiles();
                    isDisposed = true;
                }
            }

            public void Dispose()
            {
                Dispose (true);
            }
#endregion
        }
    }
}
#endif

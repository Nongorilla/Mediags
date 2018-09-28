using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public partial class LogEacFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "log" }; } }

        public static string Subname
        { get { return "EAC"; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (StartsWith (hdr, logEacSig0x) || StartsWith (hdr, logEacSig0y) || StartsWith (hdr, logEacSig1x))
                return new Model (stream, hdr, path);
            return null;
        }


        public partial class Model : FormatBase.ModelBase
        {
            public new readonly LogEacFormat Data;
            public LogEacTrack.Vector.Model TracksModel;
            private LogBuffer parser;

            public Model (Stream stream, byte[] hdr, string path)
            {
                TracksModel = new LogEacTrack.Vector.Model();
                base._data = Data = new LogEacFormat (stream, path, TracksModel.Bind);
                Data.Issues = IssueModel.Data;

                Data.AccurateRip = null;
                Data.RipDate = String.Empty;

                // Arbitrary limit.
                if (Data.FileSize > 250000)
                {
                    IssueModel.Add ("File insanely huge.", Severity.Fatal);
                    return;
                }

                Data.fBuf = new byte[Data.FileSize];
                Data.fbs.Position = 0;
                var got = Data.fbs.Read (Data.fBuf, 0, (int) Data.FileSize);
                if (got != Data.FileSize)
                {
                    IssueModel.Add ("Read failed", Severity.Fatal);
                    return;
                }

                if (got < 2 || Data.fBuf[0] != 0xFF || Data.fBuf[1] != 0xFE)
                    Data.Codepage = Encoding.GetEncoding (1252);
                else
                    Data.Codepage = Encoding.Unicode;

                parser = new LogBuffer (Data.fBuf, Data.Codepage);
                string lx = ParseHeader();

                if (! Data.IsRangeRip)
                {
                    lx = ParseTracks (lx);
                    if (Data.Issues.HasFatal)
                        return;
                }

                while (! parser.EOF && ! lx.Contains ("errors") && ! lx.StartsWith ("==== "))
                    lx = parser.ReadLineLTrim();

                if (lx == "No errors occured" || lx == "No errors occurred")
                    lx = parser.ReadLineLTrim();
                else if (lx == "There were errors")
                {
                    if (Data.Issues.MaxSeverity < Severity.Error)
                        IssueModel.Add ("There were errors.");
                    lx = parser.ReadLineLTrim();
                }
                else
                    IssueModel.Add ("Missing 'errors' line.");

                if (Data.IsRangeRip)
                {
                    if (lx == "AccurateRip summary")
                    {
                        for (;;)
                        {
                            lx = parser.ReadLineLTrim();
                            if (parser.EOF || ! lx.StartsWith ("Track "))
                                break;

                            if (lx.Contains ("ccurately ripped (confidence "))
                            {
                                int arVersion = lx.Contains("AR v2")? 2 : 1;
                                bool isOk = ToInt (lx, 40, out int val);
                                int arConfidence = isOk && val > 0? val : -1;
                                lx = parser.ReadLineLTrim();

                                if (Data.AccurateRipConfidence == null || Data.AccurateRipConfidence.Value > arConfidence)
                                    Data.AccurateRipConfidence = arConfidence;
                                if (Data.AccurateRip == null || Data.AccurateRip.Value > arVersion)
                                    Data.AccurateRip = arVersion;
                            }
                        }
                    }
                }

                if (lx == "All tracks accurately ripped")
                    lx = parser.ReadLineLTrim();

                if (lx.StartsWith ("End of status report"))
                    lx = parser.ReadLineLTrim();

                if (lx.StartsWith ("---- CUETools"))
                    lx = ParseCueTools (lx);

                while (! parser.EOF && ! lx.StartsWith ("==== "))
                    lx = parser.ReadLine();

                if (lx.StartsWith ("==== Log checksum ") && lx.Length >= 82)
                {
                    Data.storedHash = ConvertTo.FromHexStringToBytes (lx, 18, 32);

                    lx = parser.ReadLine();
                    if (! parser.EOF || ! String.IsNullOrEmpty (lx))
                        IssueModel.Add ("Unexpected content at end of file.", Severity.Warning, IssueTags.ProveErr);
                }

                parser = null;
                GetDiagnostics();
            }


            public Issue TkIssue { get { return Data.TkIssue; } set { Data.TkIssue = value; } }

            public void SetGuiTracks()
            { Data.GuiTracks = Data.Tracks; }


            public void SetWorkName (string workName)
            { Data.WorkName = workName; }

            public void MatchFlacs (IList<FlacFormat.Model> flacMods)
            {
                int expectNum = -1,
                    mx = Math.Min (Data.Tracks.Items.Count, flacMods.Count);

                for (int ix = 0; ix < mx; ++ix)
                {
                    LogEacTrack track = Data.Tracks.Items[ix];
                    FlacFormat.Model flacMod = flacMods[ix];
                    FlacFormat flac = flacMod.Data;

                    if (flac.ActualPcmCRC32.Value != track.CopyCRC.Value)
                        Data.TkIssue = IssueModel.Add ("Audio CRC-32 mismatch on '" + flac.Name + "'.", Severity.Fatal, IssueTags.Failure);
                    else
                        TracksModel.SetMatch (ix, flacMod);

                    string trackNumTag = flac.GetTag ("TRACKNUMBER");
                    if (String.IsNullOrEmpty (trackNumTag))
                    { IssueModel.Add ("Missing TRACKNUMBER tag."); continue; }

                    // Forgive bad formatting for now and just get the track#.
                    var integerRegex = new Regex (@"^([0-9]+)", RegexOptions.Compiled);
                    MatchCollection reMatches = integerRegex.Matches (trackNumTag);
                    string trackNumTagCapture = reMatches.Count == 1? reMatches[0].Groups[1].ToString() : trackNumTag;

                    if (! int.TryParse (trackNumTagCapture, out int trackNum))
                        IssueModel.Add ("Invalid TRACKNUMBER tag '" + trackNumTag + "'.");
                    else
                    {
                        if (expectNum >= 0 && expectNum != trackNum)
                            IssueModel.Add ("Unexpected TRACKNUMBER '" + trackNum + "': Expecting '" + expectNum + "'.");
                        expectNum = trackNum + 1;
                    }
                }
                if (flacMods.Count != Data.Tracks.Items.Count)
                    Data.TkIssue = IssueModel.Add ("Rip not complete.", Severity.Fatal, IssueTags.Failure);
            }


            private static string CheckDate (string date)
            {
                if ((date.Length != 4 && date.Length != 10) || (! date.StartsWith ("19") && ! date.StartsWith ("20"))
                                                            || ! Char.IsDigit (date[2]) || ! Char.IsDigit (date[3]))
                    return "tag not like YYYY or YYYY-MM-DD with YYYY in 1900 to 2099.";
                return null;
            }


            private void CheckWhite (string tagName, string tagVal)
            {
                if (tagVal.StartsWith (" "))
                    IssueModel.Add (tagName + " tag has leading white space.", Severity.Warning, IssueTags.BadTag);
                if (tagVal.EndsWith (" "))
                    IssueModel.Add (tagName + " tag has trailing white space.", Severity.Warning, IssueTags.BadTag);
                if (tagVal.Contains ("  "))
                    IssueModel.Add (tagName + " tag has adjacent spaces.", Severity.Warning, IssueTags.BadTag);
            }


            public void ValidateFlacTags()
            {
                var vendor = Data.Tracks.Items[0].Match.Blocks.Tags.Vendor;
                if (vendor == null)
                    IssueModel.Add ("FLAC vendor string is missing, cannot identify FLAC version.", Severity.Fatal);
                else
                {
                    //
                    // flac.exe v1.2.1 & 1.3 produce the same encoding at the binary level (for 2 channel) so both are blessed.
                    // flac.exe v1.3 is optimized for newer hardware and fixes a bug encountered when file names include exotic characters.
                    //

                    var v2 = vendor.ToLower();
                    if (! (v2.Contains ("flac 1.2.1") || v2.Contains ("flac 1.3") || v2.Contains ("flac 1.4") || v2.Contains ("flac 2.")))
                        IssueModel.Add ("FLAC version identified by '" + vendor + "' is discouraged.", Severity.Noise, IssueTags.ProveErr);
                }

                bool? isSameDate = Data.Tracks.IsFlacTagsAllSame ("DATE");
                if (isSameDate == false)
                    IssueModel.Add ("Inconsistent DATE tag.");
                else if (isSameDate == null)
                    IssueModel.Add ("Missing DATE tag.", Severity.Warning, IssueTags.BadTag);
                else
                {
                    Data.TaggedDate = Data.Tracks.Items[0].Match.GetTag ("DATE");
                    var err = CheckDate (Data.TaggedDate);
                    if (err != null)
                        IssueModel.Add ("DATE " + err, Severity.Warning, IssueTags.BadTag);
                }

                bool? isSameReleaseDate = Data.Tracks.IsFlacTagsAllSame ("RELEASE DATE");
                if (isSameReleaseDate == false)
                    IssueModel.Add ("Inconsistent RELEASE DATE tag.");
                else if (isSameReleaseDate == true)
                {
                    Data.TaggedReleaseDate = Data.Tracks.Items[0].Match.GetTag ("RELEASE DATE");
                    var err = CheckDate (Data.TaggedReleaseDate);
                    if (err != null)
                        IssueModel.Add ("RELEASE DATE " + err, Severity.Warning, IssueTags.BadTag);

                    if (isSameDate == null)
                        IssueModel.Add ("RELEASE DATE without DATE tag.");
                }

                bool? sameAlbum = Data.Tracks.IsFlacTagsAllSame ("ALBUM");
                if (sameAlbum != true)
                    IssueModel.Add ("Missing or inconsistent ALBUM tag.");
                else
                {
                    Data.TaggedAlbum = Data.Tracks.Items[0].Match.GetTag ("ALBUM");
                    CheckWhite ("ALBUM", Data.TaggedAlbum);
                }

                bool? isSameAlbumArtist = Data.Tracks.IsFlacTagsAllSame ("ALBUMARTIST");
                if (isSameAlbumArtist == true)
                {
                    Data.TaggedAlbumArtist = Data.Tracks.Items[0].Match.GetTag ("ALBUMARTIST");
                    CheckWhite ("ALBUMARTIST", Data.TaggedAlbumArtist);
                    Data.CalcedAlbumArtist = Data.TaggedAlbumArtist;
                }
                else if (isSameAlbumArtist == false)
                    IssueModel.Add ("Inconsistent ALBUMARTIST tag.");

                bool? isSameArtist = Data.Tracks.IsFlacTagsAllSame ("ARTIST");
                if (isSameArtist == true)
                    CheckWhite ("ARTIST", Data.Tracks.Items[0].Match.GetTag ("ARTIST"));

                if (isSameAlbumArtist == null)
                    if (isSameArtist == false)
                        IssueModel.Add ("Inconsistent ARTIST or missing ALBUMARTIST tag.", Severity.Warning, IssueTags.BadTag);
                    else if (isSameArtist == null)
                        IssueModel.Add ("Missing ARTIST tag.", Severity.Warning, IssueTags.Substandard);
                    else
                        Data.CalcedAlbumArtist = Data.Tracks.Items[0].Match.GetTag ("ARTIST");

                bool? isSameBarcode = Data.Tracks.IsFlacTagsAllSame ("BARCODE");
                if (isSameBarcode == false)
                    IssueModel.Add ("Inconsistent BARCODE tag.");

                bool? isSameCompilation = Data.Tracks.IsFlacTagsAllSame ("COMPILATION");
                if (isSameCompilation == false)
                    IssueModel.Add ("Inconsistent COMPILATION tag.");

                foreach (var item in Data.Tracks.Items)
                {
                    var flac = item.Match;
                    if (flac != null)
                    {
                        var trackTag = flac.GetTag ("TRACKNUMBER");

                        // Report when track number like 02 or 2/9.
                        var match = Regex.Match (trackTag, "^[1-9]+[0-9]*$");
                        if (! match.Success)
                            IssueModel.Add ("Malformed TRACKNUMBER tag '" + trackTag + "'.", Severity.Warning, IssueTags.BadTag);

                        var title = flac.GetTag ("TITLE");
                        CheckWhite ("TITLE (track "+trackTag+")", title);
                        if (isSameArtist == false)
                            CheckWhite ("ARTIST (track " + trackTag + ")", flac.GetTag ("ARTIST"));

                        var ordTag = flac.GetTag ("ORIGINAL RELEASE DATE");
                        if (! String.IsNullOrEmpty (ordTag))
                        {
                            string err = CheckDate (ordTag);
                            if (err != null)
                                IssueModel.Add ("ORIGINAL RELEASE DATE " + err, Severity.Warning, IssueTags.BadTag);
                        }
                    }
                }

                bool? isSameTrackTotal = Data.Tracks.IsFlacTagsAllSame ("TRACKTOTAL");
                if (isSameTrackTotal == false)
                    IssueModel.Add ("Inconsistent TRACKTOTAL tag.");
                else if (isSameTrackTotal == true)
                {
                    string ttt = Data.Tracks.Items[0].Match.GetTag ("TRACKTOTAL");
                    bool isOK = int.TryParse (ttt, out int ttn);
                    if (! isOK)
                        IssueModel.Add ("Malformed TRACKTOTAL tag '" + ttt + "'.");

                    if (ttn != Data.Tracks.Items.Count)
                        IssueModel.Add ("Wrong TRACKTOTAL tag. Expecting " + Data.Tracks.Items.Count + ", got " + ttn + ".");
                }

                bool? isSameDisc = Data.Tracks.IsFlacTagsAllSame ("DISCNUMBER");
                if (isSameDisc == true)
                    Data.TaggedDisc = Data.Tracks.Items[0].Match.GetTag ("DISCNUMBER");
                else if (isSameDisc == false)
                    IssueModel.Add ("Inconsistent DISCNUMBER tag.");

                bool? isSameDiscTotal = Data.Tracks.IsFlacTagsAllSame ("DISCTOTAL");
                if (isSameDiscTotal == true)
                    Data.TaggedDiscTotal = Data.Tracks.Items[0].Match.GetTag ("DISCTOTAL");
                else if (isSameDiscTotal == false)
                    IssueModel.Add ("Inconsistent DISCTOTAL tag.");

                bool? isSameOrg = Data.Tracks.IsFlacTagsAllSame ("ORGANIZATION");
                if (isSameOrg == true)
                    Data.TaggedOrg = Data.Tracks.Items[0].Match.GetTag ("ORGANIZATION");
                else if (isSameOrg == false)
                    IssueModel.Add ("Inconsistent ORGANIZATION tag.");

                bool? isSameCat = Data.Tracks.IsFlacTagsAllSame ("CATALOGNUMBER");
                if (isSameCat == false)
                    IssueModel.Add ("Inconsistent CATALOGNUMBER tag.");

                bool? isSameEdition = Data.Tracks.IsFlacTagsAllSame ("EDITION");
                if (isSameEdition == true)
                    Data.TaggedEdition = Data.Tracks.Items[0].Match.GetTag ("EDITION");

                bool? isSameSubtitle = Data.Tracks.IsFlacTagsAllSame ("SUBTITLE");
                if (isSameSubtitle == true)
                    Data.TaggedSubtitle = Data.Tracks.Items[0].Match.GetTag ("SUBTITLE");

                bool? isSameAASO = Data.Tracks.IsFlacTagsAllSameMulti ("ALBUMARTISTSORTORDER");
                if (isSameAASO == false)
                    IssueModel.Add ("Inconsistent ALBUMARTISTSORTORDER tag.");

                if (Data.Tracks.AnyHas ("ALBUM ARTIST"))
                        IssueModel.Add ("Use of ALBUM ARTIST tag not preferred, use ALBUMARTIST instead.", Severity.Warning, IssueTags.BadTag);

                if (Data.Tracks.AnyHas ("ALBUMARTISTSORT"))
                        IssueModel.Add ("Use of ALBUMARTISTSORT tag not preferred, use ALBUMARTISTSORTORDER instead.", Severity.Warning, IssueTags.BadTag);

                if (Data.Tracks.AnyHas ("PUBLISHER"))
                        IssueModel.Add ("Use of PUBLISHER tag not preferred, use ORGANIZATION instead.", Severity.Warning, IssueTags.BadTag);

                if (Data.Tracks.AnyHas ("TOTALTRACKS"))
                        IssueModel.Add ("Use of TOTALTRACKS tag deprecated, use TRACKTOTAL instead.", Severity.Warning, IssueTags.BadTag);

                if (Data.Tracks.AnyHas ("TOTALDISCS"))
                        IssueModel.Add ("Use of TOTALDISCS tag deprecated, use DISCTOTAL instead.", Severity.Warning, IssueTags.BadTag);
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                base.CalcHashes (hashFlags, validationFlags);

                if ((hashFlags & Hashes.WebCheck) != 0)
                    CalcHashWebCheck();
            }

            public void CalcHashWebCheck()
            {
                if (Data.storedHash == null)
                {
                    Severity sev = Data.EacVersionText != null && Data.EacVersionText.StartsWith ("1")? Severity.Warning : Severity.Noise;
                    Data.ShIssue = IssueModel.Add ("EAC log self-hash not present.", sev, IssueTags.ProveErr|IssueTags.Fussy);
                }
                else
                {
                    string boundary = "---------------------------" + DateTime.Now.Ticks;
                    string header = "Content-Disposition: form-data; name=\"LogFile\"; filename=\""
                                    + "SubmittedByMediags.log\"\r\nContent-Type: application/octet-stream\r\n\r\n";

                    byte[] bndBuf = Encoding.UTF8.GetBytes ("\r\n--" + boundary + "\r\n");
                    byte[] hdrBuf = Encoding.UTF8.GetBytes (header);
                    byte[] tlrBuf = Encoding.UTF8.GetBytes ("\r\n--" + boundary + "--\r\n");

                    var req = (HttpWebRequest) WebRequest.Create ("http://www.exactaudiocopy.de/log/check.aspx");
                    req.ContentType = "multipart/form-data; boundary=" + boundary;
                    req.Method = "POST";
                    req.KeepAlive = true;
                    req.Credentials = CredentialCache.DefaultCredentials;

                    try
                    {
                        using (var qs = req.GetRequestStream())
                        {
                            qs.Write (bndBuf, 0, bndBuf.Length);
                            qs.Write (hdrBuf, 0, hdrBuf.Length);
                            qs.Write (Data.fBuf, 0, Data.fBuf.Length);
                            qs.Write (tlrBuf, 0, tlrBuf.Length);
                        }

                        using (WebResponse res = req.GetResponse())
                            using (Stream ps = res.GetResponseStream())
                                using (StreamReader rdr = new StreamReader (ps))
                                {
                                    string answer = rdr.ReadLine();
                                    if (answer.Contains ("is fine"))
                                        Data.ShIssue = IssueModel.Add ("EAC log self-hash verify successful.", Severity.Advisory, IssueTags.Success);
                                    else if (answer.Contains ("incorrect"))
                                        Data.ShIssue = IssueModel.Add ("EAC log self-hash mismatch, file has been modified.", Severity.Error, IssueTags.Failure);
                                    else
                                        Data.ShIssue = IssueModel.Add ("EAC log self-hash verify attempt returned unknown result.", Severity.Advisory, IssueTags.ProveErr);
                                }
                    }
                    catch (Exception ex)
                    { Data.ShIssue = IssueModel.Add ("EAC log self-hash verify attempt failed: " + ex.Message.Trim (null), Severity.Warning, IssueTags.ProveErr); }
                }
            }


            private void GetDiagnostics()
            {
                if (String.IsNullOrEmpty (Data.Artist))
                    IssueModel.Add ("Missing artist", Severity.Warning, IssueTags.Substandard);

                if (String.IsNullOrEmpty (Data.Album))
                    IssueModel.Add ("Missing album", Severity.Warning, IssueTags.Substandard);

                if (String.IsNullOrEmpty (Data.Drive))
                    IssueModel.Add ("Missing 'Used drive'.");

                if (String.IsNullOrEmpty (Data.ReadMode))
                    IssueModel.Add ("Missing 'Read mode'.");
                else if (Data.ReadMode != "Secure with NO C2, accurate stream, disable cache"
                      && Data.ReadMode != "Secure with NO C2, accurate stream,  disable cache")
                {
                    if (Data.ReadMode != "Secure")
                        Data.DsIssue = IssueModel.Add ("Nonpreferred drive setting: Read mode: " + Data.ReadMode, Severity.Warning, IssueTags.Substandard);

                    if (Data.AccurateStream == null || Data.AccurateStream != "Yes")
                        Data.DsIssue = IssueModel.Add ("Missing drive setting: 'Utilize accurate stream: Yes'." + Data.AccurateStream, Severity.Warning, IssueTags.Substandard);

                    if (Data.DefeatCache == null || Data.DefeatCache != "Yes")
                        Data.DsIssue = IssueModel.Add ("Missing drive setting: 'Defeat audio cache: Yes'.", Severity.Warning, IssueTags.Substandard);

                    if (Data.UseC2 == null || Data.UseC2 != "No")
                        Data.DsIssue = IssueModel.Add ("Missing drive setting: 'Make use of C2 pointers: No'.", Severity.Warning, IssueTags.Substandard);
                }

                if (String.IsNullOrEmpty (Data.ReadOffset))
                    IssueModel.Add ("Missing 'Read offset correction'.", Severity.Trivia, IssueTags.Substandard);

                if (Data.FillWithSilence != null && Data.FillWithSilence != "Yes")
                    IssueModel.Add ("Missing 'Fill up missing offset samples with silence: Yes'.", Severity.Trivia, IssueTags.ProveWarn);

                if (Data.Quality != null && Data.Quality != "High")
                    IssueModel.Add ("Missing 'Quality: High'.", Severity.Advisory, IssueTags.Substandard);

                if (Data.TrimSilence == null || Data.TrimSilence != "No")
                    Data.TsIssue = IssueModel.Add ("Missing 'Delete leading and trailing silent blocks: No'.", Severity.Warning, IssueTags.Substandard);

                if (Data.CalcWithNulls != null && Data.CalcWithNulls != "Yes")
                    IssueModel.Add ("Missing 'Null samples used in CRC calculations: Yes'.");

                if (Data.GapHandling != null)
                    if (Data.GapHandling != "Appended to previous track")
                    {
                        IssueTags gapTag = IssueTags.Fussy;
                        if (Data.GapHandling != "Not detected, thus appended to previous track")
                            gapTag |= IssueTags.Substandard;

                        Data.GpIssue = IssueModel.Add ("Gap handling preferred setting is 'Appended to previous track'.", Severity.Advisory, gapTag);
                    }

                if (Data.Id3Tag == "Yes")
                    IssueModel.Add ("Append ID3 tags preferred setting is 'No'.", Severity.NoIssue, IssueTags.Fussy);

                if (Data.ReadOffset == "0" && Data.Drive.Contains ("not found in database"))
                    IssueModel.Add ("Unknown drive with offset '0'.", Severity.Advisory, IssueTags.Fussy);

                if (Data.NormalizeTo != null)
                    Data.NzIssue = IssueModel.Add ("Use of normalization considered harmful.", Severity.Warning, IssueTags.Substandard);

                if (Data.SampleFormat != null && Data.SampleFormat != "44.100 Hz; 16 Bit; Stereo")
                    IssueModel.Add ("Missing 'Sample format: 44.100 Hz; 16 Bit; Stereo'.", Severity.Warning, IssueTags.Substandard);

                if (Data.IsRangeRip)
                    IssueModel.Add ("Range rip detected.", Severity.Advisory, IssueTags.Substandard);
                else
                {
                    if (! Data.Tracks.IsNearlyAllPresent())
                        Data.TkIssue = IssueModel.Add ("Gap detected in track numbers.");

                    if (Data.TocTrackCount != null)
                    {
                        int diff = Data.TocTrackCount.Value - Data.Tracks.Items.Count;
                        if (diff != 0)
                        {
                            Severity sev = diff == 1? Severity.Advisory : Severity.Error;
                            IssueModel.Add ("Found " + Data.Tracks.Items.Count + " of " + Data.TocTrackCount.Value + " tracks.", sev);
                        }
                    }
                }

                var tpTag = IssueTags.ProveErr;
                var arTag = IssueTags.None;
                var arSev = Severity.Trivia;
                if (Data.AccurateRipConfidence != null)
                    if (Data.AccurateRipConfidence.Value > 0)
                    {
                        tpTag = IssueTags.None;
                        arTag = IssueTags.Success;
                    }
                    else
                    {
                        arSev = Severity.Advisory;
                        if (Data.AccurateRipConfidence.Value < 0)
                            arTag = IssueTags.Failure;
                    }
                Data.ArIssue = IssueModel.Add ("AccurateRip verification " + Data.AccurateRipLong + ".", arSev, arTag);

                var ctSev = Severity.Trivia;
                var ctTag = IssueTags.None;
                if (Data.CueToolsConfidence == null)
                    ctTag = IssueTags.ProveErr;
                else if (Data.CueToolsConfidence.Value < 0)
                    ctSev = Severity.Error;
                else if (Data.CueToolsConfidence.Value == 0)
                    ctSev = Severity.Advisory;
                else
                {
                    ctTag = IssueTags.Success;
                    tpTag = IssueTags.None;
                }

                Data.CtIssue = IssueModel.Add ("CUETools DB verification " + Data.CueToolsLong + ".", ctSev, ctTag);

                var kt = Data.Tracks.Items.Where (it => it.TestCRC != null).Count();
                if (kt == 0)
                    Data.TpIssue = IssueModel.Add ("Test pass not performed.", Severity.Noise, IssueTags.Fussy | tpTag);
                else if (kt < Data.Tracks.Items.Count)
                    Data.TpIssue = IssueModel.Add ("Test pass incomplete.", Severity.Error, IssueTags.Failure);
                else if (Data.Tracks.Items.All (it => it.TestCRC == it.CopyCRC))
                {
                    var sev = tpTag != IssueTags.None? Severity.Advisory : Severity.Trivia;
                    Data.TpIssue = IssueModel.Add ("Test/copy CRC-32s match for all tracks.", sev, IssueTags.Success);
                }

                int k1=0, k2=0, k3=0;
                int r1a=-1, r2a=-1, r3a=-1;
                int r1b=0, r2b=0, r3b=0;
                StringBuilder m1 = new StringBuilder(), m2 = new StringBuilder(), m3 = new StringBuilder();
                foreach (LogEacTrack tk in Data.Tracks.Items)
                {
                    if (! tk.HasOK)
                    {
                        if (r1a < 0) r1a = tk.Number;
                        r1b = tk.Number;
                        ++k1;
                    }
                    else if (r1a >= 0)
                    {
                        if (m1.Length != 0) m1.Append (",");
                        m1.Append (r1a);
                        if (r1b > r1a) { m1.Append ("-"); m1.Append (r1b); }
                        r1a = -1;
                    }
 
                    if (tk.HasOK && ! tk.HasQuality)
                    {
                        if (r2a < 0) r2a = tk.Number;
                        r2b = tk.Number;
                        ++k2;
                    }
                    else if (r2a >= 0)
                    {
                        if (m2.Length != 0) m2.Append (",");
                        m2.Append (r2a);
                        if (r2b > r2a) { m2.Append ("-"); m2.Append (r2b); }
                        r2a = -1;
                    }

                    if (tk.IsBadCRC)
                    {
                        if (r3a < 0) r3a = tk.Number;
                        r3b = tk.Number;
                        ++k3;
                    }
                    else if (r3a >= 0)
                    {
                        if (m3.Length != 0) m3.Append (",");
                        m3.Append (r3a);
                        if (r3b > r3a) { m3.Append ("-"); m3.Append (r3b); }
                        r3a = -1;
                    }
                }

                if (k1 == 0 && k2 == 0 && k3 == 0)
                    return;

                if (r1a >= 0)
                {
                    if (m1.Length != 0) m1.Append (",");
                    m1.Append (r1a);
                    if (r1b > r1a) { m1.Append ("-"); m1.Append (r1b); }
                }
                if (r2a >= 0)
                {
                    if (m2.Length != 0) m2.Append (",");
                    m2.Append (r2a);
                    if (r2b > r2a) { m2.Append ("-"); m2.Append (r2b); }
                }
                if (r3a >= 0)
                {
                    if (m3.Length != 0) m3.Append (",");
                    m3.Append (r3a);
                    if (r3b > r3a) { m3.Append ("-"); m3.Append (r3b); }
                }

                Issue i1=null, i2=null, i3=null;
                if (k1 != 0)
                {
                    m1.Append (" not OK.");
                    i1 = IssueModel.Add ((k1 == 1? "Track " : "Tracks ") + m1);
                }
                if (k2 != 0)
                {
                    m2.Append (" OK but missing quality indicator.");
                    i2 = IssueModel.Add ((k2 == 1? "Track " : "Tracks ") + m2);
                }
                if (k3 != 0)
                {
                    m3.Append (" test/copy CRCs mismatched.");
                    i3 = IssueModel.Add ((k3 == 1? "Track " : "Tracks ") + m3, Severity.Error, IssueTags.Failure);
                    Data.TpIssue = i3;
                }

                for (int trackIndex = 0; trackIndex < TracksModel.Bind.Items.Count; ++trackIndex)
                {
                    var tk = TracksModel.Bind.Items[trackIndex];

                    if (tk.RipSeverest == null || tk.RipSeverest.Level < Severity.Error)
                        if (! tk.HasOK)
                            TracksModel.SetSeverest (trackIndex, i1);
                        else if (! tk.HasQuality)
                            TracksModel.SetSeverest (trackIndex, i2);
                        else if (tk.IsBadCRC)
                            TracksModel.SetSeverest (trackIndex, i3);
                }
            }
        }

        private static readonly byte[] logEacSig0x = new byte[] { (byte)'E', (byte)'A', (byte)'C', (byte)' ' };
        private static readonly byte[] logEacSig0y = Encoding.ASCII.GetBytes ("Exact Audio Copy V0");
        private static readonly byte[] logEacSig1x = Encoding.Unicode.GetBytes ("\uFEFFExact Audio Copy V");

        public LogEacTrack.Vector Tracks { get; private set; }
        public LogEacTrack.Vector GuiTracks { get; private set; }
        public string EacVersionText { get; private set; }
        public string RipDate { get; private set; }
        public string Artist { get; private set; }
        public string Album { get; private set; }
        public string RipArtistAlbum { get { return Artist + " / " + Album; } }
        public string Drive { get; private set; }
        public string ReadOffset { get; private set; }
        public string Overread { get; private set; }
        public string Id3Tag { get; private set; }
        public string FillWithSilence { get; private set; }
        public string TrimSilence { get; private set; }
        public string SampleFormat { get; private set; }
        public string CalcWithNulls { get; private set; }
        public string Interface { get; private set; }
        public string GapHandling { get; private set; }
        public string NormalizeTo { get; private set; }
        public string Quality { get; private set; }
        public int? TocTrackCount { get; private set; }
        public bool IsRangeRip { get; private set; }
        public int? AccurateRip { get; private set; }
        public int? AccurateRipConfidence { get; private set; }
        public int? CueToolsConfidence { get; private set; }

        private string accurateStream;
        private string defeatCache;
        private string useC2;
        private string readMode;
        private string readModeLongLazy;

        public string AccurateStream
        {
            get { return accurateStream; }
            private set { accurateStream = value; readModeLongLazy = null; }
        }
        public string DefeatCache
        {
            get { return defeatCache; }
            private set { defeatCache = value; readModeLongLazy = null; }
        }
        public string UseC2
        {
            get { return useC2; }
            private set { useC2 = value; readModeLongLazy = null; }
        }
        public string ReadMode
        {
            get { return readMode; }
            private set { readMode = value; readModeLongLazy = null; }
        }

        public string EacVersionLong
        { get { return EacVersionText?? "unknown"; } }

        public string AccurateRipLong
        {
            get
            {
                if (AccurateRipConfidence == null) return "not attempted";
                if (AccurateRipConfidence.Value < 0) return "failed";
                if (AccurateRipConfidence.Value == 0) return "data not present";
                return "confidence " + AccurateRipConfidence.Value + " (v" + AccurateRip + ")";
            }
        }

        public string CueToolsLong
        {
            get
            {
                if (CueToolsConfidence == null) return "not attempted";
                if (CueToolsConfidence.Value < 0) return "failed";
                if (CueToolsConfidence.Value == 0) return "data not present";
                return "confidence " + CueToolsConfidence.Value;
            }
        }

        public string ReadModeLong
        {
            get
            {
                if (readModeLongLazy == null)
                    if (AccurateStream == null && DefeatCache == null && UseC2 == null)
                        readModeLongLazy = readMode;
                    else
                        readModeLongLazy = (readMode?? "?") + " ("
                            + "AccurateStream=" + (AccurateStream?? "?")
                            + ", DefeatCache=" + (DefeatCache?? "?")
                            + ", UseC2=" + (UseC2?? "?")
                            + ")";

                return readModeLongLazy;
            }
        }


        private byte[] storedHash;
        public string SelfHashLong
        { get { return storedHash==null? (EacVersionText==null || EacVersionText.StartsWith ("0")? "none" : "missing") : ((storedHash.Length * 8).ToString() + " bits"); } }

        public Issue DsIssue { get; private set; }
        public Issue NzIssue { get; private set; }
        public Issue ShIssue { get; private set; }
        public Issue ArIssue { get; private set; }
        public Issue CtIssue { get; private set; }
        public Issue GpIssue { get; private set; }
        public Issue TkIssue { get; private set; }
        public Issue TpIssue { get; private set; }
        public Issue TsIssue { get; private set; }


        // FLAC only:
        public string CalcedAlbumArtist { get; private set; }
        public string TaggedAlbumArtist { get; private set; }
        public string TaggedAlbum { get; private set; }
        public string TaggedDate { get; private set; }
        public string TaggedOrg { get; private set; }
        public string TaggedDisc { get; private set; }
        public string TaggedDiscTotal { get; private set; }
        public string TaggedReleaseDate { get; private set; }
        public string TaggedEdition { get; private set; }
        public string TaggedSubtitle { get; private set; }
        public string WorkName { get; private set; }

        public Encoding Codepage { get; private set; }


        private LogEacFormat (Stream fs, string path, LogEacTrack.Vector tracks) : base (fs, path)
        { this.Tracks = tracks; }


        public string GetCleanWorkName (NamingStrategy strategy)
        {
            string dirtyName;
            string date = TaggedDate?? "(NoDate)";
            bool isVarious = CalcedAlbumArtist == null || (CalcedAlbumArtist.ToLower()+' ').StartsWith("various ");

            switch (strategy)
            {
                case NamingStrategy.ArtistTitle:
                    dirtyName = (CalcedAlbumArtist?? "(Various)") + " - " + date + " - " + TaggedAlbum;
                    break;

                case NamingStrategy.ShortTitle:
                    if (isVarious)
                        dirtyName = "- " + TaggedAlbum + " - " + date;
                    else
                        dirtyName = CalcedAlbumArtist + " - " + date + " - " + TaggedAlbum;
                    break;

                case NamingStrategy.UnloadedAlbum:
                    var sb = new StringBuilder();
                    var albumArtist = CalcedAlbumArtist?? "(Various)";

                    if (! isVarious)
                    { sb.Append (albumArtist); sb.Append (' '); }

                    var relDate = TaggedReleaseDate?? date;
                    if (! isVarious)
                    { sb.Append ("- "); sb.Append (relDate); sb.Append (' '); }

                    sb.Append ("- ");
                    sb.Append (TaggedAlbum);
                    var punct = " [";
                    if (TaggedReleaseDate != null && TaggedDate != null)
                    { sb.Append (punct); sb.Append (TaggedDate); punct = " "; }
                    if (TaggedEdition != null)
                    { sb.Append (punct); sb.Append (TaggedEdition); punct = ", "; }
                    if (TaggedOrg != null)
                    {
                        if (TaggedOrg == "Mobile Fidelity Sound Lab")
                        { sb.Append (punct); sb.Append ("MFSL"); punct = ", "; }
                    }
                    if (punct != " [")
                        sb.Append (']');


                    if (TaggedDisc != null && (TaggedDisc != "1" || TaggedDiscTotal != "1"))
                    {
                        sb.Append (" (Disc " + TaggedDisc + ')');
                        if (TaggedSubtitle != null)
                            sb.Append (" (" + TaggedSubtitle + ')');
                    }

                    if (isVarious)
                    { sb.Append (" - "); sb.Append (relDate); }

                    dirtyName = sb.ToString();
                    break;

                default:
                    dirtyName = WorkName;
                    break;
            }

            return Map1252.ToClean1252FileName (dirtyName);
        }


        public override bool IsBadData
        { get { return ShIssue != null && ShIssue.Failure; } }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (scope <= Granularity.Detail && report.Count > 0)
                report.Add (String.Empty);

            report.Add ("EAC version = " + EacVersionLong);

            if (scope > Granularity.Detail)
                return;

            if (storedHash != null)
                report.Add ("EAC stored self-hash = " + ConvertTo.ToHexString (storedHash));

            report.Add ("AccurateRip = " + (AccurateRip == null? "(none)" : AccurateRip.Value.ToString()));
            report.Add ("CUETools confidence = " + (CueToolsConfidence == null? "(none)" : CueToolsConfidence.Value.ToString()));

            report.Add ("Rip album = " + RipArtistAlbum);
            report.Add ("Rip date = " + RipDate);
            if (CalcedAlbumArtist != null) report.Add ("Derived artist = " + CalcedAlbumArtist);

            report.Add ("Drive = " + Drive);
            report.Add ("Interface = " + Interface);
            report.Add ("Read mode = " + ReadMode);
            if (AccurateStream != null) report.Add ("  Accurate Stream = " + AccurateStream);
            if (DefeatCache != null) report.Add ("  Defeat cache = " + DefeatCache);
            if (UseC2 != null) report.Add ("  Use C2 info = " + UseC2);

            if (ReadOffset != null) report.Add ("Drive offset = " + ReadOffset);
            if (Overread != null) report.Add ("Overread = " + Overread);
            if (FillWithSilence != null) report.Add ("Fill with silence = " + FillWithSilence);
            if (TrimSilence != null) report.Add ("Trim silence = " + TrimSilence);
            if (CalcWithNulls != null) report.Add ("Use nulls in CRC = " + CalcWithNulls);
            if (Quality != null) report.Add ("Error recovery quality = " + Quality);
            if (NormalizeTo != null) report.Add ("Normalization = " + NormalizeTo);

            if (GapHandling != null) report.Add ("Gap handling = " + GapHandling);
            if (SampleFormat != null) report.Add ("Sample format = " + SampleFormat);

            report.Add ("Track count (ToC) = " + (TocTrackCount==null? "(none)" : TocTrackCount.ToString()));
            report.Add ("Track count (rip) = " + Tracks.Items.Count);
            if (IsRangeRip)
                report.Add ("Range rip = true");
            else if (scope <= Granularity.Detail)
            {
                var sb = new StringBuilder();
                report.Add (String.Empty);
                report.Add ("Tracks:");
                foreach (var tk in Tracks.Items)
                {
                    sb.Clear();
                    sb.AppendFormat ("{0,3}", tk.Number);
                    sb.Append (": ");
                    sb.Append (tk.FilePath);
                    if (! String.IsNullOrEmpty (tk.Qual))
                    { sb.Append (" | "); sb.Append (tk.Qual); }
                    if (tk.CopyCRC != null)
                        sb.AppendFormat (" | {0:X8}", tk.CopyCRC);
                    if (! tk.HasOK)
                        sb.Append (" *BAD*");
                    report.Add (sb.ToString());
                }
            }
        }
    }
}

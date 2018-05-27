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
            public readonly LogEacFormat Bind;
            public LogEacTrack.Vector.Model TracksModel;
            private LogBuffer parser;

            public Model (Stream stream, byte[] hdr, string path)
            {
                TracksModel = new LogEacTrack.Vector.Model();
                BaseBind = this.Bind = new LogEacFormat (stream, path, TracksModel.Bind);
                Bind.Issues = IssueModel.Bind;

                Bind.AccurateRip = null;
                Bind.RipDate = String.Empty;

                // Arbitrary limit.
                if (Bind.FileSize > 250000)
                {
                    IssueModel.Add ("File insanely huge.", Severity.Fatal);
                    return;
                }

                Bind.fBuf = new byte[Bind.FileSize];
                Bind.fbs.Position = 0;
                var got = Bind.fbs.Read (Bind.fBuf, 0, (int) Bind.FileSize);
                if (got != Bind.FileSize)
                {
                    IssueModel.Add ("Read failed", Severity.Fatal);
                    return;
                }

                if (got < 2 || Bind.fBuf[0] != 0xFF || Bind.fBuf[1] != 0xFE)
                    Bind.Codepage = Encoding.GetEncoding (1252);
                else
                    Bind.Codepage = Encoding.Unicode;

                parser = new LogBuffer (Bind.fBuf, Bind.Codepage);
                string lx = ParseHeader();

                if (! Bind.IsRangeRip)
                {
                    lx = ParseTracks (lx);
                    if (Bind.Issues.HasFatal)
                        return;
                }

                while (! parser.EOF && ! lx.Contains ("errors") && ! lx.StartsWith ("==== "))
                    lx = parser.ReadLineLTrim();

                if (lx == "No errors occured" || lx == "No errors occurred")
                    lx = parser.ReadLineLTrim();
                else if (lx == "There were errors")
                {
                    if (Bind.Issues.MaxSeverity < Severity.Error)
                        IssueModel.Add ("There were errors.");
                    lx = parser.ReadLineLTrim();
                }
                else
                    IssueModel.Add ("Missing 'errors' line.");

                if (Bind.IsRangeRip)
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

                                if (Bind.AccurateRipConfidence == null || Bind.AccurateRipConfidence.Value > arConfidence)
                                    Bind.AccurateRipConfidence = arConfidence;
                                if (Bind.AccurateRip == null || Bind.AccurateRip.Value > arVersion)
                                    Bind.AccurateRip = arVersion;
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
                    Bind.storedHash = ConvertTo.FromHexStringToBytes (lx, 18, 32);

                    lx = parser.ReadLine();
                    if (! parser.EOF || ! String.IsNullOrEmpty (lx))
                        IssueModel.Add ("Unexpected content at end of file.", Severity.Warning, IssueTags.ProveErr);
                }

                parser = null;
                GetDiagnostics();
            }


            public Issue TkIssue { get { return Bind.TkIssue; } set { Bind.TkIssue = value; } }

            public void SetGuiTracks()
            { Bind.GuiTracks = Bind.Tracks; }


            public void SetWorkName (string workName)
            { Bind.WorkName = workName; }

            public void MatchFlacs (IList<FlacFormat.Model> flacMods)
            {
                int expectNum = -1,
                    mx = Math.Min (Bind.Tracks.Items.Count, flacMods.Count);

                for (int ix = 0; ix < mx; ++ix)
                {
                    LogEacTrack track = Bind.Tracks.Items[ix];
                    FlacFormat.Model flacMod = flacMods[ix];
                    FlacFormat flac = flacMod.Bind;

                    if (flac.ActualPcmCRC32.Value != track.CopyCRC.Value)
                        Bind.TkIssue = IssueModel.Add ("Audio CRC-32 mismatch on '" + flac.Name + "'.", Severity.Fatal, IssueTags.Failure);
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
                if (flacMods.Count != Bind.Tracks.Items.Count)
                    Bind.TkIssue = IssueModel.Add ("Rip not complete.", Severity.Fatal, IssueTags.Failure);
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
                var vendor = Bind.Tracks.Items[0].Match.Blocks.Tags.Vendor;
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

                bool? isSameDate = Bind.Tracks.IsFlacTagsAllSame ("DATE");
                if (isSameDate == false)
                    IssueModel.Add ("Inconsistent DATE tag.");
                else if (isSameDate == null)
                    IssueModel.Add ("Missing DATE tag.", Severity.Warning, IssueTags.BadTag);
                else
                {
                    Bind.TaggedDate = Bind.Tracks.Items[0].Match.GetTag ("DATE");
                    var err = CheckDate (Bind.TaggedDate);
                    if (err != null)
                        IssueModel.Add ("DATE " + err, Severity.Warning, IssueTags.BadTag);
                }

                bool? isSameReleaseDate = Bind.Tracks.IsFlacTagsAllSame ("RELEASE DATE");
                if (isSameReleaseDate == false)
                    IssueModel.Add ("Inconsistent RELEASE DATE tag.");
                else if (isSameReleaseDate == true)
                {
                    Bind.TaggedReleaseDate = Bind.Tracks.Items[0].Match.GetTag ("RELEASE DATE");
                    var err = CheckDate (Bind.TaggedReleaseDate);
                    if (err != null)
                        IssueModel.Add ("RELEASE DATE " + err, Severity.Warning, IssueTags.BadTag);

                    if (isSameDate == null)
                        IssueModel.Add ("RELEASE DATE without DATE tag.");
                }

                bool? sameAlbum = Bind.Tracks.IsFlacTagsAllSame ("ALBUM");
                if (sameAlbum != true)
                    IssueModel.Add ("Missing or inconsistent ALBUM tag.");
                else
                {
                    Bind.TaggedAlbum = Bind.Tracks.Items[0].Match.GetTag ("ALBUM");
                    CheckWhite ("ALBUM", Bind.TaggedAlbum);
                }

                bool? isSameAlbumArtist = Bind.Tracks.IsFlacTagsAllSame ("ALBUMARTIST");
                if (isSameAlbumArtist == true)
                {
                    Bind.TaggedAlbumArtist = Bind.Tracks.Items[0].Match.GetTag ("ALBUMARTIST");
                    CheckWhite ("ALBUMARTIST", Bind.TaggedAlbumArtist);
                    Bind.CalcedAlbumArtist = Bind.TaggedAlbumArtist;
                }
                else if (isSameAlbumArtist == false)
                    IssueModel.Add ("Inconsistent ALBUMARTIST tag.");

                bool? isSameArtist = Bind.Tracks.IsFlacTagsAllSame ("ARTIST");
                if (isSameArtist == true)
                    CheckWhite ("ARTIST", Bind.Tracks.Items[0].Match.GetTag ("ARTIST"));

                if (isSameAlbumArtist == null)
                    if (isSameArtist == false)
                        IssueModel.Add ("Inconsistent ARTIST or missing ALBUMARTIST tag.", Severity.Warning, IssueTags.BadTag);
                    else if (isSameArtist == null)
                        IssueModel.Add ("Missing ARTIST tag.", Severity.Warning, IssueTags.Substandard);
                    else
                        Bind.CalcedAlbumArtist = Bind.Tracks.Items[0].Match.GetTag ("ARTIST");

                bool? isSameBarcode = Bind.Tracks.IsFlacTagsAllSame ("BARCODE");
                if (isSameBarcode == false)
                    IssueModel.Add ("Inconsistent BARCODE tag.");

                bool? isSameCompilation = Bind.Tracks.IsFlacTagsAllSame ("COMPILATION");
                if (isSameCompilation == false)
                    IssueModel.Add ("Inconsistent COMPILATION tag.");

                foreach (var item in Bind.Tracks.Items)
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

                bool? isSameTrackTotal = Bind.Tracks.IsFlacTagsAllSame ("TRACKTOTAL");
                if (isSameTrackTotal == false)
                    IssueModel.Add ("Inconsistent TRACKTOTAL tag.");
                else if (isSameTrackTotal == true)
                {
                    string ttt = Bind.Tracks.Items[0].Match.GetTag ("TRACKTOTAL");
                    bool isOK = int.TryParse (ttt, out int ttn);
                    if (! isOK)
                        IssueModel.Add ("Malformed TRACKTOTAL tag '" + ttt + "'.");

                    if (ttn != Bind.Tracks.Items.Count)
                        IssueModel.Add ("Wrong TRACKTOTAL tag. Expecting " + Bind.Tracks.Items.Count + ", got " + ttn + ".");
                }

                bool? isSameDisc = Bind.Tracks.IsFlacTagsAllSame ("DISCNUMBER");
                if (isSameDisc == true)
                    Bind.TaggedDisc = Bind.Tracks.Items[0].Match.GetTag ("DISCNUMBER");
                else if (isSameDisc == false)
                    IssueModel.Add ("Inconsistent DISCNUMBER tag.");

                bool? isSameDiscTotal = Bind.Tracks.IsFlacTagsAllSame ("DISCTOTAL");
                if (isSameDiscTotal == true)
                    Bind.TaggedDiscTotal = Bind.Tracks.Items[0].Match.GetTag ("DISCTOTAL");
                else if (isSameDiscTotal == false)
                    IssueModel.Add ("Inconsistent DISCTOTAL tag.");

                bool? isSameOrg = Bind.Tracks.IsFlacTagsAllSame ("ORGANIZATION");
                if (isSameOrg == true)
                    Bind.TaggedOrg = Bind.Tracks.Items[0].Match.GetTag ("ORGANIZATION");
                else if (isSameOrg == false)
                    IssueModel.Add ("Inconsistent ORGANIZATION tag.");

                bool? isSameCat = Bind.Tracks.IsFlacTagsAllSame ("CATALOGNUMBER");
                if (isSameCat == false)
                    IssueModel.Add ("Inconsistent CATALOGNUMBER tag.");

                bool? isSameEdition = Bind.Tracks.IsFlacTagsAllSame ("EDITION");
                if (isSameEdition == true)
                    Bind.TaggedEdition = Bind.Tracks.Items[0].Match.GetTag ("EDITION");

                bool? isSameSubtitle = Bind.Tracks.IsFlacTagsAllSame ("SUBTITLE");
                if (isSameSubtitle == true)
                    Bind.TaggedSubtitle = Bind.Tracks.Items[0].Match.GetTag ("SUBTITLE");

                bool? isSameAASO = Bind.Tracks.IsFlacTagsAllSameMulti ("ALBUMARTISTSORTORDER");
                if (isSameAASO == false)
                    IssueModel.Add ("Inconsistent ALBUMARTISTSORTORDER tag.");

                if (Bind.Tracks.AnyHas ("ALBUM ARTIST"))
                        IssueModel.Add ("Use of ALBUM ARTIST tag not preferred, use ALBUMARTIST instead.", Severity.Warning, IssueTags.BadTag);

                if (Bind.Tracks.AnyHas ("ALBUMARTISTSORT"))
                        IssueModel.Add ("Use of ALBUMARTISTSORT tag not preferred, use ALBUMARTISTSORTORDER instead.", Severity.Warning, IssueTags.BadTag);

                if (Bind.Tracks.AnyHas ("PUBLISHER"))
                        IssueModel.Add ("Use of PUBLISHER tag not preferred, use ORGANIZATION instead.", Severity.Warning, IssueTags.BadTag);

                if (Bind.Tracks.AnyHas ("TOTALTRACKS"))
                        IssueModel.Add ("Use of TOTALTRACKS tag deprecated, use TRACKTOTAL instead.", Severity.Warning, IssueTags.BadTag);

                if (Bind.Tracks.AnyHas ("TOTALDISCS"))
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
                if (Bind.storedHash == null)
                {
                    Severity sev = Bind.EacVersionText != null && Bind.EacVersionText.StartsWith ("1")? Severity.Warning : Severity.Noise;
                    Bind.ShIssue = IssueModel.Add ("EAC log self-hash not present.", sev, IssueTags.ProveErr|IssueTags.Fussy);
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
                            qs.Write (Bind.fBuf, 0, Bind.fBuf.Length);
                            qs.Write (tlrBuf, 0, tlrBuf.Length);
                        }

                        using (WebResponse res = req.GetResponse())
                            using (Stream ps = res.GetResponseStream())
                                using (StreamReader rdr = new StreamReader (ps))
                                {
                                    string answer = rdr.ReadLine();
                                    if (answer.Contains ("is fine"))
                                        Bind.ShIssue = IssueModel.Add ("EAC log self-hash verify successful.", Severity.Trivia, IssueTags.Success);
                                    else if (answer.Contains ("incorrect"))
                                        Bind.ShIssue = IssueModel.Add ("EAC log self-hash mismatch, file has been modified.", Severity.Error, IssueTags.Failure);
                                    else
                                        Bind.ShIssue = IssueModel.Add ("EAC log self-hash verify attempt returned unknown result.", Severity.Advisory, IssueTags.ProveErr);
                                }
                    }
                    catch (Exception ex)
                    { Bind.ShIssue = IssueModel.Add ("EAC log self-hash verify attempt failed: " + ex.Message.Trim (null), Severity.Warning, IssueTags.ProveErr); }
                }
            }


            private void GetDiagnostics()
            {
                if (String.IsNullOrEmpty (Bind.Artist))
                    IssueModel.Add ("Missing artist", Severity.Warning, IssueTags.Substandard);

                if (String.IsNullOrEmpty (Bind.Album))
                    IssueModel.Add ("Missing album", Severity.Warning, IssueTags.Substandard);

                if (String.IsNullOrEmpty (Bind.Drive))
                    IssueModel.Add ("Missing 'Used drive'.");

                if (String.IsNullOrEmpty (Bind.ReadMode))
                    IssueModel.Add ("Missing 'Read mode'.");
                else if (Bind.ReadMode != "Secure with NO C2, accurate stream, disable cache"
                      && Bind.ReadMode != "Secure with NO C2, accurate stream,  disable cache")
                {
                    if (Bind.ReadMode != "Secure")
                        Bind.DsIssue = IssueModel.Add ("Nonpreferred drive setting: Read mode: " + Bind.ReadMode, Severity.Warning, IssueTags.Substandard);

                    if (Bind.AccurateStream == null || Bind.AccurateStream != "Yes")
                        Bind.DsIssue = IssueModel.Add ("Missing drive setting: 'Utilize accurate stream: Yes'." + Bind.AccurateStream, Severity.Warning, IssueTags.Substandard);

                    if (Bind.DefeatCache == null || Bind.DefeatCache != "Yes")
                        Bind.DsIssue = IssueModel.Add ("Missing drive setting: 'Defeat audio cache: Yes'.", Severity.Warning, IssueTags.Substandard);

                    if (Bind.UseC2 == null || Bind.UseC2 != "No")
                        Bind.DsIssue = IssueModel.Add ("Missing drive setting: 'Make use of C2 pointers: No'.", Severity.Warning, IssueTags.Substandard);
                }

                if (String.IsNullOrEmpty (Bind.ReadOffset))
                    IssueModel.Add ("Missing 'Read offset correction'.", Severity.Trivia, IssueTags.Substandard);

                if (Bind.FillWithSilence != null && Bind.FillWithSilence != "Yes")
                    IssueModel.Add ("Missing 'Fill up missing offset samples with silence: Yes'.", Severity.Trivia, IssueTags.ProveWarn);

                if (Bind.Quality != null && Bind.Quality != "High")
                    IssueModel.Add ("Missing 'Quality: High'.", Severity.Advisory, IssueTags.Substandard);

                if (Bind.TrimSilence == null || Bind.TrimSilence != "No")
                    Bind.TsIssue = IssueModel.Add ("Missing 'Delete leading and trailing silent blocks: No'.", Severity.Warning, IssueTags.Substandard);

                if (Bind.CalcWithNulls != null && Bind.CalcWithNulls != "Yes")
                    IssueModel.Add ("Missing 'Null samples used in CRC calculations: Yes'.");

                if (Bind.GapHandling != null)
                    if (Bind.GapHandling != "Appended to previous track")
                    {
                        IssueTags gapTag = IssueTags.Fussy;
                        if (Bind.GapHandling != "Not detected, thus appended to previous track")
                            gapTag |= IssueTags.Substandard;

                        Bind.GpIssue = IssueModel.Add ("Gap handling preferred setting is 'Appended to previous track'.", Severity.Advisory, gapTag);
                    }

                if (Bind.Id3Tag == "Yes")
                    IssueModel.Add ("Append ID3 tags preferred setting is 'No'.", Severity.Trivia, IssueTags.Fussy);

                if (Bind.ReadOffset == "0" && Bind.Drive.Contains ("not found in database"))
                    IssueModel.Add ("Unknown drive with offset '0'.", Severity.Advisory, IssueTags.Fussy);

                if (Bind.NormalizeTo != null)
                    Bind.NzIssue = IssueModel.Add ("Use of normalization considered harmful.", Severity.Warning, IssueTags.Substandard);

                if (Bind.SampleFormat != null && Bind.SampleFormat != "44.100 Hz; 16 Bit; Stereo")
                    IssueModel.Add ("Missing 'Sample format: 44.100 Hz; 16 Bit; Stereo'.", Severity.Warning, IssueTags.Substandard);

                if (Bind.IsRangeRip)
                    IssueModel.Add ("Range rip detected.", Severity.Advisory, IssueTags.Substandard);
                else
                {
                    if (! Bind.Tracks.IsNearlyAllPresent())
                        Bind.TkIssue = IssueModel.Add ("Gap detected in track numbers.");

                    if (Bind.TocTrackCount != null)
                    {
                        int diff = Bind.TocTrackCount.Value - Bind.Tracks.Items.Count;
                        if (diff != 0)
                        {
                            Severity sev = diff == 1? Severity.Advisory : Severity.Error;
                            IssueModel.Add ("Found " + Bind.Tracks.Items.Count + " of " + Bind.TocTrackCount.Value + " tracks.", sev);
                        }
                    }
                }

                var tpTag = IssueTags.ProveErr;
                var arTag = IssueTags.None;
                var arSev = Severity.Trivia;
                if (Bind.AccurateRipConfidence != null)
                    if (Bind.AccurateRipConfidence.Value > 0)
                    {
                        tpTag = IssueTags.None;
                        arTag = IssueTags.Success;
                    }
                    else
                    {
                        arSev = Severity.Advisory;
                        if (Bind.AccurateRipConfidence.Value < 0)
                            arTag = IssueTags.Failure;
                    }
                Bind.ArIssue = IssueModel.Add ("AccurateRip verification " + Bind.AccurateRipLong + ".", arSev, arTag);

                var ctSev = Severity.Trivia;
                var ctTag = IssueTags.None;
                if (Bind.CueToolsConfidence == null)
                    ctTag = IssueTags.ProveErr;
                else if (Bind.CueToolsConfidence.Value < 0)
                    ctSev = Severity.Error;
                else if (Bind.CueToolsConfidence.Value == 0)
                    ctSev = Severity.Advisory;
                else
                {
                    ctTag = IssueTags.Success;
                    tpTag = IssueTags.None;
                }

                Bind.CtIssue = IssueModel.Add ("CUETools DB verification " + Bind.CueToolsLong + ".", ctSev, ctTag);

                var kt = Bind.Tracks.Items.Where (it => it.TestCRC != null).Count();
                if (kt == 0)
                    Bind.TpIssue = IssueModel.Add ("Test pass not performed.", Severity.Noise, IssueTags.Fussy | tpTag);
                else if (kt < Bind.Tracks.Items.Count)
                    Bind.TpIssue = IssueModel.Add ("Test pass incomplete.", Severity.Error, IssueTags.Failure);
                else if (Bind.Tracks.Items.All (it => it.TestCRC == it.CopyCRC))
                {
                    var sev = tpTag != IssueTags.None? Severity.Advisory : Severity.Trivia;
                    Bind.TpIssue = IssueModel.Add ("Test/copy CRC-32s match for all tracks.", sev, IssueTags.Success);
                }

                int k1=0, k2=0, k3=0;
                int r1a=-1, r2a=-1, r3a=-1;
                int r1b=0, r2b=0, r3b=0;
                StringBuilder m1 = new StringBuilder(), m2 = new StringBuilder(), m3 = new StringBuilder();
                foreach (LogEacTrack tk in Bind.Tracks.Items)
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
                    Bind.TpIssue = i3;
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

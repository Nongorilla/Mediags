using System;
using System.Linq;
using System.Globalization;
using System.Text;
using NongIssue;

namespace NongFormat
{
    /// <summary>
    /// Provides primitive text parsing for a cached text file.
    /// </summary>
    public class LogBuffer
    {
        private readonly byte[] buf;
        private int bufPos;
        private int linePos;
        private Encoding encoding;
        public static readonly Encoding cp1252 = Encoding.GetEncoding (1252);

        public int FilePosition
        { get { return bufPos; } }

        public int LinePosition
        { get { return linePos; } }

        public int LineNum
        { get; private set; }

        public bool EOF
        { get { return bufPos >= buf.Length; } }

        public string GetPlace()
        { return "line " + LineNum.ToString(); }

        public LogBuffer (byte[] data, Encoding encoding)
        {
            this.buf = data;
            this.encoding = encoding;
            this.bufPos = encoding == Encoding.Unicode ? 2 : 0;
        }


        // Get next non-blank line, left trimmed.
        public string ReadLineLTrim()
        {
            if (EOF)
                return String.Empty;

            for (;;)
            {
                string result = ReadLine();
                if (EOF || ! String.IsNullOrWhiteSpace (result))
                    return result.TrimStart();
            }
        }


        // Get next non-blank line.
        public string ReadLine()
        {
            int eolWidth = 0;
            for (linePos = bufPos; bufPos < buf.Length; ++bufPos)
                if (buf[bufPos]==0x0A)
                { eolWidth = 1; ++bufPos; break; }
                else if (buf[bufPos]==0x0D)
                    if (encoding == Encoding.Unicode)
                    {
                        if (bufPos < buf.Length-3 && buf[bufPos+1]==0 && buf[bufPos+2]==0x0A && buf[bufPos+3]==0)
                        { eolWidth = 4; bufPos += 4; break; }
                    }
                    else if (bufPos < buf.Length-1 && buf[bufPos+1]==0x0A)
                    { eolWidth = 2; bufPos += 2; break; }

            ++LineNum;
            string result = encoding.GetString (buf, linePos, bufPos-linePos-eolWidth);
            return result;
        }
    }


    public partial class LogEacFormat : FormatBase
    {
        public partial class Model : FormatBase.ModelBase
        {
            public static bool ToInt (string source, int offset, out int result)
            {
                while (offset < source.Length && Char.IsWhiteSpace (source[offset]))
                    ++offset;
                int stop=offset;
                while (stop < source.Length && Char.IsDigit (source[stop]))
                    ++stop;
                return int.TryParse (source.Substring (offset, stop-offset), out result);
            }


            private string ParseHeader()
            {
                string lx = parser.ReadLine();
                if (lx.StartsWith ("Exact Audio Copy V"))
                {
                    var spacePos = lx.IndexOf (' ', 18);
                    if (spacePos > 0)
                        Data.EacVersionText = lx.Substring (18, spacePos-18);

                    lx = parser.ReadLine();
                    lx = parser.ReadLine();
                }

                if (! lx.StartsWith ("EAC extraction logfile from "))
                {
                    IssueModel.Add ("Unexpected contents, " + parser.GetPlace() + ": Expecting 'EAC extraction logfile'.");
                    return lx;
                }

                lx = lx.Substring (28);
                if (lx.Length < 12)
                {
                    IssueModel.Add ("Missing rip date, " + parser.GetPlace() + ".");
                    return lx;
                }
                Data.RipDate = lx.EndsWith (" for CD")? lx.Substring (0, lx.Length-7) : lx;

                lx = parser.ReadLine();
                if (String.IsNullOrWhiteSpace (lx))
                    lx = parser.ReadLine();

                int slashPos = lx.IndexOf ('/');
                if (slashPos < 0)
                {
                    IssueModel.Add ("Missing '<artist> / <album>', " + parser.GetPlace() + ".");
                    return lx;
                }
                Data.Artist = lx.Substring (0, slashPos).Trim();
                Data.Album = lx.Substring (slashPos + 1).Trim();

                for (;;)
                {
                    TOP:
                    if (parser.EOF)
                        return String.Empty;
                    lx = parser.ReadLine();
                    if (lx.StartsWith ("Track ") || lx.StartsWith ("===="))
                        return lx;

                    if (lx == "Range status and errors")
                    {
                        Data.IsRangeRip = true;
                        return parser.ReadLine();
                    }

                    if (lx == "TOC of the extracted CD")
                        for (Data.TocTrackCount = 0;;)
                        {
                            lx = parser.ReadLine();
                            if (parser.EOF || lx.StartsWith ("==== ") || (lx.StartsWith ("Track") && ! lx.Contains ('|')))
                                return lx;

                            if (lx == "Range status and errors")
                            {
                                Data.IsRangeRip = true;
                                return parser.ReadLine();
                            }

                            if (lx.Length >= 60)
                            {
                                if (int.TryParse (lx.Substring (0, 9), out int tn))
                                    ++Data.TocTrackCount;
                            }
                        }

                    int ik0 = 0, ik1=0,  ii=0, iv0=0, iv1;
                    for (;; ++ik0)
                        if (ik0 >= lx.Length) goto TOP;
                        else if (lx[ik0] != ' ')
                        {
                            for (ii = ik0, ik1 = ii;;)
                            {
                                ++ii;
                                if (ii >= lx.Length)
                                {
                                    string kk = lx.Substring (ik0, ik1-ik0+1);
                                    if (kk == "Installed external ASPI interface" || kk == "Native Win32 interface for Win NT & 2000")
                                        Data.Interface = kk;
                                    goto TOP;
                                }
                                if (lx[ii] == ':') break;
                                if (lx[ii] != ' ') ik1 = ii;
                            }

                            for (iv0 = ii+1;; ++iv0)
                                if (iv0 >= lx.Length) goto TOP;
                                else if (lx[iv0] != ' ')
                                {
                                    for (ii=iv0+1, iv1=iv0;; ++ii)
                                        if (ii == lx.Length) break;
                                        else if (lx[ii] != ' ') iv1=ii;

                                    string optKey = lx.Substring (ik0, ik1-ik0+1),
                                           optVal = lx.Substring (iv0, iv1-iv0+1);
                                    if (optKey == "Used drive")
                                        Data.Drive = optVal;
                                    else if (optKey == "Utilize accurate stream")
                                        Data.AccurateStream = optVal;
                                    else if (optKey == "Defeat audio cache")
                                        Data.DefeatCache = optVal;
                                    else if (optKey == "Make use of C2 pointers")
                                        Data.UseC2 = optVal;
                                    else if (optKey == "Read mode")
                                        Data.ReadMode = optVal;
                                    else if (optKey == "Read offset correction" || optKey == "Combined read/write offset correction")
                                        Data.ReadOffset = optVal;
                                    else if (optKey == "Overread into Lead-In and Lead-Out")
                                        Data.Overread = optVal;
                                    else if (optKey == "Fill up missing offset samples with silence")
                                        Data.FillWithSilence = optVal;
                                    else if (optKey == "Delete leading and trailing silent blocks")
                                        Data.TrimSilence = optVal;
                                    else if (optKey == "Null samples used in CRC calculations")
                                        Data.CalcWithNulls = optVal;
                                    else if (optKey == "Normalize to")
                                        Data.NormalizeTo = optVal;
                                    else if (optKey == "Used interface")
                                        Data.Interface = optVal;
                                    else if (optKey == "Gap handling")
                                        Data.GapHandling = optVal;
                                    else if (optKey == "Sample format")
                                        Data.SampleFormat = optVal;
                                    else if (optKey == "Quality")
                                        Data.Quality = optVal;
                                    else if (optKey == "Add ID3 tag")
                                        Data.Id3Tag = optVal;
                                    break;
                                }
                        }
                }
            }


            private string ParseTracks (string lx)
            {
                for (;;)
                {
                    if (parser.EOF
                            || lx == "No errors occured"
                            || lx == "No errors occurred"
                            || lx == "There were errors"
                            || lx.Contains ("accurate"))
                        break;

                    if (! lx.StartsWith ("Track") || lx.StartsWith ("Track quality"))
                    {
                        lx = parser.ReadLineLTrim();
                        continue;
                    }

                    bool success = Int32.TryParse (lx.Substring (6), out int num);
                    if (! success)
                    {
                        Data.TkIssue = IssueModel.Add ("Invalid track " + parser.GetPlace(), Severity.Fatal, IssueTags.Failure);
                        break;
                    }

                    lx = parser.ReadLineLTrim();
                    if (! lx.StartsWith ("Filename "))
                        Data.TkIssue = IssueModel.Add ("Track " + num + ": Missing 'Filename'.", Severity.Error, IssueTags.Failure);
                    else
                        lx = ParseTrack (lx, num);
                }
                return lx;
            }


            private string ParseTrack (string lx, int num)
            {
                string name="", peak="", speed="", pregap="", qual="";
                int? arVersion=null;
                int? arConfidence=null;
                uint? testCrc=null, copyCrc=null;
                bool trackErr = false;
                uint word;

                name = lx.Substring (9);
                if (name.Length < 10 || (lx.Length < 252 && ! lx.EndsWith (".wav")))
                    IssueModel.Add ("Unexpected extension " + parser.GetPlace());

                lx = parser.ReadLineLTrim();
                if (lx.StartsWith ("Pre-gap length  "))
                { pregap = lx.Substring (16); lx = parser.ReadLineLTrim(); }

                for (;;)
                {
                    if (parser.EOF)
                        return lx;

                    if (lx.StartsWith ("Track quality") || lx.StartsWith ("Peak level "))
                        break;

                    if (lx.StartsWith ("Track "))
                        return lx;  // Unexpected start of next track.

                    if (! trackErr)
                    {
                        trackErr = true;
                        IssueModel.Add ("Track " + num + ": '" + lx + "'.");
                    }
                    lx = parser.ReadLineLTrim();
                }

                if (lx.StartsWith ("Peak level "))
                { peak = lx.Substring (11); lx = parser.ReadLineLTrim(); }

                if (lx.StartsWith ("Extraction speed "))
                { speed = lx.Substring (17); lx = parser.ReadLineLTrim(); }

                if (lx.StartsWith ("Track quality "))
                { qual = lx.Substring (14); lx = parser.ReadLineLTrim(); }

                if (lx.StartsWith ("Test CRC "))
                {
                    if (uint.TryParse (lx.Substring (9), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out word))
                        testCrc = word;
                    else
                        Data.TpIssue = IssueModel.Add ("Track " + num + ": Invalid test CRC-32.", Severity.Error, IssueTags.Failure);
                    lx = parser.ReadLineLTrim();
                }

                if (! lx.StartsWith ("Copy CRC "))
                    IssueModel.Add ("Track " + num + ": Missing copy CRC-32.", Severity.Warning);
                else
                {
                    if (uint.TryParse (lx.Substring (9), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out word))
                        copyCrc = word;
                    else
                        Data.TkIssue = IssueModel.Add ("Track " + num + ": Invalid copy CRC-32.", Severity.Error, IssueTags.Failure);
                    lx = parser.ReadLineLTrim();
                }

                if (lx.StartsWith ("Accurately ripped (confidence "))
                {
                    arVersion = lx.Contains("AR v2")? 2 : 1;
                    bool isOk = ToInt (lx, 30, out int val);
                    arConfidence = isOk && val > 0? val : -1;
                    lx = parser.ReadLineLTrim();
                }
                else if (lx.StartsWith ("Cannot be verified"))
                {
                    arVersion = lx.Contains("AR v2")? 2 : 1;
                    arConfidence = -1;
                    lx = parser.ReadLineLTrim();
                }
                else if (lx.StartsWith ("Track not present"))
                {
                    if (peak != "0.0 %")
                        arConfidence = 0;
                    lx = parser.ReadLineLTrim();
                }

                if (arConfidence != null)
                {
                    if (Data.AccurateRipConfidence == null || Data.AccurateRipConfidence > arConfidence)
                        Data.AccurateRipConfidence = arConfidence;
                    if (arVersion != null)
                        if (Data.AccurateRip == null || Data.AccurateRip.Value > arVersion.Value)
                            Data.AccurateRip = arVersion;
                }

                bool hasOK = false;
                if (lx == "Copy OK")
                {
                    hasOK = true;
                    lx = parser.ReadLineLTrim();
                }

                TracksModel.Add (num, name, pregap, peak, speed, qual, testCrc, copyCrc, hasOK, arVersion, arConfidence);
                return lx;
            }


            private string ParseCueTools (string lx)
            {
                do
                {
                    lx = parser.ReadLineLTrim();
                    if ( parser.EOF || lx.StartsWith ("==== "))
                        return lx;
                } while (! lx.StartsWith ("[CTDB"));

                if (lx.Contains ("not present"))
                {
                    Data.CueToolsConfidence = 0;
                    lx = parser.ReadLineLTrim();
                    if (! parser.EOF && lx.StartsWith ("Submit"))
                        lx = parser.ReadLineLTrim();
                    return lx;
                }

                if (! lx.Contains (" found"))
                    return lx;

                do
                {
                    lx = parser.ReadLineLTrim();
                    if (parser.EOF || lx.StartsWith ("==== "))
                        return lx;

                    if (lx.StartsWith ("["))
                    {
                        int ctConfidence = -1;
                        if (lx.Contains ("Accurately ripped"))
                        {
                            bool isOK = ToInt (lx, 12, out ctConfidence);
                        }
                        Data.CueToolsConfidence = ctConfidence;
                        lx = parser.ReadLineLTrim();
                        return lx;
                    }
                } while (! lx.StartsWith ("Track"));

                for (int tx = 0; ; ++tx)
                {
                    lx = parser.ReadLine();
                    if (parser.EOF || lx.StartsWith ("==== ") || lx.StartsWith ("All tracks"))
                        return lx;

                    int ctConfidence;
                    if (lx.Contains ("Accurately ripped"))
                    {
                        bool isOK = ToInt (lx, 9, out ctConfidence);
                        if (! isOK)
                        { Data.CueToolsConfidence = -1; return lx; }
                    }
                    else if (lx.Contains ("Differs"))
                        ctConfidence = -1;
                    else
                        break;

                    if (tx < TracksModel.Bind.Items.Count)
                        TracksModel.SetCtConfidence (tx, ctConfidence);
                    if (Data.CueToolsConfidence == null || Data.CueToolsConfidence > ctConfidence)
                        Data.CueToolsConfidence = ctConfidence;
                }
                return parser.ReadLineLTrim();
            }
        }
    }
}

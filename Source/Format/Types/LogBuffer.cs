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
                        Bind.EacVersionText = lx.Substring (18, spacePos-18);

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
                Bind.RipDate = lx.EndsWith (" for CD")? lx.Substring (0, lx.Length-7) : lx;

                lx = parser.ReadLine();
                if (String.IsNullOrWhiteSpace (lx))
                    lx = parser.ReadLine();

                int slashPos = lx.IndexOf ('/');
                if (slashPos < 0)
                {
                    IssueModel.Add ("Missing '<artist> / <album>', " + parser.GetPlace() + ".");
                    return lx;
                }
                Bind.Artist = lx.Substring (0, slashPos).Trim();
                Bind.Album = lx.Substring (slashPos + 1).Trim();

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
                        Bind.IsRangeRip = true;
                        return parser.ReadLine();
                    }

                    if (lx == "TOC of the extracted CD")
                        for (Bind.TocTrackCount = 0;;)
                        {
                            lx = parser.ReadLine();
                            if (parser.EOF || lx.StartsWith ("==== ") || (lx.StartsWith ("Track") && ! lx.Contains ('|')))
                                return lx;

                            if (lx == "Range status and errors")
                            {
                                Bind.IsRangeRip = true;
                                return parser.ReadLine();
                            }

                            if (lx.Length >= 60)
                            {
                                int tn = 0;
                                if (int.TryParse (lx.Substring (0, 9), out tn))
                                    ++Bind.TocTrackCount;
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
                                        Bind.Interface = kk;
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
                                        Bind.Drive = optVal;
                                    else if (optKey == "Utilize accurate stream")
                                        Bind.AccurateStream = optVal;
                                    else if (optKey == "Defeat audio cache")
                                        Bind.DefeatCache = optVal;
                                    else if (optKey == "Make use of C2 pointers")
                                        Bind.UseC2 = optVal;
                                    else if (optKey == "Read mode")
                                        Bind.ReadMode = optVal;
                                    else if (optKey == "Read offset correction" || optKey == "Combined read/write offset correction")
                                        Bind.ReadOffset = optVal;
                                    else if (optKey == "Overread into Lead-In and Lead-Out")
                                        Bind.Overread = optVal;
                                    else if (optKey == "Fill up missing offset samples with silence")
                                        Bind.FillWithSilence = optVal;
                                    else if (optKey == "Delete leading and trailing silent blocks")
                                        Bind.TrimSilence = optVal;
                                    else if (optKey == "Null samples used in CRC calculations")
                                        Bind.CalcWithNulls = optVal;
                                    else if (optKey == "Normalize to")
                                        Bind.NormalizeTo = optVal;
                                    else if (optKey == "Used interface")
                                        Bind.Interface = optVal;
                                    else if (optKey == "Gap handling")
                                        Bind.GapHandling = optVal;
                                    else if (optKey == "Sample format")
                                        Bind.SampleFormat = optVal;
                                    else if (optKey == "Quality")
                                        Bind.Quality = optVal;
                                    else if (optKey == "Add ID3 tag")
                                        Bind.Id3Tag = optVal;
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

                    int num;
                    bool success = Int32.TryParse (lx.Substring (6), out num);
                    if (! success)
                    {
                        Bind.TkIssue = IssueModel.Add ("Invalid track " + parser.GetPlace(), Severity.Fatal, IssueTags.Failure);
                        break;
                    }

                    lx = parser.ReadLineLTrim();
                    if (! lx.StartsWith ("Filename "))
                        Bind.TkIssue = IssueModel.Add ("Track " + num + ": Missing 'Filename'.", Severity.Error, IssueTags.Failure);
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
                        Bind.TpIssue = IssueModel.Add ("Track " + num + ": Invalid test CRC-32.", Severity.Error, IssueTags.Failure);
                    lx = parser.ReadLineLTrim();
                }

                if (! lx.StartsWith ("Copy CRC "))
                    IssueModel.Add ("Track " + num + ": Missing copy CRC-32.", Severity.Warning);
                else
                {
                    if (uint.TryParse (lx.Substring (9), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out word))
                        copyCrc = word;
                    else
                        Bind.TkIssue = IssueModel.Add ("Track " + num + ": Invalid copy CRC-32.", Severity.Error, IssueTags.Failure);
                    lx = parser.ReadLineLTrim();
                }

                if (lx.StartsWith ("Accurately ripped (confidence "))
                {
                    arVersion = lx.Contains("AR v2")? 2 : 1;
                    int val;
                    bool isOk = ToInt (lx, 30, out val);
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
                    if (Bind.AccurateRipConfidence == null || Bind.AccurateRipConfidence > arConfidence)
                        Bind.AccurateRipConfidence = arConfidence;
                    if (arVersion != null)
                        if (Bind.AccurateRip == null || Bind.AccurateRip.Value > arVersion.Value)
                            Bind.AccurateRip = arVersion;
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
                    Bind.CueToolsConfidence = 0;
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
                        Bind.CueToolsConfidence = ctConfidence;
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
                        { Bind.CueToolsConfidence = -1; return lx; }
                    }
                    else if (lx.Contains ("Differs"))
                        ctConfidence = -1;
                    else
                        break;

                    if (tx < TracksModel.Bind.Items.Count)
                        TracksModel.SetCtConfidence (tx, ctConfidence);
                    if (Bind.CueToolsConfidence == null || Bind.CueToolsConfidence > ctConfidence)
                        Bind.CueToolsConfidence = ctConfidence;
                }
                return parser.ReadLineLTrim();
            }
        }
    }
}

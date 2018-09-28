using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Diagnostics;
using NongIssue;
using NongCrypto;

namespace NongFormat
{
    public enum Mp3ChannelMode
    { Stereo, JointStereo, DualChannel, SingleChannel }

    public partial class Mp3Format : FormatBase
    {
        public static string[] Names => new string[] { "mp3" };
        public override string[] ValidNames => Names;

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if ((hdr[0]=='I' && hdr[1]=='D' && hdr[2]=='3') || (hdr[0] == 0xFF && ((hdr[1] & 0xE6) == 0xE2)))
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public Mp3XingBlock.Model XingModel { get; private set; }
            public Mp3LameBlock.Model LameModel { get; private set; }
            public new readonly Mp3Format Data;

            public Model (Stream stream, byte[] header, string path)
            {
                base._data = Data = new Mp3Format (stream, header, path);
                Data.Issues = IssueModel.Data;

                if (Data.FileSize > Int32.MaxValue)
                {
                    IssueModel.Add ("File is insanely large", Severity.Fatal);
                    return;
                }

                int mediaPos32;
                int mediaCount32;
                byte[] fBuf;

                fBuf = Data.fBuf = new byte[Data.FileSize];
                Data.fbs.Position = 0;
                if (Data.fbs.Read (fBuf, 0, (int) Data.FileSize) != Data.FileSize)
                {
                    IssueModel.Add ("Read error", Severity.Fatal);
                    return;
                }

                // Detect ID3v2 tag block.
                if (fBuf[0]=='I' && fBuf[1]=='D' && fBuf[2]=='3')
                {
                    Data.id3v2Pos = 0;
                    if (Data.FileSize < 10)
                    {
                        IssueModel.Add ("File truncated near ID3v2 header", Severity.Fatal);
                        return;
                    }

                    Data.Id3v2Major = Data.fBuf[3];
                    Data.Id3v2Revision = Data.fBuf[4];

                    Data.storedId3v2DataSize = ((fBuf[6] & 0x7F) << 21) + ((fBuf[7] & 0x7F) << 14) + ((fBuf[8] & 0x7F) << 7) + (fBuf[9] & 0x7F);
                    if (Data.storedId3v2DataSize == 0 || Data.storedId3v2DataSize+12 > Data.FileSize)
                    {
                        IssueModel.Add ("ID3v2 size of " + Data.storedId3v2DataSize + " is bad or file is truncated", Severity.Fatal);
                        return;
                    }

                    if ((fBuf[6] & 0x80) != 0 || (fBuf[7] & 0x80) != 0 || (fBuf[8] & 0x80) != 0 || (Data.fBuf[9] & 0x80) != 0)
                        IssueModel.Add ("Zero parity error on ID3v2 size");

                    Data.actualId3v2DataSize = Data.storedId3v2DataSize;

                    // Check for bug in old versions of EAC that 1.5% of time writes ID3v2 size over by 1.
                    if (Data.Id3v2Major == 3)
                        if ((fBuf[10+Data.storedId3v2DataSize] & 0xFE) == 0xFA)
                        {
                            if (fBuf[9+Data.storedId3v2DataSize] == 0xFF)
                            {
                                --Data.actualId3v2DataSize;
                                // Bug bite comfirmed.
                                Data.Id3v2TagRepair = String.Format ("0009=0x{0:X2}", Data.actualId3v2DataSize & 0x7F);
                                if ((Data.actualId3v2DataSize & 0x7F) == 0x7F)
                                    Data.Id3v2TagRepair = String.Format ("0008=0x{0:X2}, ", ((Data.actualId3v2DataSize >> 7) & 0x7F)) + Data.Id3v2TagRepair;
                            }
                        }

                    Data.ValidSize = 10 + Data.actualId3v2DataSize;
                }

                while (Data.ValidSize < Data.FileSize && fBuf[Data.ValidSize]==0)
                {
                    ++Data.ValidSize;
                    ++Data.DeadBytes;
                }

                if (Data.FileSize - Data.ValidSize < 0xC0)
                {
                    IssueModel.Add ("File appears truncated.", Severity.Fatal);
                    return;
                }

                Data.Header = new Mp3Header (fBuf, (int) Data.ValidSize);

                if (! Data.Header.IsMpegLayer3)
                {
                    IssueModel.Add ("ID3v2 tag present but no MP3 marker found.", Severity.Fatal);
                    return;
                }

                if (Data.Header.MpegVersionBits == 1)
                {
                    IssueModel.Add ("MP3 marker found but MPEG version is not valid.", Severity.Fatal);
                    return;
                }

                mediaPos32 = (int) Data.ValidSize;
                Data.ValidSize = Data.FileSize;

                // Keep the audio header.
                Data.aBuf = new byte[Data.Header.XingOffset + 0x9C];
                Array.Copy (fBuf, mediaPos32, Data.aBuf, 0, Data.aBuf.Length);

                // Detect Xing/LAME encodes:

                XingModel = Mp3XingBlock.Create (Data.aBuf, Data.Header);
                if (XingModel != null)
                {
                    LameModel = XingModel as Mp3LameBlock.Model;
                    Data.Xing = XingModel.BindXing;
                    Data.Lame = Data.Xing as Mp3LameBlock;
                }

                // Detect ID3v1 tag block:

                int ePos = (int) Data.FileSize;
                if (ePos >= 130 && fBuf[ePos-128]=='T' && fBuf[ePos-127]=='A' && fBuf[ePos-126]=='G')
                {
                    ePos -= 128;
                    Data.id3v1Block = new byte[128];
                    Array.Copy (fBuf, ePos, Data.id3v1Block, 0, 128);
                }

                // Detect obsolete Lyrics3v2 block:

                if (ePos >= 15)
                {
                    if (fBuf[ePos-9]=='L' && fBuf[ePos-8]=='Y' && fBuf[ePos-7]=='R' && fBuf[ePos-6]=='I' && fBuf[ePos-5]=='C' && fBuf[ePos-4]=='S'
                        && fBuf[ePos-3]=='2' && fBuf[ePos-2]=='0' && fBuf[ePos-1]=='0')
                    {
                        int l3size = 0;
                        for (var bi = ePos-15; bi < ePos-9; ++bi)
                        {
                            if (fBuf[bi] < '0' || fBuf[bi] > '9')
                            {
                                IssueModel.Add ("Invalid Lyrics3v2 length digit.");
                                return;
                            }
                            l3size = l3size * 10 + fBuf[bi]-'0';
                        }

                        Data.Lyrics3Size = l3size + 15;
                        ePos -= Data.Lyrics3Size;
                        if (ePos < 2)
                        {
                            IssueModel.Add ("Invalid Lyrics3v2 length.");
                            return;
                        }
                    }
                }

                // Detect APE tag block:

                if (ePos >= 34)
                {
                    int pos = ePos-32;
                    if (fBuf[pos]=='A' && fBuf[pos+1]=='P' && fBuf[pos+2]=='E'
                        && fBuf[pos+3]=='T' && fBuf[pos+4]=='A' && fBuf[pos+5] == 'G' && fBuf[pos+6]=='E' && fBuf[pos+7]=='X')
                    {
                        Data.ApeSize = 32 + fBuf[pos+12] + (fBuf[pos+13] << 8) + (fBuf[pos+14] << 16) + (fBuf[pos+15] << 24);
                        ePos -= Data.ApeSize;
                    }
                }

                if (ePos <= mediaPos32)
                {
                    IssueModel.Add ("Missing audio", Severity.Fatal);
                    return;
                }

                mediaCount32 = ePos - mediaPos32;

                // Detect 2nd, phantom ID3v1 tag block:

                if (Data.Lame != null && mediaCount32 == Data.Lame.LameSize + 128 && Data.HasId3v1 && ! Data.HasLyrics3 && ! Data.HasApe && ePos > 34)
                    if (fBuf[ePos-128]=='T' && fBuf[ePos-127]=='A' && fBuf[ePos-126]=='G')
                    {
                        ePos -= 128;
                        mediaCount32 -= 128;
                        Data.ValidSize -= 128;

                        Data.excess = Data.id3v1Block;
                        Data.id3v1Block = new byte[128];
                        Array.Copy (fBuf, ePos, Data.id3v1Block, 0, 128);
                        Data.Watermark = Likeliness.Probable;
                    }

                Data.mediaPosition = mediaPos32;
                Data.MediaCount = mediaCount32;

                GetDiagnostics();
            }


            private void GetDiagnostics()
            {
                if (Data.Header.BitRate == null)
                    IssueModel.Add ("Invalid bit rate.");

                if (Data.Header.ChannelMode != Mp3ChannelMode.JointStereo)
                {
                    IssueTags tags = Data.Header.ChannelMode==Mp3ChannelMode.Stereo? IssueTags.Overstandard : IssueTags.Substandard;
                    IssueModel.Add ("Channel mode is " + Data.Header.ChannelMode + ".", Severity.Advisory, tags);
                }

                if (Data.Header.SampleRate < 44100)
                    IssueModel.Add ("Frequency is " + Data.Header.SampleRate + " Hz (expecting 44100 or better)", Severity.Advisory, IssueTags.Substandard);
                else if (Data.Header.SampleRate > 44100)
                    IssueModel.Add ("Frequency is " + Data.Header.SampleRate, Severity.Advisory, IssueTags.Overstandard);

                if (Data.Xing != null)
                {
                    if (Data.Header.CrcProtectedBit == 0)
                        IssueModel.Add ("Header not flagged for CRC protection.", Severity.Noise);

                    if (! Data.Xing.HasFrameCount)
                        IssueModel.Add ("Missing XING frame count.");

                    if (! Data.Xing.HasSize)
                        IssueModel.Add ("Missing XING file size.");

                    if (! Data.Xing.HasTableOfContents)
                        IssueModel.Add ("Missing XING table of contents.");
                    else
                        if (Data.Xing.IsTocCorrupt())
                            IssueModel.Add ("XING table of contents is corrupt.");

                    if (Data.Xing.HasQualityIndicator)
                    {
                        var qi = Data.Xing.QualityIndicator;
                        if (qi < 0 || qi > 100)
                            IssueModel.Add ("Quality indicator of " + qi + " is out of range.");
                        else
                            if (Data.Lame != null && Data.Lame.IsVbr && qi < 78)
                                IssueModel.Add ("VBR quality of " + qi + " is substandard.", Severity.Advisory, IssueTags.Substandard);
                    }
                }

                if (Data.Lame == null)
                    IssueModel.Add ("Not a LAME encoding.", Severity.Advisory, IssueTags.Substandard);
                else
                {
                    var isBlessed = blessedLames.Any (item => item == Data.Lame.LameVersion);
                    if (! isBlessed)
                        IssueModel.Add ("LAME version is not favored.", Severity.Advisory, IssueTags.Substandard);

                    if (Data.Lame.LameSize != Data.MediaCount)
                        IssueModel.Add ("Indicated LAME audio size incorrect or unrecognized tag block.", Severity.Warning);

                    if (Data.Lame.TagRevision == 0xF)
                        IssueModel.Add ("Tag revision " + Data.Lame.TagRevision + "invalid.");

                    if (Data.Lame.BitrateMethod == 0 || Data.Lame.BitrateMethod == 0xF)
                        IssueModel.Add ("Bitrate method " + Data.Lame.BitrateMethod + " invalid.");

                    if (Data.Lame.IsAbr)
                        IssueModel.Add ("ABR encoding method is obsolete.", Severity.Advisory, IssueTags.Substandard);

                    if (Data.Lame.AudiophileReplayGain != 0)
                        IssueModel.Add ("Audiophile ReplayGain (" + Data.Lame.AudiophileReplayGain + ") usage is obsolete.", Severity.Advisory, IssueTags.Substandard);

                    if (Data.Lame.IsCbr && Mp3Header.IsBadCbr (Data.Header.MpegVersionBits, Data.Lame.MinBitRate))
                        IssueModel.Add ("Minimum bit rate of " + Data.Lame.MinBitRate + " not valid.", Severity.Advisory, IssueTags.Substandard);
                }

                if (Data.HasId3v1)
                {
                    if (Data.HasId3v1Phantom)
                        IssueModel.Add ("Has phantom ID3v1 tag block.",
                            Severity.Warning, IssueTags.Fussy|IssueTags.HasId3v1,
                            "Remove phantom ID3v1 tag block", RepairPhantomTag);
                    else
                    {
                        var minor = GetMinorOfV1 (Data.id3v1Block);
                        Severity sev = minor == 0? Severity.Warning : Severity.Noise;
                        IssueModel.Add ("Has ID3v1." + minor + " tags.", sev, IssueTags.HasId3v1);
                    }
                }

                if (! Data.HasId3v2)
                    IssueModel.Add ("Missing ID3v2 tags.", Severity.Trivia, IssueTags.Fussy|IssueTags.Substandard);
                else
                {
                    switch (Data.Id3v2Major)
                    {
                        case 2:
                            IssueModel.Add ("Has obsolete ID3v2.2 tags.", Severity.Warning, IssueTags.Fussy|IssueTags.Substandard);
                            break;
                        case 3:
                            // Hunky dory!
                            break;
                        case 4:
                            IssueModel.Add ("Has jumped-the-shark ID3v2.4 tags.", Severity.Trivia);
                            break;
                        default:
                            IssueModel.Add ("Has ID3 tags of unknown version 2." + Data.Id3v2Major);
                            break;
                    }

                    if (Data.Id3v2TagRepair != null)
                        IssueModel.Add ("ID3v2 tag size over by 1 (repair with " + Data.Id3v2TagRepair + ").",
                            Severity.Warning, IssueTags.Fussy,
                            "Patch EAC induced ID3v2 tag size error", RepairId3v2OffBy1);
                }

                if (Data.HasApe)
                    IssueModel.Add ("Has APE tags.", Severity.Trivia, IssueTags.HasApe);

                if (Data.HasLyrics3)
                    IssueModel.Add ("Has obsolete Lyrics3v2 block.", Severity.Advisory);

                if (Data.DeadBytes != 0)
                    IssueModel.Add ("Dead space preceeds audio, size=" + Data.DeadBytes, Severity.Warning, IssueTags.Substandard);
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Data.Issues.HasFatal)
                    return;

                if (Data.Lame != null && (hashFlags & Hashes.Intrinsic) != 0 && Data.Lame.ActualDataCrc == null)
                {
                    var hasher = new Crc16rHasher();
                    hasher.Append (Data.fBuf, (int) Data.mediaPosition, Data.Lame.LameHeaderSize);
                    byte[] hash = hasher.GetHashAndReset();
                    LameModel.SetActualHeaderCrc (BitConverter.ToUInt16 (hash, 0));

                    hasher.Append (Data.fBuf, (int) Data.mediaPosition + 0xC0, (int) Data.MediaCount - 0xC0);
                    hash = hasher.GetHashAndReset();
                    LameModel.SetActualDataCrc (BitConverter.ToUInt16 (hash, 0));

                    if (Data.IsBadHeader)
                        Data.ChIssue = IssueModel.Add ("CRC-16 check failed on audio header.", Severity.Error, IssueTags.Failure);

                    if (Data.IsBadData)
                        Data.CdIssue = IssueModel.Add ("CRC-16 check failed on audio data.", Severity.Error, IssueTags.Failure);
                    else if (Data.IsBadHeader)
                        Data.CdIssue = IssueModel.Add ("CRC-16 check successful on audio data.", Severity.Noise, IssueTags.Success);
                    else
                        Data.ChIssue = Data.CdIssue = IssueModel.Add ("CRC-16 checks successful.", Severity.Noise, IssueTags.Success);
                }

                base.CalcHashes (hashFlags, validationFlags);
            }


            public string RepairPhantomTag()
            {
                Debug.Assert (Data.fbs != null);
                if (Data.fbs == null || Data.Issues.MaxSeverity >= Severity.Error || ! Data.HasId3v1Phantom)
                    return "Invalid attempt";

                string result = null;
                try
                {
                    TruncateExcess();

                    // Overwrite the prior penultimate v1 with the just truncated v1 tag.
                    Data.fbs.Position = Data.FileSize - 128;
                    Data.fbs.Write (Data.id3v1Block, 0, 128);
                }
                catch (UnauthorizedAccessException ex)
                { result = ex.Message.TrimEnd (null); }
                catch (IOException ex)
                { result = ex.Message.TrimEnd (null); }

                return result;
            }


            public string RepairId3v2OffBy1()
            {
                Debug.Assert (Data.fbs != null);
                if (Data.fbs == null || Data.Issues.MaxSeverity >= Severity.Error || Data.storedId3v2DataSize == Data.actualId3v2DataSize)
                    return "Invalid attempt";

                // Assume values at 6,7 always 0.
                var bb = new byte[] { (byte) ((Data.actualId3v2DataSize >> 7) & 0x7F), (byte) (Data.actualId3v2DataSize & 0x7F) };

                string result = null;
                try
                {
                    Data.fbs.Position = 8;
                    Data.fbs.Write (bb, 0, 2);
                    Data.Id3v2TagRepair = null;
                    Data.storedId3v2DataSize = Data.actualId3v2DataSize;
                }
                catch (UnauthorizedAccessException ex)
                { result = ex.Message.TrimEnd (null); }
                catch (IOException ex)
                { result = ex.Message.TrimEnd (null); }

                return result;
            }
        }


        static private readonly string[] blessedLames = { "LAME3.90.", "LAME3.90r", "LAME3.92 ", "LAME3.99r", "LAME3.100" };

        private byte[] aBuf;   // Keeps the lame header loaded.
        public Mp3Header Header { get; private set; }
        public Mp3XingBlock Xing { get; private set; }
        public Mp3LameBlock Lame { get; private set; }

        private static int GetMinorOfV1 (byte[] v1tag) => v1tag[0x7D] != 0 || v1tag[0x7E] == 0 ? 0 : 1;

        private int id3v2Pos = -1;
        private int storedId3v2DataSize, actualId3v2DataSize;
        public bool HasId3v2 => id3v2Pos >= 0;
        public int Id3v1Minor => GetMinorOfV1 (id3v1Block);
        public byte Id3v2Major { get; private set; }
        public byte Id3v2Revision { get; private set; }
        public string Id3v2TagRepair { get; private set; }
        public int Id3v2Size => actualId3v2DataSize+10;

        private byte[] id3v1Block = null;
        public bool HasId3v1 => id3v1Block != null;
        public bool HasId3v1Phantom => excess != null;

        public int ApeSize { get; private set; }
        public bool HasApe => ApeSize > 0;
        public int Lyrics3Size { get; private set; }
        public bool HasLyrics3 => Lyrics3Size > 0;

        public int DeadBytes { get; private set; }

        private string layout = null;
        public string Layout
        {
            get
            {
                if (layout == null)
                {
                    var sb = new StringBuilder ("|");
                    if (HasId3v2)
                    { sb.Append (" ID3v2."); sb.Append (Id3v2Major); sb.Append (" ("); sb.Append (Id3v2Size); sb.Append(") |"); }
                    if (DeadBytes > 0)
                    { sb.Append (" Dead ("); sb.Append (DeadBytes); sb.Append (") |"); }
                    sb.Append (" Audio (");
                    sb.Append (MediaCount.ToString());
                    sb.Append (") |");
                    if (HasApe)
                    { sb.Append (" APE ("); sb.Append (ApeSize); sb.Append (") |"); }
                    if (HasLyrics3)
                    { sb.Append (" Lyrics3v2 ("); sb.Append (Lyrics3Size); sb.Append (") |"); }
                    if (HasId3v1Phantom)
                        sb.Append (" ID3v1 (128) |");
                    if (HasId3v1)
                    { sb.Append(" ID3v1."); sb.Append (GetMinorOfV1 (id3v1Block)); sb.Append (" (128) |"); }
                    layout = sb.ToString();
                }
                return layout;
            }
        }

        public Issue ChIssue { get; private set; }
        public Issue CdIssue { get; private set; }

        private Mp3Format (Stream stream, byte[] hdr, string path) : base (stream, path)
        { }

        public override bool IsBadHeader
         => Lame != null && Lame.ActualHeaderCrc != null && Lame.ActualHeaderCrc != Lame.StoredHeaderCrc;

        public override bool IsBadData
         => Lame != null && Lame.ActualDataCrc != null && Lame.ActualDataCrc != Lame.StoredDataCrc;


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (Issues.HasFatal)
                return;

            if (scope <= Granularity.Detail)
            {
                report.Add ($"MPEG size = {MediaCount}");
                if (Xing != null)
                    report.Add ($"XING size = {Xing.XingSize}");
                if (Lame != null)
                    report.Add ($"LAME size = {Lame.LameSize}");

                report.Add (String.Empty);
                report.Add ($"Raw audio header: {ConvertTo.ToBitString (Header.Bits, 32)}");
                report.Add (String.Empty);
            }

            report.Add ("Cooked audio header:");
            report.Add ($"  Codec = {Header.Codec}");
            report.Add ($"  Bit rate = {Header.BitRateText}");
            report.Add ($"  Frequency = {Header.SampleRateText}");
            report.Add ($"  Mode = {Header.ModeText}");

            if (scope <= Granularity.Detail)
            {
                report.Add ($"  CRC protection bit = {Header.CrcProtectedBit}");
                report.Add ($"  Padding bit = {Header.PaddingBit}");
                report.Add ($"  Private bit = {Header.PrivateBit}");
                report.Add ($"  Copyright bit = {Header.CopyrightBit}");
                report.Add ($"  Original bit = {Header.OriginalBit}");
                report.Add ($"  Emphasis = {Header.EmphasisText}");
            }

            if (Xing != null)
            {
                if (scope <= Granularity.Detail)
                    report.Add (String.Empty);

                if (scope <= Granularity.Detail)
                {
                    report.Add ("XING:");
                    report.Add ($"  String = {Xing.XingString}");
                    report.Add ($"  Layout = {Xing.Layout}");
                }
            }

            if (Lame != null)
            {
                if (scope <= Granularity.Detail)
                    report.Add (String.Empty);

                report.Add ("LAME:");
                report.Add ($"  Version string = {Lame.LameVersion}");
                report.Add ($"  Profile string = {Lame.Profile}");
                report.Add ($"  Profile detail = {Lame.Method}");

                if (scope <= Granularity.Detail)
                {
                    report.Add ($"  Tag revision = {Lame.TagRevision}");
                    report.Add ($"  Lowpass filter = {Lame.LowpassFilter}");
                    report.Add ($"  Replay Gain: Peak = {Lame.ReplayGainPeak}, Radio = {Lame.RadioReplayGain:X4}, Audiophile = {Lame.AudiophileReplayGain:X4}");
                    report.Add ($"  Lame encoding flags = {ConvertTo.ToBitString (Lame.LameFlags, 8)}");
                    report.Add ($"  Encoder delay: Start = {Lame.EncoderDelayStart}, End = {Lame.EncoderDelayEnd}");
                    report.Add ($"  LAME surround = {Lame.Surround}, LAME preset = {Lame.Preset}");
                    report.Add ($"  MP3 gain = {Lame.Mp3Gain}");
                    report.Add ($"  Minimum bit rate = {Lame.MinBitRateText}");
                    report.Add ("  Checks:");
                    report.Add ($"    Stored: audio header CRC-16 = {Lame.StoredHeaderCrcText}, audio data CRC-16 = {Lame.StoredDataCrcText}");

                    if (Lame.ActualHeaderCrc != null)
                        report.Add ($"    Actual: audio header CRC-16 = {Lame.ActualHeaderCrcText}, audio data CRC-16 = {Lame.ActualDataCrcText}");
                }
            }

            if (scope <= Granularity.Detail)
                report.Add (String.Empty);

            report.Add ($"Layout = {Layout}");
        }
    }
}

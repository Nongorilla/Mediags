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
        public static string[] Names
        { get { return new string[] { "mp3" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x2C
                    && (hdr[0]=='I' && hdr[1]=='D' && hdr[2]=='3')
                    || (hdr[0]==0xFF && ((hdr[1]&0xF6)==0xF2)))
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public Mp3XingBlock.Model XingModel { get; private set; }
            public Mp3LameBlock.Model LameModel { get; private set; }
            public readonly Mp3Format Bind;

            public Model (Stream stream, byte[] header, string path)
            {
                BaseBind = Bind = new Mp3Format (stream, header, path);
                Bind.Issues = IssueModel.Bind;

                if (Bind.FileSize > Int32.MaxValue)
                {
                    IssueModel.Add ("File is insanely large", Severity.Fatal);
                    return;
                }

                int mediaPos32;
                int mediaCount32;
                byte[] fBuf;

                fBuf = Bind.fBuf = new byte[Bind.FileSize];
                Bind.fbs.Position = 0;
                if (Bind.fbs.Read (fBuf, 0, (int) Bind.FileSize) != Bind.FileSize)
                {
                    IssueModel.Add ("Read error", Severity.Fatal);
                    return;
                }

                // Detect ID3v2 tag block.
                if (fBuf[0]=='I' && fBuf[1]=='D' && fBuf[2]=='3')
                {
                    Bind.id3v2Pos = 0;
                    if (Bind.FileSize < 10)
                    {
                        IssueModel.Add ("File truncated near ID3v2 header", Severity.Fatal);
                        return;
                    }

                    Bind.Id3v2Major = Bind.fBuf[3];
                    Bind.Id3v2Revision = Bind.fBuf[4];

                    Bind.storedId3v2DataSize = ((fBuf[6] & 0x7F) << 21) + ((fBuf[7] & 0x7F) << 14) + ((fBuf[8] & 0x7F) << 7) + (fBuf[9] & 0x7F);
                    if (Bind.storedId3v2DataSize == 0 || Bind.storedId3v2DataSize+12 > Bind.FileSize)
                    {
                        IssueModel.Add ("ID3v2 size of " + Bind.storedId3v2DataSize + " is bad or file is truncated", Severity.Fatal);
                        return;
                    }

                    if ((fBuf[6] & 0x80) != 0 || (fBuf[7] & 0x80) != 0 || (fBuf[8] & 0x80) != 0 || (Bind.fBuf[9] & 0x80) != 0)
                        IssueModel.Add ("Zero parity error on ID3v2 size");

                    Bind.actualId3v2DataSize = Bind.storedId3v2DataSize;

                    // Check for bug in old versions of EAC that 1.5% of time writes ID3v2 size over by 1.
                    if (Bind.Id3v2Major == 3)
                        if ((fBuf[10+Bind.storedId3v2DataSize] & 0xFE) == 0xFA)
                        {
                            if (fBuf[9+Bind.storedId3v2DataSize] == 0xFF)
                            {
                                --Bind.actualId3v2DataSize;
                                // Bug bite comfirmed.
                                Bind.Id3v2TagRepair = String.Format ("0009=0x{0:X2}", Bind.actualId3v2DataSize & 0x7F);
                                if ((Bind.actualId3v2DataSize & 0x7F) == 0x7F)
                                    Bind.Id3v2TagRepair = String.Format ("0008=0x{0:X2}, ", ((Bind.actualId3v2DataSize >> 7) & 0x7F)) + Bind.Id3v2TagRepair;
                            }
                        }

                    Bind.ValidSize = 10 + Bind.actualId3v2DataSize;
                }

                while (Bind.ValidSize < Bind.FileSize && fBuf[Bind.ValidSize]==0)
                {
                    ++Bind.ValidSize;
                    ++Bind.DeadBytes;
                }

                // Detect MP3 header.
                if (Bind.FileSize-Bind.ValidSize < 0xC0 || fBuf[Bind.ValidSize] != 0xFF || (fBuf[Bind.ValidSize+1] & 0xF6) != 0xF2)
                {
                    if (Bind.HasId3v2)
                        IssueModel.Add ("ID3v2 tag present but no MP3 marker found.", Severity.Fatal);
                    else
                        IssueModel.Add ("MP3 marker not found.", Severity.Fatal);
                    return;
                }

                mediaPos32 = (int) Bind.ValidSize;
                Bind.ValidSize = Bind.FileSize;

                // Keep the audio header.
                Bind.aHdr = new byte[0xC0];
                Array.Copy (fBuf, mediaPos32, Bind.aHdr, 0, 0xC0);

                // Basic MP3 header:

                Debug.Assert ((Bind.aHdr[1] & 0x10) == 0x10);
                Debug.Assert (Bind.MpegLayerBits == 1);

                Bind.MpegVersion = Bind.MpegVersionBits == 0? 2 : 1;
                Bind.IsProtected = Bind.ProtectedBit == 0;
                Bind.BitRate = bitRateMap[Bind.MpegVersionBits][Bind.BitRateBits];
                Bind.Frequency = samplingRateMap[Bind.MpegVersionBits][Bind.FrequencyBits];
                Bind.IsFramePadded = Bind.FramePaddedBit != 0;
                Bind.IsPrivate = Bind.PrivateBit != 0;
                Bind.Mode = (Mp3ChannelMode) Bind.ModeBits;
                Bind.IsIntensityStereo = Bind.IntensityStereoBit != 0;
                Bind.IsMsStereo = Bind.MsStereoBit != 0;
                Bind.IsCopyrighted = Bind.CopyrightBit != 0;
                Bind.IsOriginal = Bind.OriginalBit != 0;
                Bind.Emphasis = emphasisMap [Bind.EmphasisBits];

                // Detect Xing/LAME encodes:

                string xingText = "";
                for (var hi = mediaPos32 + 0x24; hi <= mediaPos32 + 0x27; ++hi)
                    if (fBuf[hi] >= 32 && fBuf[hi] < 127)
                        xingText += (char) fBuf[hi];

                string lameText = "";
                for (var hi = mediaPos32 + 0x9C; hi <= mediaPos32 + 0xA4; ++hi)
                    if (fBuf[hi] >= 32 && fBuf[hi] < 127)
                        lameText += (char) fBuf[hi];

                if (xingText != null && (xingText == "Info" || xingText == "Xing"))
                    if (lameText == null || ! lameText.StartsWith ("LAME"))
                    {
                        XingModel = new Mp3XingBlock.Model (Bind.aHdr, xingText);
                        Bind.Xing = XingModel.BindXing;
                    }
                    else
                    {
                        XingModel = LameModel = new Mp3LameBlock.Model (Bind.aHdr, lameText, xingText);
                        Bind.Xing = XingModel.BindXing;
                        Bind.Lame = LameModel.Bind;
                    }

                // Detect ID3v1 tag block:

                int ePos = (int) Bind.FileSize;
                if (ePos >= 130 && fBuf[ePos-128]=='T' && fBuf[ePos-127]=='A' && fBuf[ePos-126]=='G')
                {
                    ePos -= 128;
                    Bind.id3v1Block = new byte[128];
                    Array.Copy (fBuf, ePos, Bind.id3v1Block, 0, 128);
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

                        Bind.Lyrics3Size = l3size + 15;
                        ePos -= Bind.Lyrics3Size;
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
                        Bind.ApeSize = 32 + fBuf[pos+12] + (fBuf[pos+13] << 8) + (fBuf[pos+14] << 16) + (fBuf[pos+15] << 24);
                        ePos -= Bind.ApeSize;
                    }
                }

                if (ePos <= mediaPos32)
                {
                    IssueModel.Add ("Missing audio", Severity.Fatal);
                    return;
                }

                mediaCount32 = ePos - mediaPos32;

                // Detect 2nd, phantom ID3v1 tag block:

                if (Bind.Lame != null && mediaCount32 == Bind.Lame.LameSize + 128 && Bind.HasId3v1 && ! Bind.HasLyrics3 && ! Bind.HasApe && ePos > 34)
                    if (fBuf[ePos-128]=='T' && fBuf[ePos-127]=='A' && fBuf[ePos-126]=='G')
                    {
                        ePos -= 128;
                        mediaCount32 -= 128;
                        Bind.ValidSize -= 128;

                        Bind.excess = Bind.id3v1Block;
                        Bind.id3v1Block = new byte[128];
                        Array.Copy (fBuf, ePos, Bind.id3v1Block, 0, 128);
                        Bind.Watermark = Likeliness.Probable;
                    }

                Bind.mediaPosition = mediaPos32;
                Bind.MediaCount = mediaCount32;

                GetDiagnostics();
            }


            private void GetDiagnostics()
            {
                if (Bind.BitRate == null)
                    IssueModel.Add ("Invalid bit rate.");

                if (Bind.Mode != Mp3ChannelMode.JointStereo)
                {
                    IssueTags tags = Bind.Mode==Mp3ChannelMode.Stereo? IssueTags.Overstandard : IssueTags.Substandard;
                    IssueModel.Add ("Channel mode is " + Bind.Mode + ".", Severity.Advisory, tags);
                }

                if (Bind.Frequency < 44100)
                    IssueModel.Add ("Frequency is " + Bind.Frequency + " Hz (expecting 44100 or better)", Severity.Advisory, IssueTags.Substandard);
                else if (Bind.Frequency > 44100)
                    IssueModel.Add ("Frequency is " + Bind.Frequency, Severity.Advisory, IssueTags.Overstandard);

                if (Bind.IsXing)
                {
                    if (! Bind.IsProtected)
                        IssueModel.Add ("Header not flagged for CRC protection.", Severity.Noise);

                    if (! Bind.Xing.IsFrameCountPresent)
                        IssueModel.Add ("Missing XING frame count.");

                    if (! Bind.Xing.IsBytesPresent)
                        IssueModel.Add ("Missing XING file size.");

                    if (! Bind.Xing.IsTableOfContentsPresent)
                        IssueModel.Add ("Missing XING table of contents.");
                    else
                        if (Bind.Xing.IsTocCorrupt())
                            IssueModel.Add ("XING table of contents is corrupt.");

                    if (Bind.Xing.QualityIndicator != null)
                    {
                        var qi = Bind.Xing.QualityIndicator;
                        if (qi < 0 || qi > 100)
                            IssueModel.Add ("Quality indicator of " + qi + " is out of range.");
                        else
                            if (Bind.Lame != null && Bind.Lame.IsVbr && qi < 78)
                                IssueModel.Add ("VBR quality of " + qi + " is substandard.", Severity.Advisory, IssueTags.Substandard);
                    }
                }

                if (! Bind.IsLame)
                    IssueModel.Add ("Not a LAME encoding.", Severity.Advisory, IssueTags.Substandard);
                else
                {
                    var isBlessed = blessedLames.Any (item => item == Bind.Lame.Version);
                    if (! isBlessed)
                        IssueModel.Add ("LAME version is not favored.", Severity.Advisory, IssueTags.Substandard);

                    if (Bind.Lame.LameSize != Bind.MediaCount)
                        IssueModel.Add ("Indicated LAME audio size incorrect or unrecognized tag block.", Severity.Warning);

                    if (Bind.Lame.TagRevision == 0xF)
                        IssueModel.Add ("Tag revision " + Bind.Lame.TagRevision + "invalid.");

                    if (Bind.Lame.BitrateMethod == 0 || Bind.Lame.BitrateMethod == 0xF)
                        IssueModel.Add ("Bitrate method " + Bind.Lame.BitrateMethod + " invalid.");

                    if (Bind.Lame.LowpassFilter == 0)
                        IssueModel.Add ("Lowpass filter " + Bind.Lame.LowpassFilter + " invalid.");

                    if (Bind.Lame.IsAbr)
                        IssueModel.Add ("ABR encoding method is obsolete.", Severity.Advisory, IssueTags.Substandard);

                    if (Bind.Lame.AudiophileReplayGain != 0)
                        IssueModel.Add ("Audiophile ReplayGain (" + Bind.Lame.AudiophileReplayGain + ") usage is obsolete.", Severity.Advisory, IssueTags.Substandard);

                    var isBadRate = false;
                    if (Bind.Lame.IsCbr)
                        for (var ri = 0;;)
                            if (bitRateMap1L3[ri] == Bind.Lame.MinBitRate)
                                break;
                            else if (++ri == bitRateMap1L3.Length - 1)
                            {
                                isBadRate = true;
                                break;
                            }

                    if (isBadRate)
                        IssueModel.Add ("Minimum bit rate of " + Bind.Lame.MinBitRate + " not valid.", Severity.Advisory, IssueTags.Substandard);
                }

                if (Bind.HasId3v1)
                {
                    if (Bind.HasId3v1Phantom)
                        IssueModel.Add ("Has phantom ID3v1 tag block.",
                            Severity.Warning, IssueTags.Fussy|IssueTags.HasId3v1,
                            "Remove phantom ID3v1 tag block", RepairPhantomTag);
                    else
                    {
                        var minor = GetMinorOfV1 (Bind.id3v1Block);
                        Severity sev = minor == 0? Severity.Warning : Severity.Noise;
                        IssueModel.Add ("Has ID3v1." + minor + " tags.", sev, IssueTags.HasId3v1);
                    }
                }

                if (! Bind.HasId3v2)
                    IssueModel.Add ("Missing ID3v2 tags.", Severity.Trivia, IssueTags.Fussy|IssueTags.Substandard);
                else
                {
                    switch (Bind.Id3v2Major)
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
                            IssueModel.Add ("Has ID3 tags of unknown version 2." + Bind.Id3v2Major);
                            break;
                    }

                    if (Bind.Id3v2TagRepair != null)
                        IssueModel.Add ("ID3v2 tag size over by 1 (repair with " + Bind.Id3v2TagRepair + ").",
                            Severity.Warning, IssueTags.Fussy,
                            "Patch EAC induced ID3v2 tag size error", RepairId3v2OffBy1);
                }

                if (Bind.HasApe)
                    IssueModel.Add ("Has APE tags.", Severity.Trivia, IssueTags.HasApe);

                if (Bind.HasLyrics3)
                    IssueModel.Add ("Has obsolete Lyrics3v2 block.", Severity.Advisory);

                if (Bind.DeadBytes != 0)
                    IssueModel.Add ("Dead space preceeds audio, size=" + Bind.DeadBytes, Severity.Warning, IssueTags.Substandard);
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Bind.Issues.HasFatal)
                    return;

                if (Bind.Lame != null && (hashFlags & Hashes.Intrinsic) != 0 && Bind.Lame.ActualAudioDataCRC16 == null)
                {
                    var hasher = new Crc16rHasher();
                    hasher.Append (Bind.fBuf, (int) Bind.mediaPosition, 0xBE);
                    byte[] hash = hasher.GetHashAndReset();
                    LameModel.SetActualAudioHeaderCRC16 (BitConverter.ToUInt16 (hash, 0));

                    hasher.Append (Bind.fBuf, (int) Bind.mediaPosition + 0xC0, (int) Bind.MediaCount - 0xC0);
                    hash = hasher.GetHashAndReset();
                    LameModel.SetActualAudioDataCRC16 (BitConverter.ToUInt16 (hash, 0));

                    if (Bind.IsBadHeader)
                        IssueModel.Add ("CRC-16 check failed on audio header.");

                    if (Bind.IsBadData)
                        IssueModel.Add ("CRC-16 check failed on audio data.");
                    else if (! Bind.IsBadHeader)
                        IssueModel.Add ("CRC-16 checks successful.", Severity.Noise);
                }

                base.CalcHashes (hashFlags, validationFlags);
            }


            public string RepairPhantomTag()
            {
                System.Diagnostics.Debug.Assert (Bind.fbs != null);
                if (Bind.fbs == null || Bind.Issues.MaxSeverity >= Severity.Error || ! Bind.HasId3v1Phantom)
                    return "Invalid attempt";

                string result = null;
                try
                {
                    TruncateExcess();

                    // Overwrite the prior penultimate v1 with the just truncated v1 tag.
                    Bind.fbs.Position = Bind.FileSize - 128;
                    Bind.fbs.Write (Bind.id3v1Block, 0, 128);
                }
                catch (UnauthorizedAccessException ex)
                { result = ex.Message.TrimEnd (null); }
                catch (IOException ex)
                { result = ex.Message.TrimEnd (null); }

                return result;
            }


            public string RepairId3v2OffBy1()
            {
                System.Diagnostics.Debug.Assert (Bind.fbs != null);
                if (Bind.fbs == null || Bind.Issues.MaxSeverity >= Severity.Error || Bind.storedId3v2DataSize == Bind.actualId3v2DataSize)
                    return "Invalid attempt";

                // Assume values at 6,7 always 0.
                var bb = new byte[] { (byte) ((Bind.actualId3v2DataSize >> 7) & 0x7F), (byte) (Bind.actualId3v2DataSize & 0x7F) };

                string result = null;
                try
                {
                    Bind.fbs.Position = 8;
                    Bind.fbs.Write (bb, 0, 2);
                    Bind.Id3v2TagRepair = null;
                    Bind.storedId3v2DataSize = Bind.actualId3v2DataSize;
                }
                catch (UnauthorizedAccessException ex)
                { result = ex.Message.TrimEnd (null); }
                catch (IOException ex)
                { result = ex.Message.TrimEnd (null); }

                return result;
            }
        }


        private static int GetMinorOfV1 (byte[] v1tag)
        {
            return v1tag[0x7D] != 0 || v1tag[0x7E] == 0? 0 : 1;
        }

        static private readonly string[] blessedLames = { "LAME3.90.", "LAME3.90r", "LAME3.92 ", "LAME3.99r" };

        static private readonly int?[] bitRateMap1L3 =
        { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, null };  // 0=free, null=reserved

        static private readonly int?[] bitRateMap2L3 =
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, null };  // 0=free, null=reserved

        static private readonly int?[][] bitRateMap =
        { bitRateMap2L3, bitRateMap1L3 };

        static private readonly int[] samplingRateMap1L3 = { 44100, 48000, 32000, 0 };  // 0 = reserved
        static private readonly int[] samplingRateMap2L3 = { 22050, 24000, 16000, 0 };  // 0 = reserved

        static private readonly int[][] samplingRateMap = { samplingRateMap2L3, samplingRateMap1L3 };

        static private readonly string[] emphasisMap = { "None", "50/15 MS", "Reserved", "CCIT J.17" };

        public Mp3XingBlock Xing { get; private set; }
        public Mp3LameBlock Lame { get; private set; }
        public bool IsXing { get { return Xing != null; } }
        public bool IsLame { get { return Lame != null; } }

        private byte[] aHdr;   // Keeps the header loaded.

        public int MpegVersionBits { get { return (aHdr[1] & 0x08) >> 3; } }
        public int MpegVersion { get; private set; }
        public int MpegLayerBits { get { return (aHdr[1] & 6) >> 1; } }
        public int MpegLayer { get { return 3; } }
        public int ProtectedBit { get { return aHdr[1] & 1; } }
        public bool IsProtected { get; private set; } // Not reliable since LAME header CRC should *always* be accurate.
        public int BitRateBits { get { return aHdr[2] >> 4; } }
        public int? BitRate { get; private set; }
        public string BitRateToString() { return BitRate == null? "reserved" : BitRate.ToString(); }
        public int FrequencyBits { get { return (aHdr[2] & 0x0C) >> 2; } }
        public int? Frequency { get; private set; }
        public string FrequencyToString() { return Frequency == null? "reserved" : Frequency.ToString(); }
        public int FramePaddedBit { get { return (aHdr[2] & 2) >> 1; } }
        public bool IsFramePadded { get; private set; }
        public int PrivateBit { get { return aHdr[2] & 1; } }
        public bool IsPrivate { get; private set; }
        public int ModeBits { get { return aHdr[3] >> 6; } }
        public Mp3ChannelMode Mode { get; private set; }
        public int IntensityStereoBit { get { return (aHdr[3] & 0x20) >> 5; } }
        public bool IsIntensityStereo { get; private set; }
        public int MsStereoBit { get { return (aHdr[3] & 0x10) >> 4; } }
        public bool IsMsStereo { get; private set; }
        public int CopyrightBit { get { return (aHdr[3] & 8) >> 3; } }
        public bool IsCopyrighted { get; private set; }
        public int OriginalBit { get { return (aHdr[3] & 4) >> 2; } }
        public bool IsOriginal { get; private set; }
        public int EmphasisBits { get { return aHdr[3] & 3; } }
        public string Emphasis { get; private set; }

        public string Codec { get { return "MPEG-" + MpegVersion + " Layer " + MpegLayer; } }

        public string ModeText
        {
            get
            {
                string result = Mode.ToString();
                if (Mode == Mp3ChannelMode.JointStereo)
                    result += " (Intensity stereo " + (IsIntensityStereo? "on" : "off")
                            + ", M/S stereo " + (IsMsStereo? "on" : "off") + ")";
                return result;
            }
        }

        private int id3v2Pos = -1;
        private int storedId3v2DataSize, actualId3v2DataSize;
        public bool HasId3v2 { get { return id3v2Pos >= 0; } }
        public int Id3v1Minor { get { return GetMinorOfV1 (id3v1Block); } }
        public byte Id3v2Major { get; private set; }
        public byte Id3v2Revision { get; private set; }
        public string Id3v2TagRepair { get; private set; }
        public int Id3v2Size { get { return actualId3v2DataSize+10; } }

        private byte[] id3v1Block = null;
        public bool HasId3v1 { get { return id3v1Block != null; } }
        public bool HasId3v1Phantom { get { return excess != null; } }

        public int ApeSize { get; private set; }
        public bool HasApe { get { return ApeSize > 0; } }
        public int Lyrics3Size { get; private set; }
        public bool HasLyrics3 { get { return Lyrics3Size > 0; } }

        public int DeadBytes { get; private set; }


        private Mp3Format (Stream stream, byte[] hdr, string path) : base (stream, path)
        { }


        public override bool IsBadHeader
        { get { return IsLame && Lame.ActualAudioHeaderCRC16 != null && Lame.ActualAudioHeaderCRC16 != Lame.StoredAudioHeaderCRC16; } }


        public override bool IsBadData
        { get { return IsLame && Lame.ActualAudioDataCRC16 != null && Lame.ActualAudioDataCRC16 != Lame.StoredAudioDataCRC16; } }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (Issues.HasFatal)
                return;

            if (scope <= Granularity.Detail)
            {
                report.Add ("MPEG size = " + MediaCount);
                if (Xing != null)
                    report.Add ("XING size = " + Xing.XingSize);
                if (Lame != null)
                    report.Add ("LAME size = " + Lame.LameSize);

                report.Add (String.Empty);

                report.Add ("Raw audio header:" + "  " + ConvertTo.ToBinaryString (aHdr, 4));
                report.Add (String.Empty);
            }

            report.Add ("Cooked audio header:");
            report.Add ("  Codec = " + Codec);

            if (scope <= Granularity.Detail)
                report.Add ("  Flagged for CRC protection = " + IsProtected);
            report.Add ("  Bit rate = " + BitRateToString());
            report.Add ("  Frequency = " + FrequencyToString());
            if (scope <= Granularity.Detail)
            {
                report.Add ("  Frame padded = " + IsFramePadded);
                report.Add ("  Private = " + PrivateBit);
            }
            report.Add ("  Mode = " + ModeText);
            if (scope <= Granularity.Detail)
            {
                report.Add ("  Copyrighted = " + IsCopyrighted);
                report.Add ("  Original = " + IsOriginal);
                report.Add ("  Emphasis = " + Emphasis);
            }

            if (Xing != null)
            {
                if (scope <= Granularity.Detail)
                    report.Add (String.Empty);

                report.Add ("LAME:");
                if (scope <= Granularity.Detail)
                {
                    var opts = "  Optionals layout = |";
                    if (Lame.IsFrameCountPresent)
                        opts += " FrameCount |";
                    if (Lame.IsBytesPresent)
                        opts += " Bytes |";
                    if (Lame.IsTableOfContentsPresent)
                        opts += " ToC |";
                    if (Lame.IsQualityIndicatorPresent)
                        opts += " QI |";
                    report.Add (opts);
                }
            }
            if (Lame != null)
            {
                report.Add ("  Version string = " + Lame.Version);
                if (Lame.IsVbr)
                { report.Add ("  VBR: min bit rate = " + BitRate + ", method = " + Lame.BitrateMethod); }
                if (Lame.IsAbr)
                { report.Add ("  ABR: bit rate = " + BitRate + ", method = " + Lame.BitrateMethod); }
                if (Lame.IsCbr)
                { report.Add ("  CBR: bit rate = " + BitRate + ", method = " + Lame.BitrateMethod); }

                if (scope <= Granularity.Detail)
                {
                    report.Add ("  Tag revision = " + Lame.TagRevision);
                    report.Add ("  Lowpass filter = " + Lame.LowpassFilter);
                    report.Add (String.Format ("  Replay Gain: Peak = {0}, Radio = {1:X4}, Audiophile = {2:X4}",
                                        Lame.ReplayGainPeak, Lame.RadioReplayGain, Lame.AudiophileReplayGain));
                    report.Add ("  Lame encoding flags = " + Convert.ToString (Lame.LameFlags, 2).PadLeft (8, '0'));
                    report.Add (String.Format ("  Encoder delay start = {0}, end = {1}", Lame.EncoderDelayStart, Lame.EncoderDelayEnd));
                    report.Add (String.Format ("  LAME surround = {0}, LAME preset = {1}", Lame.Surround, Lame.Preset));
                    report.Add ("  MP3 gain = " + Lame.Mp3Gain.ToString());
                    report.Add ("  Minimum bit rate = " + Lame.MinBitRate);
                    report.Add ("  Checks:");
                    report.Add ("    Stored: audio header CRC-16 = " + Lame.StoredAudioHeaderCRC16ToHex
                                          + ", audio data CRC-16 = " + Lame.StoredAudioDataCRC16ToHex);

                    string lx = "    Actual: audio header CRC-16 = " + Lame.ActualAudioHeaderCRC16ToHex;
                    if (Lame.ActualAudioDataCRC16 >= 0)
                        lx += ", audio data CRC-16 = " + Lame.ActualAudioDataCRC16ToHex;
                    report.Add (lx);
                }
            }

            var sb = new StringBuilder();
            sb.Append ("Layout = |");
            if (HasId3v2)
            { sb.Append (" ID3v2."); sb.Append (Id3v2Major); sb.Append (" ("); sb.Append (Id3v2Size); sb.Append (") |"); }
            sb.Append (" Audio (");
            if (DeadBytes > 0)
            { sb.Append (" Dead ("); sb.Append (DeadBytes); sb.Append (") |"); }
            sb.Append (MediaCount.ToString());
            sb.Append (") |");
            if (HasApe)
            { sb.Append (" APE tag ("); sb.Append (ApeSize); sb.Append (") |"); }
            if (HasLyrics3)
            { sb.Append (" Lyrics3v2 ("); sb.Append (Lyrics3Size); sb.Append (") |"); }
            if (HasId3v1Phantom)
                sb.Append (" ID3v1 (128) |");
            if (HasId3v1)
            { sb.Append (" ID3v1."); sb.Append (GetMinorOfV1 (id3v1Block)); sb.Append (" (128) |"); }
            if (scope <= Granularity.Detail)
                report.Add (String.Empty);
            report.Add (sb.ToString());
        }
    }
}

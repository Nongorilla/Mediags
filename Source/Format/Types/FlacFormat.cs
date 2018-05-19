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
    // xiph.org/flac/format.html
    public partial class FlacFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "flac" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 12
                    && hdr[0]=='f' && hdr[1]=='L' && hdr[2]=='a' && hdr[3]=='C' && hdr[4]==0)
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public readonly FlacFormat Bind;

            public Model (Stream stream, byte[] hdr, string path)
            {
                BaseBind = Bind = new FlacFormat (stream, path);
                Bind.Issues = IssueModel.Bind;

                Bind.MetadataBlockStreamInfoSize = ConvertTo.FromBig24ToInt32 (hdr, 5);
                if (Bind.MetadataBlockStreamInfoSize < 34)
                {
                    IssueModel.Add ("Bad metablock size of " + Bind.MetadataBlockStreamInfoSize, Severity.Fatal);
                    return;
                }

                var bb = new byte[Bind.MetadataBlockStreamInfoSize];
                Bind.ValidSize = 8;

                Bind.fbs.Position = Bind.ValidSize;
                var got = Bind.fbs.Read (bb, 0, Bind.MetadataBlockStreamInfoSize);
                if (got != Bind.MetadataBlockStreamInfoSize)
                {
                    IssueModel.Add ("File truncated", Severity.Fatal);
                    return;
                }

                Bind.MinBlockSize = ConvertTo.FromBig16ToInt32 (bb, 0);
                Bind.MinBlockSize = ConvertTo.FromBig16ToInt32 (bb, 2);
                Bind.MinFrameSize = ConvertTo.FromBig24ToInt32 (bb, 4);
                Bind.MaxFrameSize = ConvertTo.FromBig24ToInt32 (bb, 7);
            
                Bind.MetaSampleRate = bb[10] << 12 | bb[11] << 4 | bb[12] >> 4;
                Bind.ChannelCount = ((bb[12] & 0x0E) >> 1) + 1;
                Bind.BitsPerSample = (((bb[12] & 1) << 4) | (bb[13] >> 4)) + 1;
                Bind.TotalSamples = ((bb[13] & 0x0F) << 32) | bb[14] << 24 | bb[15] << 16 | bb[16] << 8 | bb[17];

                Bind.mHdr = bb;
                Bind.storedAudioDataMD5 = new byte[16];
                Array.Copy (bb, 18, Bind.storedAudioDataMD5, 0, 16);

                Bind.ValidSize += Bind.MetadataBlockStreamInfoSize;

                for (;;)
                {
                    bb = new byte[12];
                    try
                    {
                        Bind.fbs.Position = Bind.ValidSize;
                    }
                    catch (EndOfStreamException)
                    {
                        IssueModel.Add ("File truncated near meta data", Severity.Fatal);
                        return;
                    }

                    Bind.fbs.Position = Bind.ValidSize;
                    got = Bind.fbs.Read (bb, 0, 4);
                    if (got != 4)
                    {
                        IssueModel.Add ("File truncated near meta data", Severity.Fatal);
                        return;
                    }

                    var blockSize = ConvertTo.FromBig24ToInt32 (bb, 1);
                    Bind.ValidSize += 4;

                    switch ((FlacBlockType) (bb[0] & 0x7F))
                    {
                        case FlacBlockType.Padding:
                            Bind.Blocks.AddPad (blockSize);
                            break;
                        case FlacBlockType.Application:
                            got = Bind.fbs.Read (bb, 0, 4);
                            if (got != 4)
                            {
                                IssueModel.Add ("File truncated near tags", Severity.Fatal);
                                return;
                            }
                            int appId = ConvertTo.FromBig32ToInt32 (bb, 0);
                            Bind.Blocks.AddApp (blockSize, appId);
                            break;
                        case FlacBlockType.SeekTable:
                            var st = new byte[blockSize];
                            got = Bind.fbs.Read (st, 0, blockSize);
                            if (got != blockSize)
                            {
                                IssueModel.Add ("File truncated near seek table", Severity.Fatal);
                                return;
                            }
                            Bind.Blocks.AddSeekTable (blockSize, st);
                            break;
                        case FlacBlockType.Tags:
                            bb = new byte[blockSize];
                            Bind.fbs.Position = Bind.ValidSize;
                            got = Bind.fbs.Read (bb, 0, blockSize);
                            if (got != blockSize)
                            {
                                IssueModel.Add ("File truncated near tags", Severity.Fatal);
                                return;
                            }
                            if (Bind.Blocks.Tags != null)
                                IssueModel.Add ("Contains multiple tag blocks", Severity.Error);
                            else
                                Bind.Blocks.AddTags (blockSize, bb);
                            break;
                        case FlacBlockType.CueSheet:
                            var sb = new byte[284];
                            got = Bind.fbs.Read (sb, 0, 284);
                            if (got != 284)
                            {
                                IssueModel.Add ("File truncated near cuesheet", Severity.Fatal);
                                return;
                            }
                            var isCD = (sb[24] & 0x80) != 0;
                            int trackCount = sb[283];
                            Bind.Blocks.AddCuesheet (blockSize, isCD, trackCount);
                            break;
                        case FlacBlockType.Picture:
                            var pb = new byte[blockSize];
                            got = Bind.fbs.Read (pb, 0, blockSize);
                            if (got != blockSize)
                            {
                                IssueModel.Add ("File truncated near picture", Severity.Fatal);
                                return;
                            }
                            var picType = (PicType) ConvertTo.FromBig32ToInt32 (pb, 0);
                            var mimeLen = ConvertTo.FromBig32ToInt32 (pb, 4);
                            var mime = Encoding.UTF8.GetString (pb, 8, mimeLen);
                            var descLen = ConvertTo.FromBig32ToInt32 (pb, mimeLen + 8);
                            var desc = Encoding.UTF8.GetString (pb, mimeLen+12, descLen);
                            var width = ConvertTo.FromBig32ToInt32 (pb, mimeLen + descLen + 12);
                            var height = ConvertTo.FromBig32ToInt32 (pb, mimeLen + descLen + 16);
                            Bind.Blocks.AddPic (blockSize, picType, width, height);
                            break;
                        default:
                            IssueModel.Add ("Encountered unexpected metadata block type of " + (bb[0] & 0x7F), Severity.Fatal);
                            return;
                    }

                    Bind.ValidSize += blockSize;
                    if ((bb[0] & 0x80) != 0)
                        break;
                }

                try
                {
                    Bind.fbs.Position = Bind.ValidSize;
                }
                catch (EndOfStreamException)
                {
                    IssueModel.Add ("File truncated near frame header", Severity.Fatal);
                    return;
                }
                got = Bind.fbs.Read (bb, 0, 4);
                if (got != 4)
                {
                    IssueModel.Add ("File truncated", Severity.Fatal);
                    return;
                }

                // Detect frame header sync code
                if (bb[0] != 0xFF || (bb[1] & 0xFC) != 0xF8)
                {
                    IssueModel.Add ("Audio data not found", Severity.Fatal);
                    return;
                }

                Bind.mediaPosition = Bind.ValidSize;

                Bind.SampleOrFrameNumber = Bind.fbs.ReadWobbly (out byte[] wtfBuf);
                if (Bind.SampleOrFrameNumber < 0)
                {
                    IssueModel.Add ("File truncated or badly formed sample/frame number.", Severity.Fatal);
                    return;
                }
                Array.Copy (wtfBuf, 0, bb, 4, wtfBuf.Length);
                int bPos = 4 + wtfBuf.Length;

                Bind.RawBlockingStrategy = bb[1] & 1;

                Bind.RawBlockSize = bb[2] >> 4;
                if (Bind.RawBlockSize == 0)
                    Bind.BlockSize = 0;
                else if (Bind.RawBlockSize == 1)
                    Bind.BlockSize = 192;
                else if (Bind.RawBlockSize >= 2 && Bind.RawBlockSize <= 5)
                    Bind.BlockSize = 576 * (1 << (Bind.RawBlockSize - 2));
                else if (Bind.RawBlockSize == 6)
                {
                    got = Bind.fbs.Read (bb, bPos, 1);
                    Bind.BlockSize = bb[bPos] + 1;
                    bPos += 1;
                }
                else if (Bind.RawBlockSize == 7)
                {
                    got = Bind.fbs.Read (bb, bPos, 2);
                    Bind.BlockSize = (bb[bPos]<<8) + bb[bPos+1] + 1;
                    bPos += 2;
                }
                else
                    Bind.BlockSize = 256 * (1 << (Bind.RawBlockSize - 8));


                Bind.RawSampleRate = bb[2] & 0xF;
                if (Bind.RawSampleRate == 0xC)
                {
                    got = Bind.fbs.Read (bb, bPos, 1);
                    Bind.SampleRateText = bb[bPos] + "kHz";
                    bPos += 1;
                }
                else if (Bind.RawSampleRate == 0xD || Bind.RawSampleRate == 0xE)
                {
                    got = Bind.fbs.Read (bb, bPos, 2);
                    Bind.SampleRateText = (bb[bPos]<<8).ToString() + bb[bPos+1] + (Bind.RawSampleRate == 0xD? " Hz" : " kHz");
                    bPos += 2;
                }
                else if (Bind.RawSampleRate == 0)
                    Bind.SampleRateText = Bind.MetaSampleRate.ToString() + " Hz";
                else
                    Bind.SampleRateText = SampleRateMap[Bind.RawSampleRate];

                Bind.RawChannelAssignment = bb[3] >> 4;

                Bind.RawSampleSize = (bb[3] & 0xE) >> 1;
                if (Bind.RawSampleSize == 0)
                    Bind.SampleSizeText = Bind.BitsPerSample.ToString() + " bits";
                else
                    Bind.SampleSizeText = SampleSizeMap[Bind.RawSampleSize];

                Bind.aHdr = new byte[bPos];
                Array.Copy (bb, Bind.aHdr, bPos);

                Bind.ValidSize += bPos;
                Bind.fbs.Position = Bind.ValidSize;
                int octet = Bind.fbs.ReadByte();
                if (octet < 0)
                {
                    IssueModel.Add ("File truncated near CRC-8", Severity.Fatal);
                    return;
                }
                Bind.StoredAudioHeaderCRC8 = (Byte) octet;

                try
                {
                    Bind.fbs.Position = Bind.mediaPosition;
                }
                catch (EndOfStreamException)
                {
                    IssueModel.Add ("File truncated near audio data", Severity.Fatal);
                    return;
                }

                try
                {
                    Bind.fbs.Position = Bind.FileSize - 2;
                }
                catch (EndOfStreamException)
                {
                    IssueModel.Add ("File truncated looking for end", Severity.Fatal);
                    return;
                }

                bb = new byte[2];
                if (Bind.fbs.Read (bb, 0, 2) != 2)
                {
                    IssueModel.Add ("Read failed on audio block CRC-16", Severity.Fatal);
                    return;
                }

                Bind.StoredAudioBlockCRC16 = (UInt16) (bb[0] << 8 | bb[1]);
                Bind.MediaCount = Bind.FileSize - Bind.mediaPosition;

                GetDiagnostics ();
            }

            private void GetDiagnostics()
            {
                if (Bind.MetadataBlockStreamInfoSize != 0x22)
                    IssueModel.Add ("Unexpected Metadata block size of " + Bind.MetadataBlockStreamInfoSize, Severity.Advisory);

                if (Bind.MinBlockSize < 16)
                    IssueModel.Add ("Minimum block size too low", Severity.Error);

                if (Bind.MinBlockSize > 65535)
                    IssueModel.Add ("Maximum block size too high", Severity.Error);

                if (Bind.RawSampleRate == 0xF)
                    IssueModel.Add ("Invalid sample rate", Severity.Error);

                if (Bind.RawSampleSize == 3 || Bind.RawSampleSize == 7)
                    IssueModel.Add ("Use of sample size index " + Bind.RawSampleSize + " is reserved", Severity.Error);

                if (Bind.RawChannelAssignment >= 0xB)
                    IssueModel.Add ("Use of reserved (undefined) channel assignment " + Bind.RawChannelAssignment, Severity.Warning);

                if (Bind.RawBlockSize == 0)
                    IssueModel.Add ("Block size index 0 use is reserved", Severity.Warning);

                if (Bind.RawSampleRate == 0xF)
                    IssueModel.Add ("Sample rate index 15 use is invalid", Severity.Error);

                if (Bind.RawChannelAssignment >= 0xB)
                    IssueModel.Add ("Channel index " + Bind.RawChannelAssignment + " use is reserved", Severity.Warning);

                if (Bind.Blocks.Tags.Lines.Count != Bind.Blocks.Tags.StoredTagCount)
                    IssueModel.Add ("Stored tag count wrong");

                foreach (var lx in Bind.Blocks.Tags.Lines)
                {
                    if (lx.IndexOf ('=') < 0)
                        IssueModel.Add ("Invalid tag line: " + lx);

                    // U+FFFD is substituted by .NET when malformed utf8 encountered.
                    if (lx.Contains ('\uFFFD'))
                        IssueModel.Add ("Tag with malformed UTF-8 character encoding: " + lx);

                    if (lx.Any (cu => Char.IsSurrogate (cu)))
                        IssueModel.Add ("Tag contains character(s) beyond the basic multilingual plane (may cause player issues): " + lx, Severity.Trivia);
                }

                int picPlusPadSize = 0;
                foreach (FlacBlockItem block in Bind.Blocks.Items)
                    if (block.BlockType == FlacBlockType.Padding || block.BlockType == FlacBlockType.Picture)
                        picPlusPadSize += block.Size;

                if (picPlusPadSize > 512*1024)
                    IssueModel.Add ("Artwork plus padding consume " + picPlusPadSize + " bytes.", Severity.Trivia, IssueTags.Fussy);
            }


#if ! NETFX_CORE
            public static bool ApplyReplayGain (IList<FileInfo> flacs)
            {
                var sb = new StringBuilder ("--add-replay-gain");
                foreach (var flac in flacs)
                {
                    sb.Append (' ');
                    sb.Append ('"');
                    sb.Append (flac.FullName);
                    sb.Append ('"');
                }

                var px = new Process();
                px.StartInfo.UseShellExecute = false;
                px.StartInfo.Arguments = sb.ToString();
                px.StartInfo.FileName = "metaflac";
                var isGo = px.Start();
                px.WaitForExit();
                return isGo;
            }


            private static Process StartFlac (string name)
            {
                var px = new Process();
                px.StartInfo.UseShellExecute = false;
                px.StartInfo.RedirectStandardOutput = true;
                px.StartInfo.CreateNoWindow = true;
                px.StartInfo.Arguments = "-d -c -f --totally-silent --force-raw-format --endian=little --sign=signed " + '"' + name + '"';
                px.StartInfo.FileName = "flac";
                var isGo = px.Start();
                return px;
            }
#endif


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Bind.Issues.HasFatal)
                    return;

                if ((hashFlags & Hashes.Intrinsic) != 0)
                {
                    if (Bind.ActualAudioHeaderCRC8 == null)
                    {
                        var hasher = new Crc8Hasher();
                        hasher.Append (Bind.aHdr);
                        var hash = hasher.GetHashAndReset();
                        Bind.ActualAudioHeaderCRC8 = hash[0];

                        if (Bind.IsBadHeader)
                            IssueModel.Add ("CRC-8 check failed on audio header.");
                    }

                    if (Bind.ActualAudioBlockCRC16 == null)
                        try
                        {
                            var hasher = new Crc16nHasher();
                            hasher.Append (Bind.fbs, Bind.mediaPosition, Bind.FileSize - Bind.mediaPosition - 2);
                            var hash = hasher.GetHashAndReset();
                            Bind.ActualAudioBlockCRC16 = BitConverter.ToUInt16 (hash, 0);
                        }
                        catch (EndOfStreamException ex)
                        {
                            IssueModel.Add ("Read failed while verifying audio CRC: " + ex.Message, Severity.Fatal);
                            return;
                        }

                        if (Bind.IsBadDataCRC16)
                            IssueModel.Add ("CRC-16 check failed on audio data.");
                        else
                            if (Bind.IsBadHeader)
                                IssueModel.Add ("CRC-16 check successful.", Severity.Advisory);
                            else
                                IssueModel.Add ("CRC-8, CRC-16 checks successful.", Severity.Noise);
                }

#if ! NETFX_CORE
                if ((hashFlags & Hashes.PcmMD5) != 0 && Bind.actualAudioDataMD5 == null)
                {
                    Process px = null;
                    try
                    { px = StartFlac (Bind.Path); }
                    catch (Exception ex)
                    { IssueModel.Add ("flac executable failed with '" + ex.Message.Trim (null) + "'."); }

                    if (px != null)
                        using (var br = new BinaryReader (px.StandardOutput.BaseStream))
                        {
                            try
                            {
                                var hasher = new Md5Hasher();
                                hasher.Append (br);
                                var hash = hasher.GetHashAndReset();
                                Bind.actualAudioDataMD5 = hash;
                            }
                            catch (EndOfStreamException ex)
                            { IssueModel.Add ("Read failed while verifying audio MD5: " + ex.Message, Severity.Fatal); }

                            if (Bind.IsBadDataMD5)
                                IssueModel.Add ("MD5 check failed on audio data.");
                            else
                                IssueModel.Add ("MD5 check successful.", Severity.Noise);
                        }
                }

                if ((hashFlags & Hashes.PcmCRC32) != 0 && Bind.ActualPcmCRC32 == null)
                {
                    Process px = null;
                    try
                    { px = StartFlac (Bind.Path); }
                    catch (Exception ex)
                    { IssueModel.Add ("flac executable failed with '" + ex.Message.Trim (null) + "'."); }
                
                    if (px != null)
                        using (var br = new BinaryReader (px.StandardOutput.BaseStream))
                        {
                            var hasher = new Crc32rHasher();
                            hasher.Append (br);
                            var hash = hasher.GetHashAndReset();
                            Bind.ActualPcmCRC32 = BitConverter.ToUInt32 (hash, 0);
                        }
                }
#endif
                base.CalcHashes (hashFlags, validationFlags);
            }
        }


        private static string[] SampleRateMap =
        { "g0000", "88.2kHz", "176.4kHz", "192kHz", "8kHz", "16kHz", "22.05kHz", "24kHz",
          "32kHz", "44.1kHz", "48kHz", "96kHz", "g1100", "g1101", "g1110", "invalid" };

        private static string[] SampleSizeMap =
        { "getIt", "8 bits", "12 bits", "reserved",
          "16 bits", "20 bits", "24 bits", "reserved" };

        private static string[] ChannelAssignmentMap =
        {
            "mono", "L,R", "L,R,C", "FL,FR,BL,BR", "FL,FR,FC,BL,BR", "FL,FR,FC,LFE,BL,BR",
            "FL,FR,FC,LFE,BC,SL,SR", "FL,FR,FC,LFE,BL,BR,SL,SR",
            "left/side stereo", "right/side stereo", "mid/side stereo",
            "R1011", "R1100", "R1101", "R1111"
        };

        private byte[] mHdr = null;
        private byte[] aHdr = null;

        public readonly FlacBlockList Blocks = new FlacBlockList();

        public int MetadataBlockStreamInfoSize { get; private set; }
        public int MinBlockSize { get; private set; }
        public int MaxBlockSize { get; private set; }
        public int MinFrameSize { get; private set; }
        public int MaxFrameSize { get; private set; }
        public int MetaSampleRate { get; private set; }
        public int ChannelCount { get; private set; }
        public int BitsPerSample { get; private set; }
        public long TotalSamples { get; private set; }
        public long SampleOrFrameNumber { get; private set; }

        public int RawBlockingStrategy { get; private set; }
        public string BlockingStrategyText { get { return (RawBlockingStrategy == 0? "Fixed" : "Variable") + " size"; } }
        public int RawBlockSize { get; private set; }
        public int BlockSize { get; private set; }
        public int RawSampleRate { get; private set; }
        public string SampleRateText { get; private set; }
        public int RawChannelAssignment { get; private set; }
        public string ChannelAssignmentText { get { return ChannelAssignmentMap[RawChannelAssignment]; } }
        public int RawSampleSize { get; private set; }
        public string SampleSizeText { get; private set; }

        public Byte StoredAudioHeaderCRC8 { get; private set; }
        public Byte? ActualAudioHeaderCRC8 { get; private set; }
        public string StoredAudioHeaderCRC8ToHex { get { return StoredAudioHeaderCRC8.ToString ("X2"); } }
        public string ActualAudioHeaderCRC8ToHex
        { get { return ActualAudioHeaderCRC8?.ToString ("X2"); } }

        public UInt16 StoredAudioBlockCRC16 { get; private set; }
        public UInt16? ActualAudioBlockCRC16 { get; private set; }
        public string StoredAudioBlockCRC16ToHex { get { return StoredAudioBlockCRC16.ToString ("X4"); } }
        public string ActualAudioBlockCRC16ToHex
        { get { return ActualAudioBlockCRC16?.ToString ("X4"); } }

        private byte[] storedAudioDataMD5 = null;
        private byte[] actualAudioDataMD5 = null;
        public string StoredAudioDataMD5ToHex { get { return storedAudioDataMD5==null? null : ConvertTo.ToHexString (storedAudioDataMD5); } }
        public string ActualAudioDataMD5ToHex { get { return actualAudioDataMD5==null? null : ConvertTo.ToHexString (actualAudioDataMD5); } }

        public UInt32? ActualPcmCRC32 { get; private set; }
        public string ActualPcmCRC32ToHex
        { get { return ActualPcmCRC32?.ToString ("X8"); } }

        public bool IsBadDataCRC16 { get { return ActualAudioBlockCRC16 != null && ActualAudioBlockCRC16.Value != StoredAudioBlockCRC16; } }
        public bool IsBadDataMD5 { get { return actualAudioDataMD5 != null && ! actualAudioDataMD5.SequenceEqual (storedAudioDataMD5); } }


        private FlacFormat (Stream stream, string path) : base (stream, path)
        { }


        public override bool IsBadHeader
        {
            get { return ActualAudioHeaderCRC8 != null && StoredAudioHeaderCRC8 != ActualAudioHeaderCRC8.Value; }
        }


        public override bool IsBadData
        {
            get { return IsBadDataCRC16 || IsBadDataMD5; }
        }


        public string GetTag (string name)
        {
            name = name.ToLower() + "=";
            foreach (var item in Blocks.Tags.Lines)
                if (item.ToLower().StartsWith (name))
                    return item.Substring (name.Length);
            return String.Empty;
        }


        public string GetMultiTag (string name)
        {
            string result = null;
            name = name.ToLower() + "=";
            foreach (var item in Blocks.Tags.Lines)
                if (item.ToLower().StartsWith (name))
                    if (result == null)
                        result = item.Substring (name.Length);
                    else
                        result += @"\\" + item.Substring (name.Length);
            return result;
        }


        public string GetCleanFileName (NamingStrategy strategy, string calcedAlbumArtist, int trackWidth)
        {
            System.Diagnostics.Debug.Assert (strategy == NamingStrategy.Manual || trackWidth > 0);

            string dirtyName;
            bool isVarious = calcedAlbumArtist == null || (calcedAlbumArtist.ToLower()+' ').StartsWith("various ");

            var track = GetTag ("TRACKNUMBER");
            var padding = trackWidth - track.Length;
            if (padding > 0)
                track = new String ('0', padding) + track;

            var artist = GetTag ("ARTIST");
            if (String.IsNullOrEmpty (artist))
            {
                artist = GetTag ("COMPOSER");
                if (String.IsNullOrEmpty (artist))
                    artist = "(NoArtist)";
            }

            var title = GetTag ("TITLE");
            if (String.IsNullOrEmpty (title))
                title = "(NoTitle)";

            switch (strategy)
            {
                case NamingStrategy.ArtistTitle:
                    dirtyName = track + " - " + artist + " - " + title + ".flac";
                    break;

                case NamingStrategy.ShortTitle:
                case NamingStrategy.UnloadedAlbum:
                    if (isVarious || artist != calcedAlbumArtist)
                        dirtyName = track + " - " + artist + " - " + title + ".flac";
                    else
                        dirtyName = track + " - " + title + ".flac";
                    break;

                default:
                    dirtyName = Name;
                    break;
            }

            return Map1252.ToClean1252FileName (dirtyName);
        }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (report.Count > 0 && scope <= Granularity.Detail)
                report.Add (String.Empty);

            report.Add ("Meta header:");
            if (scope <= Granularity.Detail)
            {
                report.Add ("  Minimum block size = " + MinBlockSize);
                report.Add ("  Maximum block size = " + MaxBlockSize);
                report.Add ("  Minimum frame size = " + MinFrameSize);
                report.Add ("  Maximum frame size = " + MaxFrameSize);
            }

            report.Add ("  Sample rate = " + MetaSampleRate + " Hz");
            report.Add ("  Number of channels = " + ChannelCount);
            report.Add ("  Bits per sample = " + BitsPerSample);

            if (scope <= Granularity.Detail)
            {
                report.Add ("  Total samples = " + (TotalSamples != 0? TotalSamples.ToString() : " (unknown)"));

                report.Add (String.Empty);
                report.Add ("Raw audio header:" + "  " + ConvertTo.ToBinaryString (aHdr, 1));

                report.Add (String.Empty);
                report.Add ("Cooked audio header:");
                report.Add ("  Blocking strategy = " + BlockingStrategyText);
                report.Add ("  Block size = " + BlockSize + " samples");
                report.Add ("  Sample rate = " + SampleRateText);
                report.Add ("  Channel assignment = " + ChannelAssignmentText);
                report.Add ("  Sample size = " + SampleSizeText);
                report.Add ("  Sample/frame number = " + SampleOrFrameNumber);

                report.Add (String.Empty);
                report.Add ("Checks:");

                report.Add ("  Stored audio header CRC-8 = " + StoredAudioHeaderCRC8ToHex);
                if (ActualAudioHeaderCRC8 != null)
                    report.Add ("  Actual audio header CRC-8 = " + ActualAudioHeaderCRC8ToHex);

                report.Add ("  Stored audio block CRC-16 = " + StoredAudioBlockCRC16ToHex);
                if (ActualAudioBlockCRC16 != null)
                    report.Add ("  Actual audio block CRC-16 = " + ActualAudioBlockCRC16ToHex);

                report.Add ("  Stored PCM MD5 = " + StoredAudioDataMD5ToHex);
                if (actualAudioDataMD5 != null)
                    report.Add ("  Actual PCM MD5 = " + ActualAudioDataMD5ToHex);

                if (ActualPcmCRC32 != null)
                    report.Add ("  Actual PCM CRC-32 = " + ActualPcmCRC32ToHex);
            }

            var sb = new StringBuilder();
            sb.Append ("Layout = |");
            foreach (var item in Blocks.Items)
            {
                sb.Append (' ');
                sb.Append (item.Name);
                sb.Append (" (");
                sb.Append (item.Size);
                sb.Append (") |");
            }

            if (aHdr != null)
            {
                sb.Append (" Audio (");
                sb.Append (MediaCount);
                sb.Append (") |");
            }

            if (scope <= Granularity.Detail)
                report.Add (String.Empty);
            report.Add (sb.ToString());

            if (scope <= Granularity.Detail && Blocks.Tags != null)
            {
                report.Add (String.Empty);
                report.Add ("Tags:");
                report.Add ("  Vendor: " + Blocks.Tags.Vendor);
                foreach (var item in Blocks.Tags.Lines)
                    report.Add ("  " + item);
            }
        }
    }
}

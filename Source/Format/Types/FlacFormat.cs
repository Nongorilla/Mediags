using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NongIssue;
using NongCrypto;

namespace NongFormat
{
    // xiph.org/flac/format.html
    public partial class FlacFormat : FormatBase
    {
        public static string[] Names
         => new string[] { "flac" };

        public override string[] ValidNames
         => Names;

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 12
                    && hdr[0]=='f' && hdr[1]=='L' && hdr[2]=='a' && hdr[3]=='C' && hdr[4]==0)
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : FormatBase.Model
        {
            public new readonly FlacFormat Data;

            public Model (Stream stream, byte[] hdr, string path)
            {
                base._data = Data = new FlacFormat (this, stream, path);

                Data.MetadataBlockStreamInfoSize = ConvertTo.FromBig24ToInt32 (hdr, 5);
                if (Data.MetadataBlockStreamInfoSize < 34)
                {
                    IssueModel.Add ($"Bad metablock size of {Data.MetadataBlockStreamInfoSize}", Severity.Fatal);
                    return;
                }

                var bb = new byte[Data.MetadataBlockStreamInfoSize];
                Data.ValidSize = 8;

                Data.fbs.Position = Data.ValidSize;
                var got = Data.fbs.Read (bb, 0, Data.MetadataBlockStreamInfoSize);
                if (got != Data.MetadataBlockStreamInfoSize)
                {
                    IssueModel.Add ("File truncated", Severity.Fatal);
                    return;
                }

                Data.MinBlockSize = ConvertTo.FromBig16ToInt32 (bb, 0);
                Data.MinBlockSize = ConvertTo.FromBig16ToInt32 (bb, 2);
                Data.MinFrameSize = ConvertTo.FromBig24ToInt32 (bb, 4);
                Data.MaxFrameSize = ConvertTo.FromBig24ToInt32 (bb, 7);
            
                Data.MetaSampleRate = bb[10] << 12 | bb[11] << 4 | bb[12] >> 4;
                Data.ChannelCount = ((bb[12] & 0x0E) >> 1) + 1;
                Data.BitsPerSample = (((bb[12] & 1) << 4) | (bb[13] >> 4)) + 1;
                Data.TotalSamples = ((bb[13] & 0x0F) << 32) | bb[14] << 24 | bb[15] << 16 | bb[16] << 8 | bb[17];

                Data.mHdr = bb;
                Data.storedAudioDataMD5 = new byte[16];
                Array.Copy (bb, 18, Data.storedAudioDataMD5, 0, 16);

                Data.ValidSize += Data.MetadataBlockStreamInfoSize;

                for (;;)
                {
                    bb = new byte[12];
                    try
                    {
                        Data.fbs.Position = Data.ValidSize;
                    }
                    catch (EndOfStreamException)
                    {
                        IssueModel.Add ("File truncated near meta data", Severity.Fatal);
                        return;
                    }

                    Data.fbs.Position = Data.ValidSize;
                    got = Data.fbs.Read (bb, 0, 4);
                    if (got != 4)
                    {
                        IssueModel.Add ("File truncated near meta data", Severity.Fatal);
                        return;
                    }

                    var blockSize = ConvertTo.FromBig24ToInt32 (bb, 1);
                    Data.ValidSize += 4;

                    switch ((FlacBlockType) (bb[0] & 0x7F))
                    {
                        case FlacBlockType.Padding:
                            Data.Blocks.AddPad (blockSize);
                            break;
                        case FlacBlockType.Application:
                            got = Data.fbs.Read (bb, 0, 4);
                            if (got != 4)
                            {
                                IssueModel.Add ("File truncated near tags", Severity.Fatal);
                                return;
                            }
                            int appId = ConvertTo.FromBig32ToInt32 (bb, 0);
                            Data.Blocks.AddApp (blockSize, appId);
                            break;
                        case FlacBlockType.SeekTable:
                            var st = new byte[blockSize];
                            got = Data.fbs.Read (st, 0, blockSize);
                            if (got != blockSize)
                            {
                                IssueModel.Add ("File truncated near seek table", Severity.Fatal);
                                return;
                            }
                            Data.Blocks.AddSeekTable (blockSize, st);
                            break;
                        case FlacBlockType.Tags:
                            bb = new byte[blockSize];
                            Data.fbs.Position = Data.ValidSize;
                            got = Data.fbs.Read (bb, 0, blockSize);
                            if (got != blockSize)
                            {
                                IssueModel.Add ("File truncated near tags", Severity.Fatal);
                                return;
                            }
                            if (Data.Blocks.Tags != null)
                                IssueModel.Add ("Contains multiple tag blocks", Severity.Error);
                            else
                                Data.Blocks.AddTags (blockSize, bb);
                            break;
                        case FlacBlockType.CueSheet:
                            var sb = new byte[284];
                            got = Data.fbs.Read (sb, 0, 284);
                            if (got != 284)
                            {
                                IssueModel.Add ("File truncated near cuesheet", Severity.Fatal);
                                return;
                            }
                            var isCD = (sb[24] & 0x80) != 0;
                            int trackCount = sb[283];
                            Data.Blocks.AddCuesheet (blockSize, isCD, trackCount);
                            break;
                        case FlacBlockType.Picture:
                            var pb = new byte[blockSize];
                            got = Data.fbs.Read (pb, 0, blockSize);
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
                            Data.Blocks.AddPic (blockSize, picType, width, height);
                            break;
                        default:
                            IssueModel.Add ("Encountered unexpected metadata block type of " + (bb[0] & 0x7F), Severity.Fatal);
                            return;
                    }

                    Data.ValidSize += blockSize;
                    if ((bb[0] & 0x80) != 0)
                        break;
                }

                try
                {
                    Data.fbs.Position = Data.ValidSize;
                }
                catch (EndOfStreamException)
                {
                    IssueModel.Add ("File truncated near frame header", Severity.Fatal);
                    return;
                }
                got = Data.fbs.Read (bb, 0, 4);
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

                Data.mediaPosition = Data.ValidSize;

                Data.SampleOrFrameNumber = Data.fbs.ReadWobbly (out byte[] wtfBuf);
                if (Data.SampleOrFrameNumber < 0)
                {
                    IssueModel.Add ("File truncated or badly formed sample/frame number.", Severity.Fatal);
                    return;
                }
                Array.Copy (wtfBuf, 0, bb, 4, wtfBuf.Length);
                int bPos = 4 + wtfBuf.Length;

                Data.RawBlockingStrategy = bb[1] & 1;

                Data.RawBlockSize = bb[2] >> 4;
                if (Data.RawBlockSize == 0)
                    Data.BlockSize = 0;
                else if (Data.RawBlockSize == 1)
                    Data.BlockSize = 192;
                else if (Data.RawBlockSize >= 2 && Data.RawBlockSize <= 5)
                    Data.BlockSize = 576 * (1 << (Data.RawBlockSize - 2));
                else if (Data.RawBlockSize == 6)
                {
                    got = Data.fbs.Read (bb, bPos, 1);
                    Data.BlockSize = bb[bPos] + 1;
                    bPos += 1;
                }
                else if (Data.RawBlockSize == 7)
                {
                    got = Data.fbs.Read (bb, bPos, 2);
                    Data.BlockSize = (bb[bPos]<<8) + bb[bPos+1] + 1;
                    bPos += 2;
                }
                else
                    Data.BlockSize = 256 * (1 << (Data.RawBlockSize - 8));


                Data.RawSampleRate = bb[2] & 0xF;
                if (Data.RawSampleRate == 0xC)
                {
                    got = Data.fbs.Read (bb, bPos, 1);
                    Data.SampleRateText = bb[bPos] + "kHz";
                    bPos += 1;
                }
                else if (Data.RawSampleRate == 0xD || Data.RawSampleRate == 0xE)
                {
                    got = Data.fbs.Read (bb, bPos, 2);
                    Data.SampleRateText = (bb[bPos]<<8).ToString() + bb[bPos+1] + (Data.RawSampleRate == 0xD? " Hz" : " kHz");
                    bPos += 2;
                }
                else if (Data.RawSampleRate == 0)
                    Data.SampleRateText = Data.MetaSampleRate.ToString() + " Hz";
                else
                    Data.SampleRateText = SampleRateMap[Data.RawSampleRate];

                Data.RawChannelAssignment = bb[3] >> 4;

                Data.RawSampleSize = (bb[3] & 0xE) >> 1;
                if (Data.RawSampleSize == 0)
                    Data.SampleSizeText = Data.BitsPerSample.ToString() + " bits";
                else
                    Data.SampleSizeText = SampleSizeMap[Data.RawSampleSize];

                Data.aHdr = new byte[bPos];
                Array.Copy (bb, Data.aHdr, bPos);

                Data.ValidSize += bPos;
                Data.fbs.Position = Data.ValidSize;
                int octet = Data.fbs.ReadByte();
                if (octet < 0)
                {
                    IssueModel.Add ("File truncated near CRC-8", Severity.Fatal);
                    return;
                }
                Data.StoredAudioHeaderCRC8 = (Byte) octet;

                try
                {
                    Data.fbs.Position = Data.mediaPosition;
                }
                catch (EndOfStreamException)
                {
                    IssueModel.Add ("File truncated near audio data", Severity.Fatal);
                    return;
                }

                try
                {
                    Data.fbs.Position = Data.FileSize - 2;
                }
                catch (EndOfStreamException)
                {
                    IssueModel.Add ("File truncated looking for end", Severity.Fatal);
                    return;
                }

                bb = new byte[2];
                if (Data.fbs.Read (bb, 0, 2) != 2)
                {
                    IssueModel.Add ("Read failed on audio block CRC-16", Severity.Fatal);
                    return;
                }

                Data.StoredAudioBlockCRC16 = (UInt16) (bb[0] << 8 | bb[1]);
                Data.MediaCount = Data.FileSize - Data.mediaPosition;

                GetDiagnostics ();
            }

            private void GetDiagnostics()
            {
                if (Data.MetadataBlockStreamInfoSize != 0x22)
                    IssueModel.Add ($"Unexpected Metadata block size of {Data.MetadataBlockStreamInfoSize}", Severity.Advisory);

                if (Data.MinBlockSize < 16)
                    IssueModel.Add ("Minimum block size too low");

                if (Data.MinBlockSize > 65535)
                    IssueModel.Add ("Maximum block size too high");

                if (Data.RawSampleRate == 0xF)
                    IssueModel.Add ("Invalid sample rate");

                if (Data.RawSampleSize == 3 || Data.RawSampleSize == 7)
                    IssueModel.Add ($"Use of sample size index {Data.RawSampleSize} is reserved");

                if (Data.RawChannelAssignment >= 0xB)
                    IssueModel.Add ($"Use of reserved (undefined) channel assignment {Data.RawChannelAssignment}", Severity.Warning);

                if (Data.RawBlockSize == 0)
                    IssueModel.Add ("Block size index 0 use is reserved", Severity.Warning);

                if (Data.RawSampleRate == 0xF)
                    IssueModel.Add ("Sample rate index 15 use is invalid");

                if (Data.RawChannelAssignment >= 0xB)
                    IssueModel.Add ($"Channel index {Data.RawChannelAssignment} use is reserved", Severity.Warning);

                if (Data.Blocks.Tags.Lines.Count != Data.Blocks.Tags.StoredTagCount)
                    IssueModel.Add ("Stored tag count wrong");

                foreach (var lx in Data.Blocks.Tags.Lines)
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
                foreach (FlacBlockItem block in Data.Blocks.Items)
                    if (block.BlockType == FlacBlockType.Padding || block.BlockType == FlacBlockType.Picture)
                        picPlusPadSize += block.Size;

                if (picPlusPadSize > 512*1024)
                    IssueModel.Add ($"Artwork plus padding consume {picPlusPadSize} bytes.", Severity.Trivia, IssueTags.Fussy);
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
                if (Data.Issues.HasFatal)
                    return;

                if ((hashFlags & Hashes.Intrinsic) != 0)
                {
                    if (Data.ActualAudioHeaderCRC8 == null)
                    {
                        var hasher = new Crc8Hasher();
                        hasher.Append (Data.aHdr);
                        var hash = hasher.GetHashAndReset();
                        Data.ActualAudioHeaderCRC8 = hash[0];

                        if (Data.IsBadHeader)
                            IssueModel.Add ("CRC-8 check failed on audio header.");
                    }

                    if (Data.ActualAudioBlockCRC16 == null)
                        try
                        {
                            var hasher = new Crc16nHasher();
                            hasher.Append (Data.fbs, Data.mediaPosition, Data.FileSize - Data.mediaPosition - 2);
                            var hash = hasher.GetHashAndReset();
                            Data.ActualAudioBlockCRC16 = BitConverter.ToUInt16 (hash, 0);
                        }
                        catch (EndOfStreamException ex)
                        {
                            IssueModel.Add ("Read failed while verifying audio CRC: " + ex.Message, Severity.Fatal);
                            return;
                        }

                        if (Data.IsBadDataCRC16)
                            IssueModel.Add ("CRC-16 check failed on audio data.");
                        else
                            if (Data.IsBadHeader)
                                IssueModel.Add ("CRC-16 check successful.", Severity.Advisory);
                            else
                                IssueModel.Add ("CRC-8, CRC-16 checks successful.", Severity.Noise);
                }

#if ! NETFX_CORE
                if ((hashFlags & Hashes.PcmMD5) != 0 && Data.actualAudioDataMD5 == null)
                {
                    Process px = null;
                    try
                    { px = StartFlac (Data.Path); }
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
                                Data.actualAudioDataMD5 = hash;
                            }
                            catch (EndOfStreamException ex)
                            { IssueModel.Add ("Read failed while verifying audio MD5: " + ex.Message, Severity.Fatal); }

                            if (Data.IsBadDataMD5)
                                IssueModel.Add ("MD5 check failed on audio data.");
                            else
                                IssueModel.Add ("MD5 check successful.", Severity.Noise);
                        }
                }

                if ((hashFlags & Hashes.PcmCRC32) != 0 && Data.ActualPcmCRC32 == null)
                {
                    Process px = null;
                    try
                    { px = StartFlac (Data.Path); }
                    catch (Exception ex)
                    { IssueModel.Add ("flac executable failed with '" + ex.Message.Trim (null) + "'."); }
                
                    if (px != null)
                        using (var br = new BinaryReader (px.StandardOutput.BaseStream))
                        {
                            var hasher = new Crc32rHasher();
                            hasher.Append (br);
                            var hash = hasher.GetHashAndReset();
                            Data.ActualPcmCRC32 = BitConverter.ToUInt32 (hash, 0);
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
        public string BlockingStrategyText => (RawBlockingStrategy == 0 ? "Fixed" : "Variable") + " size";
        public int RawBlockSize { get; private set; }
        public int BlockSize { get; private set; }
        public int RawSampleRate { get; private set; }
        public string SampleRateText { get; private set; }
        public int RawChannelAssignment { get; private set; }
        public string ChannelAssignmentText => ChannelAssignmentMap[RawChannelAssignment];
        public int RawSampleSize { get; private set; }
        public string SampleSizeText { get; private set; }

        public Byte StoredAudioHeaderCRC8 { get; private set; }
        public Byte? ActualAudioHeaderCRC8 { get; private set; }
        public string StoredAudioHeaderCRC8ToHex => StoredAudioHeaderCRC8.ToString ("X2");
        public string ActualAudioHeaderCRC8ToHex => ActualAudioHeaderCRC8?.ToString ("X2");

        public UInt16 StoredAudioBlockCRC16 { get; private set; }
        public UInt16? ActualAudioBlockCRC16 { get; private set; }
        public string StoredAudioBlockCRC16ToHex => StoredAudioBlockCRC16.ToString ("X4");
        public string ActualAudioBlockCRC16ToHex => ActualAudioBlockCRC16?.ToString ("X4");

        private byte[] storedAudioDataMD5 = null;
        private byte[] actualAudioDataMD5 = null;
        public string StoredAudioDataMD5ToHex => storedAudioDataMD5==null ? null : ConvertTo.ToHexString (storedAudioDataMD5);
        public string ActualAudioDataMD5ToHex => actualAudioDataMD5==null ? null : ConvertTo.ToHexString (actualAudioDataMD5);

        public UInt32? ActualPcmCRC32 { get; private set; }
        public string ActualPcmCRC32ToHex => ActualPcmCRC32?.ToString ("X8");

        public bool IsBadDataCRC16 => ActualAudioBlockCRC16 != null && ActualAudioBlockCRC16.Value != StoredAudioBlockCRC16;
        public bool IsBadDataMD5 => actualAudioDataMD5 != null && ! actualAudioDataMD5.SequenceEqual (storedAudioDataMD5);

        private FlacFormat (Model model, Stream stream, string path) : base (model, stream, path)
        { }

        public override bool IsBadHeader
         => ActualAudioHeaderCRC8 != null && StoredAudioHeaderCRC8 != ActualAudioHeaderCRC8.Value;

        public override bool IsBadData
         => IsBadDataCRC16 || IsBadDataMD5;

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
                report.Add ($"  Minimum block size = {MinBlockSize}");
                report.Add ($"  Maximum block size = {MaxBlockSize}");
                report.Add ($"  Minimum frame size = {MinFrameSize}");
                report.Add ($"  Maximum frame size = {MaxFrameSize}");
            }

            report.Add ($"  Sample rate = {MetaSampleRate} Hz");
            report.Add ($"  Number of channels = {ChannelCount}");
            report.Add ($"  Bits per sample = {BitsPerSample}");

            if (scope <= Granularity.Detail)
            {
                report.Add ("  Total samples = " + (TotalSamples != 0? TotalSamples.ToString() : " (unknown)"));

                report.Add (String.Empty);
                report.Add ("Raw audio header: " + ConvertTo.ToBitString (aHdr, 1));

                report.Add (String.Empty);
                report.Add ("Cooked audio header:");
                report.Add ($"  Blocking strategy = {BlockingStrategyText}");
                report.Add ($"  Block size = {BlockSize} samples");
                report.Add ($"  Sample rate = {SampleRateText}");
                report.Add ($"  Channel assignment = {ChannelAssignmentText}");
                report.Add ($"  Sample size = {SampleSizeText}");
                report.Add ($"  Sample/frame number = {SampleOrFrameNumber}");

                report.Add (String.Empty);
                report.Add ("Checks:");

                report.Add ($"  Stored audio header CRC-8 = {StoredAudioHeaderCRC8ToHex}");
                if (ActualAudioHeaderCRC8 != null)
                    report.Add ($"  Actual audio header CRC-8 = {ActualAudioHeaderCRC8ToHex}");

                report.Add ($"  Stored audio block CRC-16 = {StoredAudioBlockCRC16ToHex}");
                if (ActualAudioBlockCRC16 != null)
                    report.Add ($"  Actual audio block CRC-16 = {ActualAudioBlockCRC16ToHex}");

                report.Add ($"  Stored PCM MD5 = {StoredAudioDataMD5ToHex}");
                if (actualAudioDataMD5 != null)
                    report.Add ($"  Actual PCM MD5 = {ActualAudioDataMD5ToHex}");

                if (ActualPcmCRC32 != null)
                    report.Add ($"  Actual PCM CRC-32 = {ActualPcmCRC32ToHex}");
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
                report.Add ($"  Vendor: {Blocks.Tags.Vendor}");
                foreach (var item in Blocks.Tags.Lines)
                    report.Add ($"  {item}");
            }
        }
    }
}

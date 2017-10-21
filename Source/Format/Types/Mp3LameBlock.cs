using System;

namespace NongFormat
{
    public class Mp3XingBlock
    {
        public class Model
        {
            public Mp3XingBlock BindXing { get; protected set; }

            public Model (byte[] hdr, string xingText)
            { }
        }

        protected byte[] aHdr;

        public Mp3XingBlock (byte[] hdr, string xingText)
        {
            this.aHdr = hdr;
            this.XingVersion = xingText;

            int ix = 0x2C;
            if (IsFrameCountPresent)
                ix += 4;
            if (IsBytesPresent)
            {
                this.XingSize = ConvertTo.FromBig32ToInt32 (aHdr, ix);
                ix += 4;
            }
            if (IsTableOfContentsPresent)
            {
                this.tocOffset = ix;
                ix += 100;
            }
            if (IsQualityIndicatorPresent)
                this.QualityIndicator = IsQualityIndicatorPresent? ConvertTo.FromBig32ToInt32 (aHdr, ix) : (int?) null;
        }

        public string XingVersion { get; private set; }
        public bool IsFrameCountPresent { get { return (aHdr[0x2B] & 1) != 0; } }
        public bool IsBytesPresent { get { return (aHdr[0x2B] & 2) != 0; } }
        public bool IsTableOfContentsPresent { get { return (aHdr[0x2B] & 4) != 0; } }
        public bool IsQualityIndicatorPresent { get { return (aHdr[0x2B] & 8) != 0; } }

        public int XingSize { get; private set; }
        private int tocOffset;  // table of contents (100 bytes of indexes)

        //  0..100 - worst..best
        //  LAME: V2=78, V0=98, altPresetExtreme=78, 320CBR=58.  Flakey.
        public int? QualityIndicator { get; private set; }

        // Scan table of contents for decrements.
        public bool IsTocCorrupt()
        {
            for (int ii = 0;;)
            {
                var prev = aHdr[tocOffset+ii];
                if (++ii >= 100)
                    return false;
                if (aHdr[tocOffset+ii] < prev)
                    return true;
            }
        }
    }


    public class Mp3LameBlock : Mp3XingBlock
    {
        public new class Model :Mp3XingBlock.Model
        {
            public readonly Mp3LameBlock Bind;

            public Model (byte[] hdr, string lameVersion, string xingText) : base (hdr, xingText)
            { BindXing = Bind = new Mp3LameBlock (hdr, lameVersion, xingText); }

            public void SetActualAudioHeaderCRC16 (UInt16 crc)
            { Bind.ActualAudioHeaderCRC16 = crc; }

            public void SetActualAudioDataCRC16 (UInt16 crc)
            { Bind.ActualAudioDataCRC16 = crc; }
        }

        public Mp3LameBlock (byte[] hdr, string lameVersion, string xingText) : base (hdr, xingText)
        {
            this.Version = lameVersion;
            this.LameSize = ConvertTo.FromBig32ToInt32 (aHdr, 0xB8);

            this.ActualAudioHeaderCRC16 = null;
            this.ActualAudioDataCRC16 = null;

            int lsn = aHdr[0xA5];
            if (lsn >= 3 && lsn <= 7)
                IsVbr = true;
            else if (lsn == 1 || lsn == 8)
                IsCbr = true;
            else if (lsn == 2 || (lsn >= 9 && lsn <= 14))
                IsAbr = true;

            this.TagRevision = aHdr[0xA5] >> 4;
            this.BitrateMethod = aHdr[0xA5] & 0x0F;
            this.LowpassFilter = aHdr[0xA6];
            this.ReplayGainPeak = BitConverter.ToSingle (aHdr, 0xA7);
            this.RadioReplayGain = (aHdr[0xAB] << 8) + aHdr[0xAC];
            this.AudiophileReplayGain = (aHdr[0xAD] << 8) + aHdr[0xAE];

            this.EncoderDelayStart = (aHdr[0xB1] << 4) + (aHdr[0xB2] >> 4);
            this.EncoderDelayEnd = ((aHdr[0xB2] & 0xF) << 8) + aHdr[0xB3];
            this.Mp3Gain = aHdr[0xB5];
            this.Surround = (aHdr[0xB6] & 0x38) >> 3;
            this.Preset = ((aHdr[0xB6] & 7) << 8) + aHdr[0xB7];

            this.MinBitRate = aHdr[0xB0];
            if (this.MinBitRate == 0xFF)
                this.MinBitRate = 320;
        }

        public string Version { get; private set; }
        public int LameSize { get; private set; }

        public bool IsVbr { get; private set; }
        public bool IsCbr { get; private set; }
        public bool IsAbr { get; private set; }

        public int TagRevision { get; private set; }
        public int BitrateMethod { get; private set; }
        public int LowpassFilter { get; private set; }

        // Embedded RG is obsolete in favor of RG via id3v2 tags.
        public float ReplayGainPeak { get; private set; }
        public int RadioReplayGain { get; private set; }
        public int AudiophileReplayGain { get; private set; }

        public int LameFlags { get { return aHdr[0xAF]; } }

        public int MinBitRate { get; private set; }
        public int EncoderDelayStart { get; private set; }
        public int EncoderDelayEnd { get; private set; }
        public int Mp3Gain { get; private set; }
        public int Surround { get; private set; }
        public int Preset { get; private set; }

        public UInt16? ActualAudioHeaderCRC16 { get; private set; }
        public UInt16? ActualAudioDataCRC16 { get; private set; }

        public UInt16 StoredAudioHeaderCRC16 { get { return (UInt16) (aHdr[0xBE] << 8 | aHdr[0xBF]); } }
        public UInt16 StoredAudioDataCRC16 { get { return (UInt16) (aHdr[0xBC] << 8 | aHdr[0xBD]); } }

        public string ActualAudioHeaderCRC16ToHex
        { get { return String.Format ("{0:X4}", ActualAudioHeaderCRC16); } }
        public string ActualAudioDataCRC16ToHex
        { get { return String.Format ("{0:X4}", ActualAudioDataCRC16); } }

        public string StoredAudioHeaderCRC16ToHex
        { get { return String.Format ("{0:X4}", StoredAudioHeaderCRC16); } }
        public string StoredAudioDataCRC16ToHex
        { get { return String.Format ("{0:X4}", StoredAudioDataCRC16); } }
    }
}

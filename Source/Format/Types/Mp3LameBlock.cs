using System;

namespace NongFormat
{
    public class Mp3XingBlock
    {
        public static Mp3XingBlock.Model Create (byte[] buf, Mp3Header header)
        {
            System.Diagnostics.Debug.Assert (header.IsLayer3);

            int xix = header.XingOffset;
            string xingString = ConvertTo.FromAsciiToString (buf, xix, 4);

            if (xingString == "Info" || xingString == "Xing")
            {
                string lameString = ConvertTo.FromAsciiToString (buf, xix + 0x78, 9);

                if (lameString.StartsWith ("LAME"))
                    return new Mp3LameBlock.Model (buf, xix, header, xingString, lameString);
                else
                    return new Mp3XingBlock.Model (buf, xix, header, xingString);
            }

            return null;
        }

        public class Model
        {
            public Mp3XingBlock BindXing { get; protected set; }

            public Model (byte[] hdr, int xingIx, Mp3Header header, string xingText)
            { BindXing = new Mp3XingBlock (hdr, xingIx, header, xingText); }
        }

        protected byte[] buf;
        protected int xingIx;
        protected Mp3Header header;
        private readonly int tocOffset;
        public string XingString { get; private set; }
        public int FrameCount { get; private set; }
        public int XingSize { get; private set; }

        public Mp3XingBlock (byte[] frameBuf, int xix, Mp3Header header, string xingString)
        {
            this.buf = frameBuf;
            this.header = header;
            this.xingIx = xix;
            this.XingString = xingString;

            int ix = xingIx + 8;
            if (HasFrameCount)
            {
                this.FrameCount = ConvertTo.FromBig32ToInt32 (frameBuf, ix);
                ix += 4;
            }
            if (HasSize)
            {
                this.XingSize = ConvertTo.FromBig32ToInt32 (frameBuf, ix);
                ix += 4;
            }
            if (HasTableOfContents)
            {
                this.tocOffset = ix;
                ix += 100;
            }
            if (HasQualityIndicator)
                this.QualityIndicator = ConvertTo.FromBig32ToInt32 (frameBuf, ix);
        }

        //  0..100 - worst..best
        // LAME3.100: V0=100, V1=90, V2=80, V3=70, V6=40, V7=50 (MPEG1,32K), V8 (50,MPEG2,24K), V9 (40,22050)
        // LAME (other): V0=98, altPresetExtreme=78, 320CBR=58.  Flakey.
        public int QualityIndicator { get; private set; }

        public bool HasFrameCount => (buf[xingIx+7] & 1) != 0;
        public bool HasSize => (buf[xingIx+7] & 2) != 0;
        public bool HasTableOfContents => (buf[xingIx+7] & 4) != 0;
        public bool HasQualityIndicator => (buf[xingIx+7] & 8) != 0;

        // Scan table of contents for decrements.
        bool? isTocCorrupt = null;
        public bool IsTocCorrupt()
        {
            if (isTocCorrupt == null)
                for (int ii = 0;;)
                {
                    var prev = buf[tocOffset+ii];
                    if (++ii >= 100)
                    { isTocCorrupt = false; break; }
                    if (buf[tocOffset+ii] < prev)
                    { isTocCorrupt = true; break; }
                }
            return isTocCorrupt.Value;
        }
    }


    public class Mp3LameBlock : Mp3XingBlock
    {
        public new class Model : Mp3XingBlock.Model
        {
            public readonly Mp3LameBlock Bind;

            public Model (byte[] buf, int hix, Mp3Header header, string xingString, string lameString) : base (buf, hix, header, xingString)
            { BindXing = Bind = new Mp3LameBlock (buf, hix, header, xingString, lameString); }

            public void SetActualHeaderCrc (ushort crc) => Bind.ActualHeaderCrc = crc;
            public void SetActualDataCrc (ushort crc) => Bind.ActualDataCrc = crc;
        }

        public string LameVersion { get; private set; }
        public ushort? ActualHeaderCrc { get; private set; } = null;
        public ushort? ActualDataCrc { get; private set; } = null;

        public Mp3LameBlock (byte[] buf, int xix, Mp3Header header, string xingString, string lameString) : base (buf, xix, header, xingString)
        { LameVersion = lameString; }

        public bool IsVbr { get { int b = buf[xingIx+0x81] & 0xF; return b >= 3 && b <= 7; } }
        public bool IsCbr { get { int b = buf[xingIx+0x81] & 0xF; return b == 1 || b == 8; } }
        public bool IsAbr { get { int b = buf[xingIx+0x81] & 0xF; return b == 2 || (b >= 9 && b <= 14); } }

        public int TagRevision => buf[xingIx+0x81] >> 4;
        public int BitrateMethod => buf[xingIx+0x81] & 0x0F;
        public int LowpassFilter => buf[xingIx+0x82];
        public float ReplayGainPeak => BitConverter.ToSingle (buf, xingIx+0x83);
        public int RadioReplayGain => (buf[xingIx+0x87] << 8) + buf[xingIx+0x88];
        public int AudiophileReplayGain => (buf[xingIx+0x89] << 8) + buf[xingIx+0x8A];
        public int LameFlags => buf[xingIx+0x8B];
        public int MinBitRate => buf[xingIx+0x8C];
        public int EncoderDelayStart => (buf[xingIx+0x8D] << 4) + (buf[xingIx+0x8E] >> 4);
        public int EncoderDelayEnd => ((buf[xingIx+0x8E] & 0xF) << 8) + buf[xingIx+0x8F];
        public int MiscBits => buf[xingIx+0x90];
        public int Mp3Gain => buf[xingIx+0x91];
        public int Surround => (buf[xingIx+0x92] & 0x38) >> 3;
        public int Preset => ((buf[xingIx+0x92] & 7) << 8) + buf[xingIx+0x93];
        public int LameSize => ConvertTo.FromBig32ToInt32 (buf, xingIx+0x94);
        public ushort StoredHeaderCrc => (ushort) (buf[xingIx+0x9A] << 8 | buf[xingIx+0x9B]);
        public ushort StoredDataCrc => (ushort) (buf[xingIx+0x98] << 8 | buf[xingIx+0x99]);

        public int LameHeaderSize => xingIx+0x9A;

        public string MinBitRateText { get { int br = MinBitRate; return br==255? "255+" : br.ToString(); } }
        public string ActualHeaderCrcText => $"{ActualHeaderCrc:X4}";
        public string ActualDataCrcText => $"{ActualDataCrc:X4}";
        public string StoredHeaderCrcText => $"{StoredHeaderCrc:X4}";
        public string StoredDataCrcText => $"{StoredDataCrc:X4}";

        private string profile = null;
        public string Profile
        {
            get
            {
                if (profile == null)
                {
                    if (IsVbr)
                    {
                        profile = "R" + header.BitRate;
                        if (HasQualityIndicator && QualityIndicator >= 40 && QualityIndicator <= 100)
                        {
                            int freq = header.SampleRate;
                            if (freq == 44100)
                                profile = "V" + (10 - QualityIndicator/10);
                            else if (freq == 32000)
                                profile = "V7";
                            else if (freq == 24000)
                                profile = "V8";
                            else if (freq == 22050)
                                profile = "V9";
                        }
                    }
                    else if (IsCbr)
                        profile = "C" + header.BitRate;
                    else if (IsAbr)
                        profile = "A" + Preset;
                }

                return profile;
            }
        }
    }
}

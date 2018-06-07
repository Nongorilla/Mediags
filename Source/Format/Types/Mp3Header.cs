namespace NongFormat
{
    public struct Mp3Header
    {
        static private readonly int[] mpeg2xRates = new int[]
        { 0,8,16,24,32,40,48,56,64,80,96,112,128,144,160,-1 };

        static private readonly int[][] bitRateMap =  // 0=free, -1=reserved
        {
            mpeg2xRates,
            null,
            mpeg2xRates,
            new int[] { 0,32,40,48,56,64,80,96,112,128,160,192,224,256,320,-1 }
        };

        static private readonly int[][] samplingRateMap =
        {
            new int[] { 11025,12000,8000,0 },
            null,
            new int[] { 22050,24000,16000,0 },
            new int[] { 44100,48000,32000,0 }
        };

        static private readonly string[] mpegVersion = { "2.5", "(reserved)", "2", "1" };

        static private readonly string[] emphasisMap = { "None", "50/15 MS", "(reserved)", "CCIT J.17" };

        public int Bits { get; private set; }

        public Mp3Header (byte[] hdr, int ix)
        { Bits = ConvertTo.FromBig32ToInt32 (hdr, ix); }

        public int MpegVersionBits => (Bits & 0x180000) >> 19;
        public int MpegLayerBits => (Bits & 0x60000) >> 17;
        public int CrcProtectedBit => (Bits & 0x10000) >> 16;
        public int BitRateBits => (Bits & 0xF000) >> 12;
        public int SampleRateBits => (Bits & 0xC00) >> 10;
        public int PaddingBit => (Bits & 0x200) >> 9;
        public int PrivateBit => (Bits & 0x100) >> 8;
        public Mp3ChannelMode ChannelMode => (Mp3ChannelMode) ((Bits & 0xC0) >> 6);
        public bool IsMsStereo => (Bits & 0x20) != 0;
        public bool IsIntensityStereo => (Bits & 0x10) != 0;
        public int CopyrightBit => (Bits & 8) >> 3;
        public int OriginalBit => (Bits & 4) >> 2;
        public int EmphasisBits => Bits & 3;

        public bool IsLayer3 => (Bits & 0xFFF60000) == 0xFFF20000;  // true for Mpeg1-Layer3, MPeg2-Layer3
        public string MpegVersion => mpegVersion[MpegVersionBits];
        public string Codec => "MPEG-" + MpegVersion + " Layer " + (4-MpegLayerBits);
        public int? BitRate => bitRateMap[MpegVersionBits][BitRateBits];
        public string BitRateText { get { int? r = BitRate; return r==null ? "(reserved)" : r.ToString (); } }
        public int SampleRate => samplingRateMap[MpegVersionBits][SampleRateBits];
        public string SampleRateText { get { int? s = SampleRate; return s==0 ? "(reserved)" : s.ToString (); } }
        public string EmphasisText => emphasisMap[EmphasisBits];

        public static bool IsBadCbr (int mpegVersionBits, int rate)
        {
            if (rate == 255)
                return false;
            var map = bitRateMap[mpegVersionBits];
            if (map != null)
                for (var ix = 0; ix < map.Length; ++ix)
                    if (map[ix] == rate)
                        return false;
            return true;
        }

        public string ModeText
        {
            get
            {
                string result = ChannelMode.ToString ();
                if (ChannelMode == Mp3ChannelMode.JointStereo)
                    result += " (Intensity stereo " + (IsIntensityStereo ? "on" : "off")
                            + ", M/S stereo " + (IsMsStereo ? "on" : "off") + ")";
                return result;
            }
        }

        public int XingOffset => ChannelMode == Mp3ChannelMode.SingleChannel ? (MpegVersionBits == 3 ? 0x15 : 0x0D)
                                                                             : (MpegVersionBits == 3 ? 0x24 : 0x15);
    }
}

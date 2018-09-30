using System.IO;

namespace NongFormat
{
    public class AsfFormat : FormatBase
    {
        public static string[] Names
         => new string[] { "asf", "wmv", "wma" };

        public override string[] ValidNames
         => Names;

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x28
                    && hdr[0x00]==0x30 && hdr[0x01]==0x26 && hdr[0x02]==0xB2 && hdr[0x03]==0x75
                    && hdr[0x04]==0x8E && hdr[0x05]==0x66 && hdr[0x06]==0xCF && hdr[0x07]==0x11
                    && hdr[0x08]==0xA6 && hdr[0x09]==0xD9 && hdr[0x0A]==0x00 && hdr[0x0B]==0xAA
                    && hdr[0x0C]==0x00 && hdr[0x0D]==0x62 && hdr[0x0E]==0xCE && hdr[0x0F]==0x6C)
                return new Model (stream, path);
            return null;
        }

        public class Model : FormatBase.ModelBase
        {
            public new readonly AsfFormat Data;

            public Model (Stream stream, string path)
             => base._data = Data = new AsfFormat (this, stream, path);
        }

        private AsfFormat (Model model, Stream stream, string path) : base (model, stream, path)
        { }
    }
}

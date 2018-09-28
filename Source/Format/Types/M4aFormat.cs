using System.IO;

namespace NongFormat
{
    // Typically contains AAC (Apple lossy audio) or ALAC (Apple lossless audio)
    public class M4aFormat : Mpeg4Container
    {
        public static string[] Names
        { get { return new string[] { "m4a" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x0C
                    && hdr[0x04]=='f' && hdr[0x05]=='t' && hdr[0x06]=='y' && hdr[0x07]=='p'
                    && hdr[0x08]=='M' && hdr[0x09]=='4' && hdr[0x0A]=='A' && hdr[0x0B]==' ')
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : Mpeg4Container.Model
        {
            public new readonly M4aFormat Data;

            public Model (Stream stream, byte[] header, string path)
            {
                base._data = Data = new M4aFormat (stream, path);
                Data.Issues = IssueModel.Data;

                ParseMpeg4 (stream, header, path);
                GetDiagnostics();
            }
        }


        private M4aFormat (Stream stream, string path) : base (stream, path)
        { }
    }
}

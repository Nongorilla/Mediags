using System.IO;

namespace NongFormat
{
    // Typically contains H.264 audio/visual
    public class Mp4Format : Mpeg4Container
    {
        public static string[] Names
        { get { return new string[] { "mp4" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x0C
                    && hdr[0x04]=='f' && hdr[0x05]=='t' && hdr[0x06]=='y' && hdr[0x07]=='p'
                   && (hdr[0x08]=='i' && hdr[0x09]=='s' && hdr[0x0A]=='o' && hdr[0x0B]=='m'
                    || hdr[0x08]=='m' && hdr[0x09]=='p' && hdr[0x0A]=='4' && hdr[0x0B]=='2'))
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : Mpeg4Container.Model
        {
            public new readonly Mp4Format Data;

            public Model (Stream stream, byte[] header, string path)
            {
                base._data = Data = new Mp4Format (this, stream, path);

                ParseMpeg4 (stream, header, path);
                CalcMark();
                GetDiagnostics();
            }
        }

        private Mp4Format (Model model, Stream stream, string path) : base (model, stream, path)
        { }
    }
}

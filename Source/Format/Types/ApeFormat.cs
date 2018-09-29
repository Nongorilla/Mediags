using System.IO;

namespace NongFormat
{
    public class ApeFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "ape" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x24 && hdr[0]=='M' && hdr[1]=='A' && hdr[2]=='C' && hdr[3]==' ')
                return new Model (stream, path);
            return null;
        }

        public class Model : FormatBase.ModelBase
        {
            public new readonly ApeFormat Data;

            public Model (Stream stream, string path)
             => base._data = Data = new ApeFormat (this, stream, path);
        }

        private ApeFormat (Model model, Stream stream, string path) : base (model, stream, path)
        { }
    }
}

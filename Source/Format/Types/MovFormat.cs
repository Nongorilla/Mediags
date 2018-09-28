using System.IO;

namespace NongFormat
{
    public class MovFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "mov", "qt" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static FormatBase.ModelBase CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x20)
                if (hdr[4]=='m' && hdr[5]=='o' && hdr[6]=='o' && hdr[7]=='v')
                    return new MovFormat.Model (stream, hdr, path);
                else if (hdr[0x04]=='f' && hdr[0x05]=='t' && hdr[0x06]=='y' && hdr[0x07]=='p'
                      && hdr[0x08]=='q' && hdr[0x09]=='t' && hdr[0x0A]==' ' && hdr[0x0B]==' ')
                    return new MovFormat2.Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public new readonly MovFormat Data;

            public Model (Stream stream, byte[] header, string path)
            {
                _data = Data = new MovFormat (stream, path);
                Data.Issues = IssueModel.Data;
            }
        }

        private MovFormat (Stream stream, string path) : base (stream, path)
        { }
    }


    public class MovFormat2 : Mpeg4Container
    {
        public override string[] ValidNames
        { get { return MovFormat.Names; } }

        public new class Model : Mpeg4Container.Model
        {
            public new readonly MovFormat2 Data;

            public Model (Stream stream, byte[] header, string path)
            {
                base._data = Data = new MovFormat2 (stream, path);
                Data.Issues = IssueModel.Data;

                ParseMpeg4 (stream, header, path);
                CalcMark();
                GetDiagnostics();
            }
        }

        private MovFormat2 (Stream stream, string path) : base (stream, path)
        { }
    }
}

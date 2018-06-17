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
            public readonly MovFormat Bind;

            public Model (Stream stream, byte[] header, string path)
            {
                BaseBind = Bind = new MovFormat (stream, path);
                Bind.Issues = IssueModel.Bind;
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
            public readonly MovFormat2 Bind;

            public Model (Stream stream, byte[] header, string path)
            {
                BaseBind = Mpeg4Bind = Bind = new MovFormat2 (stream, path);
                Bind.Issues = IssueModel.Bind;

                ParseMpeg4 (stream, header, path);
                CalcMark();
                GetDiagnostics();
            }
        }

        private MovFormat2 (Stream stream, string path) : base (stream, path)
        { }
    }
}

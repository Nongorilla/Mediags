using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public class AviFormat : RiffContainer
    {
        public static string[] Names
        { get { return new string[] { "avi", "divx" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x0C
                    && hdr[0x00]=='R' && hdr[0x01]=='I' && hdr[0x02]=='F' && hdr[0x03]=='F'
                    && hdr[0x08]=='A' && hdr[0x09]=='V' && hdr[0x0A]=='I' && hdr[0x0B]==' ')
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : RiffContainer.Model
        {
            public new readonly AviFormat Data;

            public Model (Stream stream, byte[] header, string path)
            {
                base._data = Data = new AviFormat (stream, path);
                Data.Issues = IssueModel.Data;

                ParseRiff (header);

                var buf = new byte[0xC0];

                stream.Position = 0;
                int got = stream.Read (buf, 0, 0xC0);
                if (got != 0xC0)
                {
                    IssueModel.Add ("File is short", Severity.Fatal);
                    return;
                }

                Data.StreamCount = ConvertTo.FromLit32ToInt32 (buf, 0x38);
                Data.Width = ConvertTo.FromLit32ToInt32 (buf, 0x40);
                Data.Height = ConvertTo.FromLit32ToInt32 (buf, 0x44);
                Data.Codec = Encoding.ASCII.GetString (buf, 0xBC, 4).Trim();

                CalcMark();
                GetDiagsForMarkable();
            }
        }


        public int StreamCount { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Codec {get; private set; }


        private AviFormat (Stream stream, string path) : base (stream, path)
        { }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            base.GetDetailsBody (report, scope);
            if (report.Count > 0 && scope <= Granularity.Detail)
                    report.Add (String.Empty);

            report.Add ("Codec = " + Codec);
            report.Add ("Resolution = " + Width + 'x' + Height);
            if (scope <= Granularity.Detail)
                report.Add ("Streams = " + StreamCount);
        }
    }
}

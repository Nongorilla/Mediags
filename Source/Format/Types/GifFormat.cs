using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public class GifFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "gif" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x20 && hdr[0]=='G' && hdr[1]=='I' && hdr[2]=='F'
                    && hdr[3]=='8' && (hdr[4]=='7' || hdr[4]=='9') && hdr[5] == 'a')
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public readonly GifFormat Bind;

            public Model (Stream stream, byte[] header, string path)
            {
                BaseBind = Bind = new GifFormat (stream, path);
                Bind.Issues = IssueModel.Data;

                // Arbitrary sanity limit.
                if (Bind.FileSize > 50000000)
                {
                    IssueModel.Add ("File size insanely huge", Severity.Fatal);
                    return;
                }

                Bind.fBuf = new byte[(int) Bind.FileSize];

                stream.Position = 0;
                int got = stream.Read (Bind.fBuf, 0, (int) Bind.FileSize);
                if (got < Bind.FileSize)
                {
                    IssueModel.Add ("Read failed.", Severity.Fatal);
                    return;
                }

                Bind.Version = Bind.fBuf[4]=='7'? "87a" : "89a";
                Bind.Width = ConvertTo.FromLit16ToInt32 (Bind.fBuf, 6);
                Bind.Height = ConvertTo.FromLit16ToInt32 (Bind.fBuf, 8);
            }
        }


        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Version { get; private set; }


        private GifFormat (Stream stream, string path) : base (stream, path)
        { }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (report.Count != 0)
                report.Add (String.Empty);

            report.Add ("Dimensions = " + Width + 'x' + Height);

            if (scope <= Granularity.Detail)
                report.Add ("Version = " + Version);
        }
    }
}

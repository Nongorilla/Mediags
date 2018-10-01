using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public class GifFormat : FormatBase
    {
        public static string[] Names
         => new string[] { "gif" };

        public override string[] ValidNames
         => Names;

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x20 && hdr[0]=='G' && hdr[1]=='I' && hdr[2]=='F'
                    && hdr[3]=='8' && (hdr[4]=='7' || hdr[4]=='9') && hdr[5] == 'a')
                return new Model (stream, path);
            return null;
        }


        public new class Model : FormatBase.Model
        {
            public new readonly GifFormat Data;

            public Model (Stream stream, string path)
            {
                base._data = Data = new GifFormat (this, stream, path);

                // Arbitrary sanity limit.
                if (Data.FileSize > 50000000)
                {
                    IssueModel.Add ("File size insanely huge", Severity.Fatal);
                    return;
                }

                Data.fBuf = new byte[(int) Data.FileSize];

                stream.Position = 0;
                int got = stream.Read (Data.fBuf, 0, (int) Data.FileSize);
                if (got < Data.FileSize)
                {
                    IssueModel.Add ("Read failed.", Severity.Fatal);
                    return;
                }

                Data.Version = Data.fBuf[4]=='7'? "87a" : "89a";
                Data.Width = ConvertTo.FromLit16ToInt32 (Data.fBuf, 6);
                Data.Height = ConvertTo.FromLit16ToInt32 (Data.fBuf, 8);
            }
        }


        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Version { get; private set; }


        private GifFormat (Model model, Stream stream, string path) : base (model, stream, path)
        { }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (report.Count != 0)
                report.Add (String.Empty);

            report.Add ($"Dimensions = {Width}x{Height}");

            if (scope <= Granularity.Detail)
                report.Add ($"Version = {Version}");
        }
    }
}

using System.Collections.Generic;
using System.Text;
using System.IO;
using NongIssue;
using System;

namespace NongFormat
{
    public partial class LogXldFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "log" }; } }

        public static string Subname
        { get { return "XLD"; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (StartsWith (hdr, logXldSig))
                return new Model (stream, hdr, path);
            return null;
        }


        public partial class Model : FormatBase.ModelBase
        {
            public readonly LogXldFormat Bind;
            public LogEacTrack.Vector.Model TracksModel;
            private LogBuffer parser;

            public Model (Stream stream, byte[] hdr, string path)
            {
                TracksModel = new LogEacTrack.Vector.Model();
                BaseBind = this.Bind = new LogXldFormat (stream, path, TracksModel.Bind);
                Bind.Issues = IssueModel.Data;

                // Arbitrary limit.
                if (Bind.FileSize > 250000)
                {
                    IssueModel.Add ("File insanely huge.", Severity.Fatal);
                    return;
                }

                Bind.fBuf = new byte[Bind.FileSize];
                Bind.fbs.Position = 0;
                var got = Bind.fbs.Read (Bind.fBuf, 0, (int) Bind.FileSize);
                if (got != Bind.FileSize)
                {
                    IssueModel.Add ("Read failed", Severity.Fatal);
                    return;
                }

                parser = new LogBuffer (Bind.fBuf, Encoding.ASCII);
                string lx = parser.ReadLineLTrim();
                Bind.XldVersionText = lx.Substring (logXldSig.Length);
                lx = parser.ReadLineLTrim();
                if (parser.EOF)
                    return;

                if (lx.StartsWith ("XLD extraction logfile from "))
                    Bind.RipDate = lx.Substring (28);

                lx = parser.ReadLineLTrim();
                if (parser.EOF)
                    return;

                int slashPos = lx.IndexOf ('/');
                if (slashPos < 0)
                {
                    IssueModel.Add ("Missing '<artist> / <album>', " + parser.GetPlace() + ".");
                    return;
                }
                Bind.RipArtist = lx.Substring (0, slashPos).Trim();
                Bind.RipAlbum = lx.Substring (slashPos + 1).Trim();

                for (;;)
                {
                    if (parser.EOF) break;
                    lx = parser.ReadLineLTrim();
                    if (lx == "-----BEGIN XLD SIGNATURE-----")
                    {
                        lx = parser.ReadLineLTrim();
                        if (! parser.EOF)
                        {
                            Bind.storedHash = lx;
                            lx = parser.ReadLineLTrim();
                        }
                    }
                }

                GetDiagnostics();
            }

            private void GetDiagnostics()
            {
                if (Bind.storedHash == null)
                    IssueModel.Add ("No signature.", Severity.Trivia, IssueTags.Fussy);
            }
        }

        private LogXldFormat (Stream stream, string path, LogEacTrack.Vector tracks) : base (stream, path)
        { this.Tracks = tracks; }

        private static readonly byte[] logXldSig = Encoding.ASCII.GetBytes ("X Lossless Decoder version ");
        public LogEacTrack.Vector Tracks { get; private set; }

        public string XldVersionText { get; private set; }
        public string RipDate { get; private set; }
        public string RipArtist { get; private set; }
        public string RipAlbum { get; private set; }
        public string RipArtistAlbum { get { return RipArtist + " / " + RipAlbum; } }

        private string storedHash = null;
        public string StoredHash { get { return storedHash; } }

        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (scope <= Granularity.Detail && report.Count > 0)
                report.Add (String.Empty);

            report.Add ("XLD version = " + XldVersionText);
            report.Add ("Signature = " + (StoredHash?? "(missing)"));
            report.Add ("Rip date = " + RipDate);
            report.Add ("Rip artist = " + RipArtist);
            report.Add ("Rip album = " + RipAlbum);
        }
    }
}

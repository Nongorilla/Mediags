using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using NongIssue;

namespace NongFormat
{
    /// <summary>
    /// Provides properties and methods for reporting on and manipulating MPEG4 audio-visual files.
    /// See xhelmboyx.tripod.com/formats/mp4-layout.txt
    /// </summary>
    public abstract class Mpeg4Container : FormatBase
    {
        public abstract class Model : FormatBase.ModelBase
        {
            public new Mpeg4Container Data => (Mpeg4Container) _data;

            protected Model()
            { }

            protected void ParseMpeg4 (Stream stream, byte[] buf, string path)
            {
                int got;

                Data.Brand = Encoding.ASCII.GetString (buf, 8, 4).Trim();

                for (;;)
                {
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse (buf, 0, 4);
                    long size = BitConverter.ToInt32 (buf, 0);

                    if (size == 1)
                    {
                        if (Data.ValidSize + 16 > Data.FileSize)
                        {
                            IssueModel.Add ("File corrupt or truncated near wide box", Severity.Error);
                            return;
                        }

                        // Special value of 1 indicates wide box (ulong) so get it.
                        Data.fbs.Position = Data.ValidSize + 8;
                        got = Data.fbs.Read (buf, 0, 8);
                        if (got != 8)
                        {
                            IssueModel.Add ("Read failed near wide box", Severity.Fatal);
                            return;
                        }

                        if (BitConverter.IsLittleEndian)
                            Array.Reverse (buf, 0, 8);
                        // By the specs, this would be a UInt64, but Position expects signed longs.
                        size = BitConverter.ToInt64 (buf, 0);
                        if (size < 0)
                        {
                            IssueModel.Add ("Size insanely large", Severity.Fatal);
                            return;
                        }
                        ++Data.Wide;
                    }

                    if (Data.ValidSize + size > Data.FileSize)
                    {
                        IssueModel.Add ("File truncated", Severity.Fatal);
                        break;
                    }

                    Data.ValidSize += size;
                    if (Data.ValidSize + 8 >= Data.FileSize)
                        break;

                    Data.fbs.Position = Data.ValidSize;
                    got = Data.fbs.Read (buf, 0, 8);
                    if (got != 8)
                    {
                        IssueModel.Add ("Read failed", Severity.Fatal);
                        return;
                    }

                    if (buf[4]=='m' && buf[5]=='o' && buf[6]=='o' && buf[7]=='v')
                        ++Data.Moov;
                    else if (buf[4]=='m' && buf[5]=='d' && buf[6]=='a' && buf[7]=='t')
                        ++Data.Mdat;
                    else if (buf[4]=='f' && buf[5]=='r' && buf[6]=='e' && buf[7]=='e')
                        ++Data.Free;
                    else if (buf[4]==0 && buf[5]==0 && buf[6]==0 && buf[7]==0)
                    {
                        if (buf[0]==0 && buf[1]==0 && buf[2]==0 && buf[3]==0)
                        {
                            if (Data.FileSize - Data.ValidSize < 16)
                                break;

                            Data.fbs.Position = Data.ValidSize + 8;
                            got = Data.fbs.Read (buf, 0, 8);
                            if (got != 8)
                            {
                                IssueModel.Add ("Read failed", Severity.Fatal);
                                return;
                            }
                            if (buf[4]=='m' && buf[5]=='d' && buf[6]=='a' && buf[7]=='t')
                            {
                                // Sometimes there is a dummy header of 8 bytes of 0, then an mdat header.
                                IssueModel.Add ("Has dummy mdat header", Severity.Trivia);
                                ++Data.Mdat;
                                Data.ValidSize += 8;
                            }
                            else
                                // Can't parse so bail.
                                break;
                        }
                    }
                    else
                        // Can't parse so bail.
                        break;
                }
            }


            protected void GetDiagnostics()
            {
                if (Data.Moov == 0)
                    IssueModel.Add ("Missing 'moov' section.", Severity.Error);

                if (Data.Mdat == 0)
                    IssueModel.Add ("Missing 'mdat' section.", Severity.Error);

                // Wide boxes are rare.
                if (Data.Wide != 0)
                    IssueModel.Add ($"Number of wide boxes={Data.Wide}.", Severity.Trivia);

                if (Data.ExcessSize == 0)
                {
                    var unparsedSize = Data.FileSize - Data.ValidSize;
                    if (unparsedSize != 0)
                        IssueModel.Add ($"Possible watermark, size={unparsedSize}.", Severity.Advisory);
                }
            }
        }

        public int Moov { get; private set; }
        public int Mdat { get; private set; }
        public int Free { get; private set; }
        public int Wide { get; private set; }
        public string Brand { get; private set; }


        protected Mpeg4Container (Model model, Stream stream, string path) : base (model, stream, path)
        { }

        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (report.Count != 0)
                report.Add (String.Empty);

            report.Add ($"Brand = {Brand}");

            if (scope <= Granularity.Detail)
            {
                report.Add ($"moov blocks = {Moov}");
                report.Add ($"mdat blocks = {Mdat}");
                report.Add ($"free blocks = {Free}");
            }
        }
    }
}

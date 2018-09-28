using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    // This factory is special because .mpg files are commonly one of two very different looking files:
    // 1. MPEG-1 in a RIFF container
    // 2. MPEG-2 using a codec-specific container
    //
    public class Mpeg2Format : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "mpg", "mpeg" , "vob" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static FormatBase.ModelBase CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x28)
                if (hdr[0]==0 && hdr[1]==0 && hdr[2]==1 && (hdr[3]==0xB3 || hdr[3]==0xBA))
                    return new Model (stream, hdr, path);
                else if (hdr[0x00]=='R' && hdr[0x01]=='I' && hdr[0x02]=='F' && hdr[0x03]=='F'
                      && hdr[0x08]=='C' && hdr[0x09]=='D' && hdr[0x0A]=='X' && hdr[0x0B]=='A'
                      && hdr[0x24]=='d' && hdr[0x25]=='a' && hdr[0x26]=='t' && hdr[0x27]=='a')
                    return new Mpeg1Format.Model (stream, hdr, path);
            return null;
        }


        private class Model : FormatBase.ModelBase
        {
            public new readonly Mpeg2Format Data;

            public Model (Stream stream, byte[] hdr, string path)
            {
                base._data = Data = new Mpeg2Format (stream, path);
                Data.Issues = IssueModel.Data;

                if (Data.FileSize < 12)
                {
                    IssueModel.Add ("File truncated near start", Severity.Error);
                    return;
                }

                Data.IsVOB = hdr[3] == 0xBA;

                // TODO parse contents for watermark/truncation
                Data.ValidSize = Data.FileSize;

                long loopStop = Data.FileSize-36;
                if (loopStop < 8)
                    loopStop = 8;
                for (long ePos = Data.FileSize-4; ePos > loopStop; --ePos)
                {
                    Data.fbs.Position = ePos;
                    var got = Data.fbs.Read (hdr, 0, 4);
                    if (got != 4)
                    {
                        IssueModel.Add ("Read error looking for trailer", Severity.Fatal);
                        return;
                    }

                    if (hdr[0]==0 && hdr[1]==0 && hdr[2]==1 && hdr[3]==0xB9)
                    {
                        // 20 bytes of zero seem typical .mpg pad, so can't do this watermark calculation
                        Data.trailerPos = ePos;
                        Data.ValidSize = ePos + 4;
                        break;
                    }
                }

                if (Data.trailerPos < 0)
                    IssueModel.Add ("No trailermark found.", Severity.Trivia);
                else
                {
                    long unparsedSize = Data.FileSize - Data.ValidSize;
                    if (unparsedSize != 0)
                        IssueModel.Add ("Possible watermark, size=" + unparsedSize, Severity.Trivia);
                }
            }
        }


        public bool IsVOB { get; private set; }
        private long trailerPos = -1;


        private Mpeg2Format (Stream stream, string path) : base (stream, path)
        { }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            report.Add ("Format = MPEG-2");
            report.Add ("Is VOB = " + IsVOB);
        }
    }
}

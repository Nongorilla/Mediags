using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public abstract class RiffContainer : FormatBase
    {
        public abstract class Model : FormatBase.ModelBase
        {
            public new RiffContainer Data => (RiffContainer) _data;

            protected void ParseRiff (byte[] hdr)
            {
                var buf = new byte[8];
                long chunkSize = ConvertTo.FromLit32ToUInt32 (hdr, 4) + 8;

                if (chunkSize % 1 != 0)
                    IssueModel.Add ("RIFF has odd sized chunk, some tools don't pad this correctly", Severity.Trivia);

                do
                {
                    if (Data.ValidSize + chunkSize > Data.FileSize)
                    {
                        IssueModel.Add ("File truncated", Severity.Fatal);
                        return;
                    }

                    ++Data.RiffChunkCount;
                    Data.RiffSize = Data.ValidSize = Data.ValidSize + chunkSize;

                    if (Data.ValidSize + 8 > Data.FileSize)
                        // Not enough bytes for a header.
                        return;

                    try
                    {
                        Data.fbs.Position = Data.ValidSize;
                    }
                    catch (EndOfStreamException)
                    {
                        IssueModel.Add ("File truncated or corrupt", Severity.Fatal);
                        return;
                    }
                    var got = Data.fbs.Read (buf, 0, 8);
                    if (got != 8)
                    {
                        IssueModel.Add ("Read error", Severity.Fatal);
                        return;
                    }
                    chunkSize = ConvertTo.FromLit32ToUInt32 (buf, 4) + 8;
                }
                while (buf[0]=='R' || buf[1]=='I' || buf[2]=='F' || buf[3]=='F');

                if (buf[0]=='J' && buf[1]=='U' && buf[2]=='N' && buf[3]=='K')
                {
                    if (Data.ValidSize + chunkSize > Data.FileSize)
                    {
                        IssueModel.Add ("File corrupt or truncated", Severity.Fatal);
                        return;
                    }

                    Data.JunkSize = chunkSize;
                    Data.ValidSize += Data.JunkSize;
                }
            }

            protected void GetRiffDiagnostics()
            {
                if (Data.RiffSize <= 12)
                    IssueModel.Add ("Missing data", Severity.Error);

                long unparsedSize = Data.FileSize - Data.ValidSize - Data.ExcessSize;
                if (unparsedSize != 0)
                    IssueModel.Add ("Unrecognized bytes at end = " + unparsedSize, Severity.Warning);
            }

            protected void GetDiagsForMarkable()
            {
                if (Data.RiffSize <= 12)
                    IssueModel.Add ("Missing data", Severity.Error);

                if (Data.ExcessSize == 0)
                {
                    var unparsedSize = base.Data.FileSize - base.Data.ValidSize;
                    if (unparsedSize > 0)
                        IssueModel.Add ("Possible watermark, size=" + unparsedSize, Severity.Trivia);
                }
            }
        }

        public long RiffSize { get; private set; }
        public int RiffChunkCount { get; private set; }
        public long JunkSize { get; private set; }

        public long ExpectedPaddedSize
        { get { return ((ValidSize - JunkSize + 2048 + 8) / 2048) * 2048; } }

        protected RiffContainer (Model model, Stream stream, string path) : base (model, stream, path)
        { }

        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (scope > Granularity.Detail)
                return;

            report.Add ("RIFF size = " + RiffSize);

            if (JunkSize > 0)
                report.Add ("JUNK size = " + JunkSize);

            if (RiffChunkCount != 1)
                report.Add ("RIFF chunks = " + RiffChunkCount);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    // en.wikipedia.org/wiki/Flv#Flash_Video_Structure
    public class FlvFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "flv" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 9
                    && hdr[0x00]=='F' && hdr[0x01]=='L' && hdr[0x02]=='V' && hdr[0x03]==1)
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public readonly FlvFormat Bind;

            public Model (Stream stream, byte[] hdr, string path)
            {
                BaseBind = Bind = new FlvFormat (stream, path);
                Bind.Issues = IssueModel.Data;

                var bb = new byte[15];

                Bind.flags = hdr[4];
                if ((Bind.flags & 0xA) != 0)
                    IssueModel.Add ("Unexpected flags.");
                if ((Bind.flags & 5) == 0)
                    IssueModel.Add ("Missing audio and video.");

                UInt32 hdrSize = ConvertTo.FromBig32ToUInt32 (hdr, 5);
                if (hdrSize != 9)
                    IssueModel.Add ("Wrong header size.");

                Bind.mediaPosition = 9;
                UInt32 actualPrevSize = 0;

                while (Bind.mediaPosition < Bind.FileSize)
                {
                    if (Bind.mediaPosition + 15 > Bind.FileSize)
                    { IssueModel.Add ("File truncated near packet header.", Severity.Fatal); return; }

                    Bind.fbs.Position = Bind.mediaPosition;
                    var got = Bind.fbs.Read (bb, 0, bb.Length);
                    if (got < bb.Length)
                    { IssueModel.Add ("Read error", Severity.Fatal); return; }

                    Bind.mediaPosition += 15;

                    UInt32 storedPrevSize = ConvertTo.FromBig32ToUInt32 (bb, 0);
                    if (storedPrevSize != actualPrevSize)
                        IssueModel.Add ("Bad previous packet size.");

                    byte packetType = bb[4];
                    UInt32 packetSize = ConvertTo.FromBig24ToUInt32 (bb, 5);
                    actualPrevSize = packetSize + 11;

                    ++Bind.PacketCount;
                    Bind.mediaPosition += packetSize;
                }

                if (Bind.mediaPosition > Bind.FileSize)
                    IssueModel.Add ("File truncated.", Severity.Fatal);
            }
        }


        private byte flags;
        public bool HasVideo { get { return (flags & 1) != 0; } }
        public bool HasAudio { get { return (flags & 4) != 0; } }
        public int PacketCount { get; private set; }


        private FlvFormat (Stream stream, string path) : base (stream, path)
        { }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (report.Count != 0)
                report.Add (String.Empty);

            report.Add ("Has audio = " + HasAudio);
            report.Add ("Has video = " + HasVideo);
            if (scope <= Granularity.Detail)
                report.Add ("Packet count = " + PacketCount);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.IO;
using NongIssue;
using NongCrypto;

// www.w3.org/TR/PNG-Structure.html

namespace NongFormat
{
    public class PngFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "png" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x28
                    && hdr[0x00]==0x89 && hdr[0x01]=='P' && hdr[0x02]=='N' && hdr[0x03]=='G'
                    && hdr[0x04]==0x0D && hdr[0x05]==0x0A && hdr[0x06]==0x1A && hdr[0x07]==0x0A)
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public readonly PngFormat Bind;
            public readonly PngChunk.Vector.Model ChunksModel;

            public Model (Stream stream, byte[] header, string path)
            {
                ChunksModel = new PngChunk.Vector.Model();
                BaseBind = Bind = new PngFormat (stream, path, ChunksModel.Bind);
                Bind.Issues = IssueModel.Bind;

                // Arbitrary sanity limit.
                if (Bind.FileSize > 100000000)
                {
                    IssueModel.Add ("File size insanely huge.", Severity.Fatal);
                    return;
                }

                Bind.fBuf = new byte[(int) Bind.FileSize];
                var fBuf = Bind.fBuf;

                stream.Position = 0;
                int got = stream.Read (fBuf, 0, (int) Bind.FileSize);
                if (got < Bind.FileSize)
                {
                    IssueModel.Add ("Read failed.", Severity.Fatal);
                    return;
                }

                Bind.ValidSize = 8;

                if (fBuf[0x0C]!='I' || fBuf[0x0D]!='H' || Bind.fBuf[0x0E]!='D' || Bind.fBuf[0x0F]!='R')
                {
                    IssueModel.Add ("Missing 'IHDR' chunk.", Severity.Fatal);
                    return;
                }

                Bind.Width = ConvertTo.FromBig32ToInt32 (fBuf, 0x10);
                Bind.Height = ConvertTo.FromBig32ToInt32 (fBuf, 0x14);
                Bind.BitDepth = fBuf[0x18];
                Bind.ColorType = fBuf[0x19];
                Bind.CompressionMethod = fBuf[0x1A];
                Bind.FilterMethod = fBuf[0x1B];
                Bind.InterlaceMethod = fBuf[0x1C];

                do
                {
                    UInt32 chunkSize = ConvertTo.FromBig32ToUInt32 (fBuf, (int) Bind.ValidSize);
                    if (Bind.ValidSize + chunkSize + 12 > Bind.FileSize)
                    {
                        IssueModel.Add ("File is corrupt or truncated.", Severity.Fatal);
                        return;
                    }

                    string type = Encoding.ASCII.GetString (fBuf, (int) Bind.ValidSize+4, 4);
                    UInt32 storedCRC = ConvertTo.FromBig32ToUInt32 (fBuf, (int) (Bind.ValidSize + chunkSize + 8));
                    ChunksModel.Add (type, chunkSize, storedCRC);

                    var typeLow = type.ToLower();
                    switch (typeLow)
                    {
                        case "idat":
                            if (Bind.mediaPosition <= 0)
                                Bind.mediaPosition = Bind.ValidSize;
                            break;
                        case "iend":
                            if (Bind.MediaCount > 0)
                                IssueModel.Add ("Multiple IEND chunks.");
                            else
                                Bind.MediaCount = Bind.ValidSize - Bind.mediaPosition + chunkSize + 0xC;
                            break;
                        case "text":
                            if (chunkSize > 0x7FFF)
                                IssueModel.Add ("String size too large.");
                            else
                            {
                                var escaped = new StringBuilder();
                                for (int ix = (int) Bind.ValidSize+8; ix < (int) Bind.ValidSize+8+chunkSize; ++ix)
                                    if (fBuf[ix] < ' ' || fBuf[ix] > 0x7F)
                                        escaped.AppendFormat ("\\{0:x2}", fBuf[ix]);
                                    else
                                        escaped.Append ((char) fBuf[ix]);

                                Bind.texts.Add (escaped.ToString());
                            }
                            break;
                        case "gama":
                            if (Bind.Gamma != null)
                                IssueModel.Add ("Unexpected multiple gamma chunks.");
                            else
                                if (chunkSize != 4)
                                    IssueModel.Add ("Bad gamma chunk size '" + chunkSize + "', expecting '4'.");
                                else
                                    Bind.Gamma = ConvertTo.FromBig32ToUInt32 (fBuf, (int) Bind.ValidSize+8) / 100000f;
                            break;
                    }

                    Bind.ValidSize += chunkSize + 0xC;
                }
                while (Bind.ValidSize < Bind.FileSize);

                if (Bind.Chunks.Items[Bind.Chunks.Items.Count-1].Type != "IEND")
                    IssueModel.Add ("Missing 'IEND' chunk.");

                if (Bind.Width <= 0 || Bind.Height <= 0)
                    IssueModel.Add ("Invalid dimensions.");

                if (Bind.BitDepth != 1 && Bind.BitDepth != 2 && Bind.BitDepth != 4 && Bind.BitDepth != 8 && Bind.BitDepth != 16)
                    IssueModel.Add ("Invalid bit depth '" + Bind.BitDepth + "'.");

                if (Bind.CompressionMethod != 0)
                    IssueModel.Add ("Invalid compression '" + Bind.CompressionMethod + "'.");

                if (Bind.FilterMethod != 0)
                    IssueModel.Add ("Invalid filter '" + Bind.FilterMethod + "'.");

                if (Bind.InterlaceMethod != 0 && Bind.InterlaceMethod != 1)
                    IssueModel.Add ("Invalid interlace '" + Bind.InterlaceMethod + "'.");
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if ((hashFlags & Hashes.Intrinsic) != 0 && Bind.BadCrcCount == null)
                {
                    Bind.BadCrcCount = 0;
                    var hasher = new Crc32rHasher();
                    int pos = 12;
                    for (int ix = 0; ix < Bind.Chunks.Items.Count; ++ix)
                    {
                        PngChunk chunk = Bind.Chunks.Items[ix];
                        hasher.Append (Bind.fBuf, pos, (int) chunk.Size + 4);
                        byte[] hash = hasher.GetHashAndReset();
                        UInt32 actualCRC = BitConverter.ToUInt32 (hash, 0);
                        ChunksModel.SetActualCRC (ix, actualCRC);

                        if (actualCRC != chunk.StoredCRC)
                            ++Bind.BadCrcCount;
                        pos += (int) chunk.Size + 12;
                    }

                    var sb = new StringBuilder();
                    sb.Append (Bind.Chunks.Items.Count - Bind.BadCrcCount);
                    sb.Append (" of ");
                    sb.Append (Bind.Chunks.Items.Count);
                    sb.Append (" CRC checks successful.");
                    IssueModel.Add (sb.ToString(), Bind.BadCrcCount==0? Severity.Noise : Severity.Error);
                }

                base.CalcHashes (hashFlags, validationFlags);
            }
        }

        public readonly PngChunk.Vector Chunks;
        private readonly ObservableCollection<string> texts;
        public ReadOnlyObservableCollection<string> Texts { get; private set; }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte BitDepth { get; private set; }
        public byte ColorType { get; private set; }
        public byte CompressionMethod { get; private set; }
        public byte FilterMethod { get; private set; }
        public byte InterlaceMethod { get; private set; }
        public float? Gamma { get; private set; }
        public int? BadCrcCount { get; private set; }

        public string ColorTypeText
        {
            get
            {
                if (ColorType == 1) return "Palette";
                else if (ColorType == 2) return "Color";
                else if (ColorType == 4) return "Alpha";
                else if (ColorType == 6) return "Color+alpha";
                else return ColorType.ToString();
            }
        }


        public PngFormat (Stream stream, string path, PngChunk.Vector chunks) : base (stream, path)
        {
            this.BadCrcCount = null;

            this.Chunks = chunks;

            this.texts = new ObservableCollection<string>();
            this.Texts = new ReadOnlyObservableCollection<string> (this.texts);
        }


        public override bool IsBadData
        { get { return BadCrcCount != null && BadCrcCount.Value != 0; } }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            report.Add ("Dimensions = " + Width + 'x' + Height);
            report.Add ("Color type = " + ColorType + " (" + ColorTypeText + ')');

            if (scope <= Granularity.Detail)
            {
                report.Add ("Gamma = " + (Gamma == null? "None" : Gamma.ToString()));
                report.Add ("Bit depth = " + BitDepth);
                report.Add ("Interlace method = " + InterlaceMethod);
            }

            if (Texts.Count > 0)
            {
                if (scope <= Granularity.Detail)
                    report.Add (String.Empty);
                report.Add ("Text:");
                foreach (string text in Texts)
                    report.Add ("  " + text);
            }

            if (scope <= Granularity.Detail)
            {
                report.Add (String.Empty);
                var sb = new StringBuilder();
                int num = 0;
                foreach (PngChunk chunk in Chunks.Items)
                {
                    ++num;
                    sb.Clear();
                    sb.Append ("Chunk ");
                    sb.Append (num.ToString());
                    sb.AppendLine (":");
                    sb.Append ("  type = ");
                    sb.AppendLine (chunk.Type);
                    sb.Append ("  size = ");
                    sb.AppendLine (chunk.Size.ToString());
                    sb.AppendFormat ("  stored CRC-32 = 0x{0:X8}", chunk.StoredCRC);
                    sb.AppendLine();
                    sb.Append ("  actual CRC-32 = ");
                    if (chunk.ActualCRC == null)
                        sb.Append ('?');
                    else
                        sb.AppendFormat ("0x{0:X8}", chunk.ActualCRC.Value);
                    report.Add (sb.ToString());
                }
            }
        }
    }
}

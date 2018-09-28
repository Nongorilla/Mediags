using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public class IconItem
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int PaletteSize { get; private set; }
        public int BitsPerPixel { get; private set; }
        public int Size { get; private set; }
        public int Offset { get; private set; }
        public bool IsPNG { get; private set; }

        public IconItem (int width, int height, int paletteSize, int bitsPerPixel, int size, int offset, bool isPNG)
        {
            this.Width = width;
            this.Height = height;
            this.PaletteSize = paletteSize;
            this.BitsPerPixel = bitsPerPixel;
            this.Size = size;
            this.Offset = offset;
            this.IsPNG = isPNG;
        }
    }


    // en.wikipedia.org/wiki/ICO_(file_format)
    // daubnet.com/en/file-format-ico
    public class IcoFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "ico" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 4 && hdr[0]==0 && hdr[1]==0 && hdr[2]==1 && hdr[3]==0)
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public new readonly IcoFormat Data;

            public Model (Stream stream, byte[] header, string path)
            {
                base._data = Data = new IcoFormat (stream, path);
                Data.Issues = IssueModel.Data;

                // Arbitrary sanity limit.
                if (Data.FileSize > 50000000)
                {
                    IssueModel.Add ("File insanely large", Severity.Fatal);
                    return;
                }

                var buf = new byte[Data.FileSize];
                stream.Position = 0;
                var got = stream.Read (buf, 0, (int) Data.FileSize);
                if (got != Data.FileSize)
                {
                    IssueModel.Add ("Read error", Severity.Fatal);
                    return;
                }

                int headerCount = ConvertTo.FromLit16ToInt32 (buf, 4);

                if (headerCount <= 0)
                {
                    IssueModel.Add ("Corrupt header count", Severity.Fatal);
                    return;
                }

                int pos = 6;
                int stop = pos + 16 * headerCount;
                int actualStart = stop;

                for (pos = 6; pos < stop; pos += 16)
                {
                    int width, height, bpp, paletteSize;
                    int storedSize = ConvertTo.FromLit32ToInt32 (buf, pos+8);
                    int storedStart = ConvertTo.FromLit32ToInt32 (buf, pos+12);

                    if (storedStart != actualStart || storedSize <= 0)
                    {
                        IssueModel.Add ("Corrupt header near byte " + Data.ValidSize, Severity.Fatal);
                        return;
                    }

                    bool isPNG = buf[actualStart]==0x89 && buf[actualStart+1]=='P' && buf[actualStart+2]=='N' && buf[actualStart+3]=='G';

                    if (isPNG)
                    {
                        width = ConvertTo.FromBig32ToInt32 (buf, actualStart + 16);
                        height = ConvertTo.FromBig32ToInt32 (buf, actualStart + 20);
                        paletteSize = 0;
                        bpp = buf[actualStart + 24];
                    }
                    else
                    {
                        width = buf[pos];
                        if (width == 0)
                            width = 256;

                        height = buf[pos+1];
                        if (height == 0)
                            height = 256;

                        paletteSize = buf[pos+2];
                        bpp = buf[pos+6];
                    }

                    Data.icons.Add (new IconItem (width, height, paletteSize, bpp, storedStart, storedSize, isPNG));

                    actualStart += storedSize;
                }

                if (actualStart != Data.FileSize)
                {
                    IssueModel.Add ("Incorrect file size");
                    return;
                }
            }
        }


        private readonly List<IconItem> icons;
        public ReadOnlyCollection<IconItem> Icons { get; private set; }
        public int Count { get { return icons.Count; } }


        private IcoFormat (Stream stream, string path) : base (stream, path)
        {
            this.icons = new List<IconItem>();
            this.Icons = new ReadOnlyCollection<IconItem> (this.icons);
        }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (report.Count > 0)
                report.Add (String.Empty);

            report.Add ("Image count = " + Icons.Count);

            if (scope == Granularity.Detail)
            {
                report.Add (String.Empty);
                report.Add ("Layout:");
                foreach (var item in Icons)
                {
                    string lx = item.IsPNG? "  PNG" : "  BMP" + String.Format (": dimensions={0}x{1}", item.Width, item.Height);
                    if (! item.IsPNG)
                        lx += String.Format (", palette={0}, bpp={1}", item.PaletteSize, item.BitsPerPixel);
                    report.Add (lx);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using NongIssue;

// en.wikipedia.org/wiki/JPEG_File_Interchange_Format
// www.media.mit.edu/pia/Research/deepview/exif.html

namespace NongFormat
{
    public enum JpegUnits { None, Inch, Centimeter }
    public enum JpegApps { None, Jfif, Exif }

    public class JpegFormat : FormatBase
    {
        public static string[] Names
         => new string[] { "jpg", "jpeg" };

        public override string[] ValidNames
         => Names;

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x20 && hdr[0]==0xFF && hdr[1]==0xD8 && hdr[2]==0xFF)
                return new Model (stream, path);
           return null;
        }


        public new class Model : FormatBase.Model
        {
            public new readonly JpegFormat Data;

            public Model (Stream stream, string path)
            {
                base._data = Data = new JpegFormat (this, stream, path);

                // Arbitrary choice of 50MB cutoff.
                if (Data.FileSize > 50000000)
                {
                    IssueModel.Add ("File insanely huge", Severity.Fatal);
                    return;
                }

                byte[] buf = new byte[(int) Data.FileSize];

                stream.Position = 0;
                int got = stream.Read (buf, 0, (int) Data.FileSize);
                if (got != Data.FileSize)
                {
                    IssueModel.Add ("Read error", Severity.Fatal);
                    return;
                }

                for (int len = 0, pos = 2; ; pos = pos + len + 2)
                {
                    if (pos > Data.FileSize - 4)
                    {
                        IssueModel.Add ("File truncated", Severity.Fatal);
                        return;
                    }

                    if (buf[pos] != 0xFF)
                    {
                        IssueModel.Add ("Missing marker.", Severity.Fatal);
                        return;
                    }

                    if (buf[pos+1] == 0xD9)
                    {
                        IssueModel.Add ("Unexpected EOI marker", Severity.Fatal);
                        return;
                    }

                    // Detect SOS (Start of Stream) marker.
                    if (buf[pos+1] == 0xDA)
                    {
                        Data.sosPos = pos;
                        break;
                    }

                    len = (buf[pos+2] << 8) + buf[pos+3];
                    if (len < 2)
                    {
                        IssueModel.Add ("Invalid segment length '" + len + "'.", Severity.Fatal);
                        return;
                    }

                    if (pos+len+2 >= Data.FileSize)
                    {
                        IssueModel.Add ("File truncated.", Severity.Fatal);
                        return;
                    }

                    if (len == 2)
                        IssueModel.Add ("Contains empty segment.", Severity.Trivia);

                    ++Data.SegmentCount;

                    // Detect application marker:
                    if (buf[pos+1]>=0xE0 && buf[pos+1]<=0xEF)
                    {
                        string appName = ConvertTo.FromAsciizToString (buf, pos+4);
                        Data.appNames.Add (appName);

                        if (buf[pos+1] == 0xE0)
                        {
                            if (buf[pos+4]!='J' || buf[pos+5]!='F' || buf[pos+6]!='I' || buf[pos+7]!='F' || buf[pos+8]!=0)
                                continue;

                            if ((Data.Apps & JpegApps.Jfif) != 0)
                            {
                                IssueModel.Add ("Contains ghost JFIF segment.", Severity.Advisory);
                                continue;
                            }

                            if (len < 13)
                            {
                                IssueModel.Add ("JFIF segment too small.", Severity.Fatal);
                                return;
                            }

                            Data.Apps |= JpegApps.Jfif;
                            Data.VersionMajor = buf[pos+0x09];
                            Data.VersionMinor = buf[pos+0x0A];
                            Data.Units = (JpegUnits) buf[pos+0x0B];
                            Data.DensityX = (buf[pos+0x0C] << 8) + buf[pos+0x0D];
                            Data.DensityY = (buf[pos+0x0E] << 8) + buf[pos+0x0F];
                            Data.ThumbXLen = buf[pos+0x10];
                            Data.ThumbYLen = buf[pos+0x11];
                        }
                        else if (buf[pos+1] == 0xE1)
                        {
                            if (buf[pos+4]=='E' && buf[pos+5]=='x' && buf[pos+6]=='i' && buf[pos+7]=='f' && buf[pos+8]==0)
                            {
                                if ((Data.Apps & JpegApps.Exif) != 0)
                                {
                                    IssueModel.Add ("Contains ghost Exif segment", Severity.Trivia);
                                    continue;
                                }

                                Data.Apps |= JpegApps.Exif;
                                // Additional Exif parsing goes here...
                            }
                        }
                    }
                }

                // Detect EOI (end of image) marker: FFD9
                for (int pos = (int) Data.sosPos; pos < Data.FileSize - 1; ++pos)
                    if (buf[pos]==0xFF && buf[pos+1]==0xD9)
                    {
                        Data.ValidSize = pos + 2;
                        Data.eoiPos = pos;
                        break;
                    }

                CalcMark();
                GetDiagnostics();
            }

            private void GetDiagnostics()
            {
                if ((Data.Apps & JpegApps.Jfif) != 0)
                {
                    // This spec is often violated.
                    if (Data.DensityX == 0 || Data.DensityY == 0)
                        IssueModel.Add ($"Invalid JFIF density of {Data.DensityX}x{Data.DensityY}", Severity.Trivia);

                    if ((int) Data.Units > 2)
                        IssueModel.Add ($"Invalid JFIF units of {Data.Units}", Severity.Warning);
                }

                if (Data.eoiPos == null)
                    IssueModel.Add ("Missing end marker");
                else
                    if (Data.ExcessSize == 0)
                    {
                        var unparsedSize = Data.FileSize - Data.ValidSize;
                        if (unparsedSize > 0)
                            IssueModel.Add ($"Possible watermark, size={unparsedSize}", Severity.Advisory);
                    }
            }
        }


        private int? eoiPos = null, sosPos = null;
        private readonly List<string> appNames;
        public ReadOnlyCollection<string> AppNames { get; private set; }

        public JpegApps Apps { get; private set; }
        public int VersionMajor { get; private set; }
        public int VersionMinor { get; private set; }
        public JpegUnits Units { get; private set; }
        public int DensityX { get; private set; }
        public int DensityY { get; private set; }
        public int ThumbXLen { get; private set; }
        public int ThumbYLen { get; private set; }
        public int SegmentCount { get; private set; }

        private JpegFormat (Model model, Stream stream, string path) : base (model, stream, path)
        {
            this.appNames = new List<string>();
            this.AppNames = new ReadOnlyCollection<string> (this.appNames);
        }

        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (report.Count > 0 && scope <= Granularity.Detail)
                report.Add (String.Empty);

            report.Add ("Applications:");
            foreach (var name in AppNames)
                report.Add ("  " + name);

            if (scope <= Granularity.Detail)
            {
                if (scope <= Granularity.Detail)
                    report.Add (String.Empty);
                report.Add ($"Header segments = {SegmentCount}");
                if ((Apps & JpegApps.Jfif) != 0)
                {
                    report.Add ("JFIF:");
                    report.Add ($"  Version = {VersionMajor}.{VersionMinor}");
                    report.Add ($"  Density = {DensityX}x{DensityY}");
                    report.Add ($"  Density units = {Units}");
                    report.Add ($"  Thumbnail size = {ThumbXLen}x{ThumbYLen}");
                }
            }
        }
    }
}

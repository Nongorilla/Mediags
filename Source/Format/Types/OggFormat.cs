using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using NongIssue;
using NongCrypto;

namespace NongFormat
{
    // xiph.org/ogg/
    // www.ietf.org/rfc/rfc3533.txt
    public class OggFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "ogg" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x28 && hdr[0]=='O' && hdr[1]=='g' && hdr[2]=='g' && hdr[3]=='S' && hdr[4]==0)
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public readonly OggFormat Bind;

            public Model (Stream stream, byte[] header, string path)
            {
                BaseBind = Bind = new OggFormat (stream, path);
                Bind.Issues = IssueModel.Bind;

                var buf1 = new byte[27+256];
                byte[] buf2 = null;

                while (Bind.ValidSize < Bind.FileSize)
                {
                    Bind.fbs.Position = Bind.ValidSize;
                    ++Bind.PageCount;
                    var got = Bind.fbs.Read (buf1, 0, buf1.Length);
                    if (got < buf1.Length)
                    {
                        IssueModel.Add ("Read failed near header", Severity.Fatal);
                        return;
                    }

                    int segmentCount = buf1[26];
                    UInt32 storedHeaderCRC32 = ConvertTo.FromLit32ToUInt32 (buf1, 22);

                    int pageSize = 27 + segmentCount;
                    for (int ix = 27; ix < 27 + segmentCount; ++ix)
                        pageSize += buf1[ix];

                    Bind.fbs.Position = Bind.ValidSize;

                    if (buf2 == null || buf2.Length < pageSize)
                        buf2 = new byte[pageSize];
                    got = Bind.fbs.Read (buf2, 0, pageSize);
                    if (got < pageSize)
                    {
                        IssueModel.Add ("Read failed near page " + Bind.PageCount, Severity.Fatal);
                        return;
                    }

                    buf2[22] = 0; buf2[23] = 0; buf2[24] = 0; buf2[25] = 0;

                    UInt32 actualHeaderCRC32;
                    var hasher = new Crc32n0Hasher();
                    hasher.Append (buf2, 0, pageSize);
                    var hash = hasher.GetHashAndReset();
                    actualHeaderCRC32 = BitConverter.ToUInt32 (hash, 0);

                    if (actualHeaderCRC32 != storedHeaderCRC32)
                        Bind.badPage.Add (Bind.PageCount);

                    Bind.ValidSize += pageSize;
                }

                if (Bind.badPage.Count > 0)
                    Bind.CdIssue = IssueModel.Add ("Page CRC-32 mismatch(es).", Severity.Error, IssueTags.Failure);
                else
                    Bind.CdIssue = IssueModel.Add ($"{Bind.PageCount} CRC-32 page validations successful.", Severity.Advisory, IssueTags.Success);
            }
        }

        private ObservableCollection<int> badPage = new ObservableCollection<int>();
        public Issue CdIssue { get; private set; }
        public int PageCount { get; private set; }
        public int GoodPageCount => PageCount - badPage.Count;
        public override bool IsBadData => badPage.Count != 0;


        private OggFormat (Stream stream, string path) : base (stream, path)
        { }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            report.Add ($"Total pages = {PageCount}");

            if (scope <= Granularity.Detail && badPage.Count != 0)
                foreach (var pageNum in badPage)
                    report.Add ($"CRC-32 mismatch on page {pageNum}.");
        }
    }
}

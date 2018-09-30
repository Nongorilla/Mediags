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
        public static string[] Names => new string[] { "ogg" };
        public override string[] ValidNames => Names;

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (hdr.Length >= 0x28 && hdr[0]=='O' && hdr[1]=='g' && hdr[2]=='g' && hdr[3]=='S' && hdr[4]==0)
                return new Model (stream, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public new readonly OggFormat Data;

            public Model (Stream stream, string path)
             => base._data = Data = new OggFormat (this, stream, path);

            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Data.Issues.HasFatal)
                    return;

                if ((hashFlags & Hashes.Intrinsic) != 0)
                {
                    var buf1 = new byte[27+256];
                    byte[] buf2 = null;
                    Data.PageCount = 0;

                    while (Data.ValidSize < Data.FileSize)
                    {
                        Data.fbs.Position = Data.ValidSize;
                        ++Data.PageCount;
                        var got = Data.fbs.Read (buf1, 0, buf1.Length);
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

                        Data.fbs.Position = Data.ValidSize;

                        if (buf2 == null || buf2.Length < pageSize)
                            buf2 = new byte[pageSize];
                        got = Data.fbs.Read (buf2, 0, pageSize);
                        if (got < pageSize)
                        {
                            IssueModel.Add ("Read failed near page " + Data.PageCount, Severity.Fatal);
                            return;
                        }

                        buf2[22] = 0; buf2[23] = 0; buf2[24] = 0; buf2[25] = 0;

                        UInt32 actualHeaderCRC32;
                        var hasher = new Crc32n0Hasher();
                        hasher.Append (buf2, 0, pageSize);
                        var hash = hasher.GetHashAndReset();
                        actualHeaderCRC32 = BitConverter.ToUInt32 (hash, 0);

                        if (actualHeaderCRC32 != storedHeaderCRC32)
                            Data.badPage.Add (Data.PageCount.Value);

                        Data.ValidSize += pageSize;
                    }

                    if (Data.badPage.Count == 0)
                        Data.CdIssue = IssueModel.Add ($"CRC-32 checks successful on {Data.PageCount} pages.", Severity.Advisory, IssueTags.Success);
                    else
                    {
                        var err = $"CRC-32 checks failed on {Data.badPage.Count} of {Data.PageCount} pages.";
                        Data.CdIssue = IssueModel.Add (err, Severity.Error, IssueTags.Failure);
                    }
                }

                base.CalcHashes (hashFlags, validationFlags);
            }
        }


        private ObservableCollection<int> badPage = new ObservableCollection<int>();
        public Issue CdIssue { get; private set; }
        public int? PageCount { get; private set; } = null;
        public int? GoodPageCount => PageCount == null ? null : PageCount - badPage.Count;
        public override bool IsBadData => badPage.Count != 0;

        private OggFormat (Model model, Stream stream, string path) : base (model, stream, path)
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

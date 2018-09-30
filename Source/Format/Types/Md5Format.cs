using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;
using NongCrypto;

namespace NongFormat
{
    public class Md5Format : HashesContainer
    {
        public static string[] Names
         => new string[] { "md5" };

        public override string[] ValidNames
         => Names;

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.ToLower().EndsWith(".md5"))
                return new Model (stream, path);
            return null;
        }


        public new class Model : HashesContainer.Model
        {
            public new readonly Md5Format Data;

            public Model (Stream stream, string path) : base (path, 16)
            {
                base._data = Data = new Md5Format (this, stream, path);
                ParseHeaderAndHistory();
                ParseHashes();
            }

            public Model (Stream stream, string md5Path, LogEacFormat log, string logName, M3uFormat m3u, string signature) : base (md5Path, 16)
            {
                base._data = Data = new Md5Format (this, stream, md5Path);
                Data.fbs = stream;

                CreateHistory();
                HistoryModel.Add ("ripped", signature);

                HashedModel.AddActual (m3u.Name, m3u.FileMD5);
                HashedModel.AddActual (logName, log.FileMD5);
                foreach (var track in log.Tracks.Items)
                    HashedModel.AddActual (track.Match.Name, track.Match.FileMD5);
            }

            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Data.Issues.HasFatal)
                    return;

                if ((validationFlags & Validations.MD5) != 0)
                    ComputeContentHashes (new Md5Hasher());

                base.CalcHashes (hashFlags, validationFlags);
            }
        }


        private Md5Format (Model model, Stream stream, string path) : base (model, stream, path)
         => Validation = Validations.MD5;

        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            base.GetDetailsBody (report, scope);
            if (scope > Granularity.Detail || History == null)
                return;

            // Do not show a volatile CRC.
            if (fbs == null || ! fbs.CanWrite)
            {
                if (report.Count > 0)
                    report.Add (String.Empty);

                report.Add ($"Stored self-CRC-32 = {History.StoredCRC:X8}");
                report.Add ("Actual self-CRC-32 = " + (History.ActualCRC == null? "?" : $"{History.ActualCRC:X8}"));
            }

            report.Add ("Prover = " + (History.Prover ?? "(none)"));

            if (report.Count > 0)
                report.Add (String.Empty);

            report.Add ("History:");
            foreach (var lx in History.Comment)
                report.Add ("  " + lx);
        }
    }
}

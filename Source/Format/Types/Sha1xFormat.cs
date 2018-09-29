using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NongCrypto;

namespace NongFormat
{
    public class Sha1xFormat : HashesContainer
    {
        public static string[] Names
        { get { return new string[] { "sha1x" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.ToLower().EndsWith(".sha1x"))
                return new Model (stream, path);
            return null;
        }


        public new class Model : HashesContainer.Model
        {
            public new readonly Sha1xFormat Data;

            public Model (Stream stream, string path) : base (path, 20)
            {
                base._data = Data = new Sha1xFormat (this, stream, path);
                ParseHeaderAndHistory();
                ParseHashes();
            }

            public Model (Stream stream, string digPath, LogEacFormat log, IList<Mp3Format.Model> mp3s, string signature) : base (digPath, 16)
            {
                base._data = Data = new Sha1xFormat (this, stream, digPath);
                Data.fbs = stream;

                CreateHistory();
                HistoryModel.Add ("ripped", signature);

                HashedModel.AddActual (log.Name, log.FileSHA1);
                foreach (var mp3Model in mp3s)
                    HashedModel.AddActual (mp3Model.Data.Name, mp3Model.Data.MediaSHA1, HashStyle.Media);
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Data.Issues.HasFatal)
                    return;

                base.CalcHashes (hashFlags, validationFlags);

                if ((validationFlags & Validations.SHA1) != 0)
                    ComputeContentHashes (new Sha1Hasher(), Hashes.MediaSHA1);
            }
        }

        private Sha1xFormat (Model model, Stream stream, string path) : base (model, stream, path, Encoding.UTF8)
         => Validation = Validations.SHA1;
    }
}

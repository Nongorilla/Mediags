using System.IO;
using NongCrypto;

namespace NongFormat
{
    public class Sha1Format : HashesContainer
    {
        public static string[] Names
        { get { return new string[] { "sha1" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.ToLower().EndsWith(".sha1"))
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : HashesContainer.Model
        {
            public readonly Sha1Format Bind;

            public Model (Stream stream, byte[] header, string path) : base (path, 20)
            {
                BaseBind = BindHashed = Bind = new Sha1Format (stream, path, HashedModel.Bind);
                Bind.Issues = IssueModel.Data;

                ParseHashes();
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Bind.Issues.HasFatal)
                    return;

                if ((validationFlags & Validations.SHA1) != 0)
                    ComputeContentHashes (new Sha1Hasher());

                base.CalcHashes (hashFlags, validationFlags);
            }
        }


        private Sha1Format (Stream stream, string path, HashedFile.Vector hashedVector) : base (stream, path, hashedVector)
        {
            this.Validation = Validations.SHA1;
        }
    }
}

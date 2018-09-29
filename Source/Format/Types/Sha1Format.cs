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
                return new Model (stream, path);
            return null;
        }

        public new class Model : HashesContainer.Model
        {
            public new readonly Sha1Format Data;

            public Model (Stream stream, string path) : base (path, 20)
            {
                base._data = Data = new Sha1Format (this, stream, path);
                ParseHashes();
            }

            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Data.Issues.HasFatal)
                    return;

                if ((validationFlags & Validations.SHA1) != 0)
                    ComputeContentHashes (new Sha1Hasher());

                base.CalcHashes (hashFlags, validationFlags);
            }
        }

        private Sha1Format (Model model, Stream stream, string path) : base (model, stream, path)
         => this.Validation = Validations.SHA1;
    }
}

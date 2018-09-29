using System.IO;
using NongCrypto;

namespace NongFormat
{
    public class Sha256Format : HashesContainer
    {
        public static string[] Names
        { get { return new string[] { "sha256" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.ToLower().EndsWith(".sha256"))
                return new Model (stream, path);
            return null;
        }


        public new class Model : HashesContainer.Model
        {
            public new readonly Sha256Format Data;

            public Model (Stream stream, string path) : base (path, 32)
            {
                base._data = Data = new Sha256Format (this, stream, path);
                ParseHashes();
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Data.Issues.HasFatal)
                    return;

                if ((validationFlags & Validations.SHA256) != 0)
                    ComputeContentHashes (new Sha256Hasher());

                base.CalcHashes (hashFlags, validationFlags);
            }
        }

        private Sha256Format (Model model, Stream stream, string path) : base (model, stream, path)
         => this.Validation = Validations.SHA256;
    }
}

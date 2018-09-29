using System;
using System.IO;

namespace NongFormat
{
    public class DbFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "db" }; } }

        public static string Subname
        { get { return "Thumbs"; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.EndsWith (System.IO.Path.DirectorySeparatorChar + "thumbs.db", StringComparison.InvariantCultureIgnoreCase))
                return new Model (stream, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public new readonly DbFormat Data;

            public Model (Stream stream, string path)
            {
                base._data = Data = new DbFormat (this, stream, path);

                // No content diagnostics at this time.
                if (Data.fbs.Length == 0)
                    IssueModel.Add ("File is empty.");
            }
        }

        private DbFormat (Model model, Stream stream, string path) : base (model, stream, path)
        { }
    }


    // Class DbOtherFormat exists just to suppress errors on non-thumbs .db files.
    public class DbOtherFormat : FormatBase
    {
        public static string[] Names
        { get { return new string[] { "db" }; } }

        public static string Subname
        { get { return "*hidden*"; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.EndsWith (".db", StringComparison.InvariantCultureIgnoreCase))
                if (! path.EndsWith (System.IO.Path.DirectorySeparatorChar + "thumbs.db", StringComparison.InvariantCultureIgnoreCase))
                    return new DbOtherFormat.Model (stream, path);
            return null;
        }

        public class Model : FormatBase.ModelBase
        {
            public new readonly DbOtherFormat Data;

            public Model (Stream stream, string path)
             => base._data = Data = new DbOtherFormat (this, stream, path);
        }

        private DbOtherFormat (Model model, Stream stream, string path) : base (model, stream, path)
        { }
    }
}

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
                return new Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public readonly DbFormat Bind;

            public Model (Stream stream, byte[] hdr, string path)
            {
                /*BaseBind*/ base._data = Bind = new DbFormat (stream, path);
                Bind.Issues = IssueModel.Data;

                // No content diagnostics at this time.
                if (Bind.fbs.Length == 0)
                    IssueModel.Add ("File is empty.");
            }
        }


        private DbFormat (Stream stream, string path) : base (stream, path)
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
                    return new DbOtherFormat.Model (stream, hdr, path);
            return null;
        }


        public class Model : FormatBase.ModelBase
        {
            public new readonly DbOtherFormat Data;

            public Model (Stream stream, byte[] hdr, string path)
            {
                base._data = Data = new DbOtherFormat (stream, path);
                Data.Issues = IssueModel.Data;
            }
        }


        private DbOtherFormat (Stream stream, string path) : base (stream, path)
        { }
    }
}

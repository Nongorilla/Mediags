using System.IO;
using System.Text;

namespace NongFormat
{
    public class M3u8Format : FilesContainer
    {
        public static string[] Names
        { get { return new string[] { "m3u8" }; } }

        public override string[] ValidNames
        { get { return Names; } }


        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.ToLower().EndsWith(".m3u8"))
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : FilesContainer.Model
        {
            public readonly M3u8Format Bind;

            public Model (Stream stream, byte[] header, string path) : base (path)
            {
                BaseBind = BindFiles = Bind = new M3u8Format (stream, path, FilesModel.Bind);
                Bind.Issues = IssueModel.Bind;

                stream.Position = 0;
                TextReader tr = new StreamReader (stream, Encoding.UTF8);

                for (int line = 1; ; ++line)
                {
                    var lx = tr.ReadLine();
                    if (lx == null)
                        break;
                    lx = lx.TrimStart();
                    if (lx.Length > 0 && lx[0] != '#')
                        FilesModel.Add (lx);
                }
            }
        }


        private M3u8Format (Stream stream, string path, FileItem.Vector files) : base (stream, path, files)
        { }
    }
}

using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public class Mpeg1Format : RiffContainer
    {
        public override string[] ValidNames
        { get { return Mpeg2Format.Names; } }

        public new class Model : RiffContainer.Model
        {
            public new readonly Mpeg1Format Data;

            public Model (Stream stream, byte[] header, string path)
            {
                base._data = Data = new Mpeg1Format (this, stream, path);
                ParseRiff (header);
                GetDiagsForMarkable();
            }
        }

        public Mpeg1Format (Model model, Stream stream, string path) : base (model, stream, path)
        { }

        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            report.Add ("Format = MPEG-1 (CDXA)");
        }
    }
}

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
            public readonly Mpeg1Format Bind;

            public Model (Stream stream, byte[] header, string path)
            {
                BaseBind = RiffBind = Bind = new Mpeg1Format (stream, path);
                Bind.Issues = IssueModel.Bind;

                ParseRiff (header);
                GetDiagsForMarkable();
            }
        }


        public Mpeg1Format (Stream stream, string path) : base (stream, path)
        { }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            report.Add ("Format = MPEG-1 (CDXA)");
        }
    }
}

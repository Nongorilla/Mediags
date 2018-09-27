using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    // en.wikipedia.org/wiki/Cue_sheet_(computing)#Cue_sheet_syntax
    // digitalx.org/cue-sheet/syntax/index.html
    public class CueFormat : FilesContainer
    {
        public static string[] Names
        { get { return new string[] { "cue" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.ToLower().EndsWith(".cue"))
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : FilesContainer.Model
        {
            public readonly CueFormat Bind;

            public Model (Stream stream, byte[] header, string path) : base (path)
            {
                BaseBind = BindFiles = Bind = new CueFormat (stream, path, FilesModel.Bind);
                Bind.Issues = IssueModel.Data;

                SetIgnoredName ("Range.wav");
                Bind.fbs.Position = 0;
                TextReader tr = new StreamReader (Bind.fbs, LogBuffer.cp1252);

                for (int line = 1; ; ++line)
                {
                    var lx = tr.ReadLine();
                    if (lx == null)
                        break;

                    lx = lx.TrimStart();
                    if (lx.Length == 0)
                        continue;

                    if (lx.StartsWith ("CATALOG "))
                    {
                        Bind.Catalog = lx.Substring (8).Trim();
                        if (Bind.Catalog.Length != 13)
                            IssueModel.Add ("Invalid CATALOG.");
                        continue;
                    }

                    if (lx.Length > 0 && lx.StartsWith ("FILE "))
                    {
                        var name = Bind.GetQuotedField (lx, 5);
                        if (name.Length == 0)
                            IssueModel.Add ("Missing file name.");
                        else
                            FilesModel.Add (name);
                    }
                }
            }
        }


        private CueFormat (Stream stream, string path, FileItem.Vector files) : base (stream, path, files)
        { }


        public string Catalog { get; private set; }

        public string GetQuotedField (string text, int pos)
        {
            do
            {
                if (pos >= text.Length)
                    return String.Empty;
            }
            while (text[pos]==' ' || text[pos]=='\t');

            if (text[pos]=='"')
            {
                int pos2 = text.IndexOf ('"', pos+1);
                return text.Substring (pos+1, pos2-pos-1);
            }
            else
            {
                int pos2 = text.IndexOf (' ', pos+1);
                return text.Substring (pos, pos2-pos);
            }
        }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            base.GetDetailsBody (report, scope);

            if (! String.IsNullOrEmpty (Catalog))
            {
                if (scope <= Granularity.Detail || report.Count != 0)
                    report.Add (String.Empty);
                report.Add ("Catalog = " + Catalog);
            }
        }
    }
}

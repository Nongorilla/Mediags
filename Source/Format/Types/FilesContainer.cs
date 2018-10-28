using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public abstract class FilesContainer : FormatBase
    {
        public abstract new class Model : FormatBase.Model
        {
            public readonly FileItem.Vector.Model FilesModel;
            public new FilesContainer Data => (FilesContainer) _data;

            public Model (string rootPath)
             => FilesModel = new FileItem.Vector.Model (rootPath);

            public void SetAllowRooted (bool allow)
            { Data.ForbidRooted = ! allow; }

            public void SetIgnoredName (string name)
            { Data.IgnoredName = name; }

            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (base.Data.Issues.HasFatal)
                    return;

                if ((validationFlags & Validations.Exists) != 0)
                    if (Data.Files.Items.Count != 1 || Data.Files.Items[0].Name != Data.IgnoredName)
                    {
                        int notFoundTotal = 0;

                        for (int ix = 0; ix < Data.Files.Items.Count; ++ix)
                        {
                            FileItem item = Data.Files.Items[ix];
                            var name = item.Name;

                            if (Data.AllowNonFile && (name.StartsWith ("http:") || name.StartsWith ("https:")))
                                IssueModel.Add ("Ignoring URL '" + name + "'.", Severity.Trivia);
                            else
                            {
                                try
                                {
                                    if (! System.IO.Path.IsPathRooted (name))
                                        name = Data.Files.RootDir + System.IO.Path.DirectorySeparatorChar + name;
                                    else if (Data.ForbidRooted)
                                        IssueModel.Add ("File is rooted: '" + item.Name + "'.");
                                }
                                catch (ArgumentException ex)
                                {
                                    IssueModel.Add ($"Malformed file name '{name}': {ex.Message}");
                                    FilesModel.SetIsFound (ix, false);
                                    ++notFoundTotal;
                                    continue;
                                }

                                // Exists doesn't seem to throw any exceptions, so no try/catch.
                                bool isFound = File.Exists (name);
                                FilesModel.SetIsFound (ix, isFound);
                                if (! isFound)
                                {
                                    IssueModel.Add ("Missing file '" + item.Name + "'.");
                                    ++notFoundTotal;
                                }
                            }
                        }

                        var sfx = Data.Files.Items.Count == 1? String.Empty : "s";

                        var tx = "Existence check" + sfx + " of "  + Data.Files.Items.Count + " file" + sfx;
                        if (notFoundTotal == 0)
                            tx += " successful.";
                        else
                            tx += " failed with " + notFoundTotal + " not found.";

                        IssueModel.Add (tx, notFoundTotal == 0? Severity.Advisory : Severity.Error);
                    }

                base.CalcHashes (hashFlags, validationFlags);
            }
        }


        public FileItem.Vector Files { get; private set; }
        public bool AllowNonFile { get; private set; }
        public bool ForbidRooted { get; private set; }
        public string IgnoredName { get; private set; }

        protected FilesContainer (Model model, Stream stream, string path) : base (model, stream, path)
        {
            this.Files = model.FilesModel.Data;
            this.AllowNonFile = true;
        }

        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (scope > Granularity.Detail || Files.Items.Count == 0)
                return;

            if (report.Count != 0)
                report.Add (String.Empty);

            report.Add ("Files:");
            foreach (var item in Files.Items)
                report.Add ((item.IsFound == true? "+ " : (item.IsFound == false? "* " : "  ")) + item.Name);
        }
    }
}

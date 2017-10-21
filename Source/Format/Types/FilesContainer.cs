using System;
using System.Collections.Generic;
using System.IO;
using NongIssue;

namespace NongFormat
{
    public abstract class FilesContainer : FormatBase
    {
        public abstract class Model : FormatBase.ModelBase
        {
            public readonly FileItem.Vector.Model FilesModel;
            public FilesContainer BindFiles { get; protected set; }

            public Model (string rootPath)
            { FilesModel = new FileItem.Vector.Model (rootPath); }

            public void SetAllowRooted (bool allow)
            { BindFiles.ForbidRooted = ! allow; }

            public void SetIgnoredName (string name)
            { BindFiles.IgnoredName = name; }

            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (BaseBind.Issues.HasFatal)
                    return;

                if ((validationFlags & Validations.Exists) != 0)
                    if (BindFiles.Files.Items.Count != 1 || BindFiles.Files.Items[0].Name != BindFiles.IgnoredName)
                    {
                        int notFoundTotal = 0;

                        for (int ix = 0; ix < BindFiles.Files.Items.Count; ++ix)
                        {
                            FileItem item = BindFiles.Files.Items[ix];
                            var name = item.Name;

                            if (BindFiles.AllowNonFile && (name.StartsWith ("http:") || name.StartsWith ("https:")))
                                IssueModel.Add ("Ignoring URL '" + name + "'.", Severity.Trivia);
                            else
                            {
                                if (! System.IO.Path.IsPathRooted (item.Name))
                                    name = BindFiles.Files.RootDir + System.IO.Path.DirectorySeparatorChar + name;
                                else if (BindFiles.ForbidRooted)
                                    IssueModel.Add ("File is rooted: '" + item.Name + "'.");

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

                        var sfx = BindFiles.Files.Items.Count == 1? String.Empty : "s";

                        var tx = "Existence check" + sfx + " of "  + BindFiles.Files.Items.Count + " file" + sfx;
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

        public FilesContainer (Stream stream, string path, FileItem.Vector files) : base (stream, path)
        {
            this.Files = files;
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

using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;

namespace NongFormat
{
    public class FileItem : INotifyPropertyChanged
    {
        public class Vector
        {
            public class Model
            {
                public readonly Vector Bind;

                public Model (string rootDir)
                { Bind = new Vector (rootDir); }

                public void Add (string fileName)
                { Bind.items.Add (new FileItem (fileName)); }

                public void SetName (int index, string fileName)
                { Bind.items[index].Name = fileName; }

                public void SetIsFound (int index, bool newValue)
                {
                    bool? oldValue = Bind.items[index].IsFound;
                    if (newValue != oldValue)
                    {
                        if (oldValue == true) --Bind.FoundCount;
                        if (newValue == true) ++Bind.FoundCount;
                        Bind.items[index].IsFound = newValue;
                    }
                }
            }


            public string RootDir { get; private set; }
            public int FoundCount { get; private set; }

            private readonly ObservableCollection<FileItem> items;
            public ReadOnlyObservableCollection<FileItem> Items { get; private set; }

            public Vector (string rootDir)
            {
                var baseDir = Path.GetDirectoryName (rootDir);
                if (baseDir.Length > 0 && baseDir[baseDir.Length-1] == Path.DirectorySeparatorChar)
                    baseDir += Path.DirectorySeparatorChar;

                this.items = new ObservableCollection<FileItem>();
                this.Items = new ReadOnlyObservableCollection<FileItem>(items);
                this.RootDir = baseDir;
            }
        }


        private string name;
        public string Name
        {
            get { return name; }
            private set
            {
                name = value;
                NotifyPropertyChanged (null);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged (string propName)
        { if (PropertyChanged != null) PropertyChanged (this, new PropertyChangedEventArgs (propName)); }

        public FileItem (string name)
        {
            this.Name = name;
            this.IsFound = null;
        }

        public bool? IsFound
        { get; private set; }

        public override string ToString()
        { return Name; }
    }
}

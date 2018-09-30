using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace NongFormat
{
    public delegate FormatBase.ModelBase FormatModelFactory (Stream stream, byte[] header, string path);

    public class FileFormat
    {
        public class Vector
        {
            public class Model
            {
                public readonly FileFormat.Vector Data;

                public Model()
                 => Data = new FileFormat.Vector();


                public void Add (FormatModelFactory factory, string[] names, string subname = null)
                {
                    Data.items.Add (new FileFormat (factory, names, subname));
                }


                public void Sort()
                {
                    Comparison<FileFormat> comp = (f1, f2) => String.CompareOrdinal (f1.LongName, f2.LongName);
                    Data.items.Sort (comp);
                }


                public void ResetTotals()
                {
                    foreach (var format in Data.items)
                    {
                        format.TrueTotal = 0;
                        format.TotalConverted = 0;
                        format.TotalCreated = 0;
                        format.TotalDataErrors = 0;
                        format.TotalHeaderErrors = 0;
                        format.TotalMisnamed = 0;
                        format.TotalMissing = 0;
                        format.TotalSigned = 0;
                    }
                }
            }


            private readonly List<FileFormat> items;
            public ReadOnlyCollection<FileFormat> Items { get; private set; }

            public Vector()
            {
                this.items = new List<FileFormat>();
                this.Items = new ReadOnlyCollection<FileFormat> (this.items);
            }
        }


        public FormatModelFactory ModelFactory { get; private set; }

        private readonly string[] names;
        public ReadOnlyCollection<string> Names { get; private set; }

        public string Subname { get; private set; }

        public int TrueTotal { get; set; }
        public int TotalHeaderErrors { get; set; }
        public int TotalDataErrors { get; set; }
        public int TotalMisnamed { get; set; }
        public int TotalMissing { get; set; }
        public int TotalCreated { get; set; }
        public int TotalConverted { get; set; }
        public int TotalSigned { get; set; }

        public string PrimaryName
        { get { return names[0]; } }


        public FileFormat (FormatModelFactory factory, string[] names, string subname)
        {
            if (names == null)
                this.names = new string[] { };
            else
            {
                this.names = new string[names.Length];
                Array.Copy (names, this.names, names.Length);
            }

            this.Names = new ReadOnlyCollection<string> (this.names);
            this.Subname = subname;
            this.ModelFactory = factory;
        }


        public string LongName
        {
            get
            {
                string result = null;

                foreach (var name in Names)
                    result = result==null? name : result + '/' + name;
                if (Subname != null)
                    result += " (" + Subname + ")";

                return result;
            }
        }
    }
}

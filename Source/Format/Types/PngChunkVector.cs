using System;
using System.Collections.ObjectModel;

namespace NongFormat
{
    public class PngChunk
    {
        public class Vector
        {
            public class Model
            {
                public readonly Vector Data;

                public Model()
                 => Data = new Vector();

                public void Add (string type, UInt32 size, UInt32 storedCRC, UInt32? actualCRC = null)
                 => Data.items.Add (new PngChunk (type, size, storedCRC, actualCRC));

                public void SetActualCRC (int index, UInt32 crc)
                 => Data.items[index].ActualCRC = crc;
            }

            private readonly ObservableCollection<PngChunk> items;
            public ReadOnlyObservableCollection<PngChunk> Items { get; private set; }

            public Vector()
            {
                this.items = new ObservableCollection<PngChunk>();
                this.Items = new ReadOnlyObservableCollection<PngChunk> (this.items);
            }
        }


        private PngChunk (string type, UInt32 size, UInt32 storedCRC, UInt32? actualCRC)
        {
            this.Type = type;
            this.Size = size;
            this.StoredCRC = storedCRC;
            this.ActualCRC = actualCRC;
        }

        public string Type { get; private set; }
        public UInt32 Size { get; private set; }
        public UInt32 StoredCRC { get; private set; }
        public UInt32? ActualCRC { get; private set; }
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace NongFormat
{
    public class HashesHistory : INotifyPropertyChanged
    {
        public class Model
        {
            public readonly HashesHistory Bind;

            public Model()
            { Bind = new HashesHistory(); }

            public void SetStoredSelfCRC (UInt32 crc)
            { Bind.StoredCRC = crc; }

            public void SetActualSelfCRC (UInt32 crc)
            { Bind.ActualCRC = crc; }

            public void SetIsDirty (bool newValue)
            { Bind.IsDirty = newValue; }

            public void SetProver (string signature)
            { Bind.Prover = signature; }

            public void SetLastAction (string signature, string action)
            { Bind.LastSig = signature; Bind.LastAction = action; }

            public void AddLine (string line)
            {
                Bind.comment.Add (line);
                Bind.NotifyPropertyChanged ("Comment");
            }

            public void Add (string action, string signature)
            {
                var now = DateTime.Now;
                var lx = String.Format ("{0:0000}{1:00}{2:00} {3}: {4}: {5}", now.Year, now.Month, now.Day, now.TimeOfDay.ToString().Substring (0, 8), signature, action);
                Bind.LastAction = action;
                Bind.LastSig = signature;
                Bind.comment.Add (lx);
                Bind.IsDirty = true;

                if (action == "proved")
                    Bind.Prover = signature;
            }

            public void Replace (string action, string signature)
            {
                Bind.comment.RemoveAt (Bind.comment.Count-1);
                Add (action, signature);
            }
        }


        private readonly ObservableCollection<string> comment;
        public ReadOnlyObservableCollection<string> Comment { get; private set; }

        public bool IsDirty { get; private set; }
        public string LastSig { get; private set; }
        public string Prover { get; private set; }
        public string LastAction { get; private set; }
        public UInt32 StoredCRC { get; private set; }
        public UInt32? ActualCRC { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged (string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged (this, new PropertyChangedEventArgs (propName));
        }

        private HashesHistory()
        {
            comment = new ObservableCollection<string>();
            Comment = new ReadOnlyObservableCollection<string> (comment);
        }
    }
}

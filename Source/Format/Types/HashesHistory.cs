using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace NongFormat
{
    public class HashesHistory : INotifyPropertyChanged
    {
        public class Model
        {
            public readonly HashesHistory Data;

            public Model()
             => Data = new HashesHistory();

            public void SetStoredSelfCRC (UInt32 crc)
             => Data.StoredCRC = crc;

            public void SetActualSelfCRC (UInt32 crc)
             => Data.ActualCRC = crc;

            public void SetIsDirty (bool newValue)
             => Data.IsDirty = newValue;

            public void SetProver (string signature)
             => Data.Prover = signature;

            public void SetLastAction (string signature, string action)
            { Data.LastSig = signature; Data.LastAction = action; }

            public void AddLine (string line)
            {
                Data.comment.Add (line);
                Data.NotifyPropertyChanged ("Comment");
            }

            public void Add (string action, string signature)
            {
                var now = DateTime.Now;
                var lx = $"{now.Year:0000}{now.Month:00}{now.Day:00} {now.TimeOfDay.ToString().Substring(0,8)}: {signature}: {action}";
                Data.LastAction = action;
                Data.LastSig = signature;
                Data.comment.Add (lx);
                Data.IsDirty = true;

                if (action == "proved")
                    Data.Prover = signature;
            }

            public void Replace (string action, string signature)
            {
                Data.comment.RemoveAt (Data.comment.Count-1);
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

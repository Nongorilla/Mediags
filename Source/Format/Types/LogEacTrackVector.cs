using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NongIssue;

namespace NongFormat
{
    public class LogEacTrack
    {
        public class Vector
        {
            public class Model
            {
                public readonly Vector Bind;

                public Model()
                { Bind = new Vector(); }

                public FlacFormat.Model GetMatch (int index) { return Bind.items[index].MatchModel; }
                public void SetMatch (int index, FlacFormat.Model flacModel) { Bind.items[index].MatchModel = flacModel; }
                public void SetSeverest (int index, Issue issue) { Bind.items[index].RipSeverest = issue; }

                public void Add (int number, string fileName, string pregap, string peak, string speed,
                                 string quality, uint? testCRC, uint? copyCRC, bool hasOK, int? arVersion, int? arConfidence)
                { Bind.items.Add (new LogEacTrack (number, fileName, pregap, peak, speed, quality, testCRC, copyCRC, hasOK, arVersion, arConfidence)); }

                public void SetCtConfidence (int number, int confidence)
                { Bind.items[number].CtConfidence = confidence; }
            }


            private readonly List<LogEacTrack> items;
            public ReadOnlyCollection<LogEacTrack> Items { get; private set; }

            public Vector()
            {
                this.items = new List<LogEacTrack>();
                this.Items = new ReadOnlyCollection<LogEacTrack> (this.items);
            }


#region log-EAC log entry methods
            // This routine allows track 1 to be missing (e.g. Quake soundtrack)
            public bool IsNearlyAllPresent()
            {
                if (items.Count == 0)
                    return false;

                int tn0 = items[0].Number;
                if (tn0 > 2)
                    return false;

                for (int ti = 1; ti < items.Count; ++ti)
                    if (ti != items[ti].Number - tn0)
                        return false;

                return true;
            }

            public bool AllHasOK()
            {
                foreach (var item in items)
                    if (! item.HasOK)
                        return false;
                return true;
            }

            public bool AllHasOkWithQuality()
            {
                foreach (var item in items)
                    if (! item.HasOK || String.IsNullOrWhiteSpace (item.Qual))
                        return false;
                return true;
            }
#endregion

#region log-EAC matching rip track methods

            public int WidestTrackWidth
            { get { return Items[items.Count-1].Match.GetTag ("TRACKNUMBER").Length; } }


            // On exit, returns null if none present
            //     else returns true if all present and same
            //     else returns false
            public bool? IsFlacTagsAllSame (string flacTag)
            {
                if (items.Count == 0)
                    return null;

                var val0 = items[0].Match.GetTag (flacTag);
                if (! String.IsNullOrEmpty (val0))
                    return items.All (x => x.Match == null || x.Match.GetTag (flacTag) == val0);

                var isAllEmpty = items.All (x => x.Match == null || String.IsNullOrEmpty (x.Match.GetTag (flacTag)));
                if (isAllEmpty)
                    return null;

                return false;
            }


            public bool? IsFlacTagsAllSameMulti (string flacTag)
            {
                if (items.Count == 0)
                    return null;

                var val0 = items[0].Match.GetMultiTag (flacTag);
                if (! String.IsNullOrEmpty (val0))
                    return items.All (x => x.Match == null || x.Match.GetMultiTag (flacTag) == val0);

                var isAllEmpty = items.All (x => x.Match == null || String.IsNullOrEmpty (x.Match.GetTag (flacTag)));
                if (isAllEmpty)
                    return null;

                return false;
            }


            public bool AnyHas (string tagId)
            {
                foreach (var it in items)
                    if (it.Match != null && ! String.IsNullOrEmpty (it.Match.GetTag (tagId)))
                        return true;
                return false;
            }
#endregion
        }


        private LogEacTrack (int number, string path, string pregap, string peak, string speed,
                             string quality, uint? testCRC, uint? copyCRC, bool isOK, int? arVersion, int? confidence)
        {
            this.Number = number;
            this.FilePath = path;
            this.Pregap = pregap;
            this.Peak = peak;
            this.Speed = speed;
            this.Qual = quality;
            this.TestCRC = testCRC;
            this.CopyCRC = copyCRC;
            this.HasOK = isOK;
            this.AR = arVersion;
            this.ArConfidence = confidence;
        }

        public bool? IsRipOK
        {
            get
            {
                if (! HasOK || ! HasQuality || IsBadCRC || CtConfidence < 0) return false;
                if (RipSeverest != null && RipSeverest.Level >= Severity.Error) return false;
                if (MatchModel == null) return null;
                return ! MatchModel.Bind.Issues.HasError;
            }
        }

        public int Number { get; private set; }
        public string FilePath { get; private set; }
        public string Pregap { get; private set; }
        public string Peak { get; private set; }
        public string Speed { get; private set; }
        public string Qual { get; private set; }
        public UInt32? TestCRC { get; private set; }
        public UInt32? CopyCRC { get; private set; }
        public bool HasOK { get; private set; }
        public int? AR { get; private set; }
        public int? ArConfidence { get; private set; }
        public int? CtConfidence { get; private set; }

        public bool HasQuality { get { return ! String.IsNullOrWhiteSpace (Qual); } }
        public bool IsBadCRC { get { return CopyCRC != null && TestCRC != null && TestCRC != CopyCRC; } }

        private FlacFormat.Model MatchModel = null;
        public FlacFormat Match { get { return MatchModel?.Bind; } }
        public Issue RipSeverest { get; private set; }
    }
}

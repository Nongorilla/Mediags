using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace NongIssue
{
    public enum Granularity
    { Detail, Long, Verbose, Advisory, Terse, Quiet, Summary };

    public enum Severity
    {
        NoIssue, Noise=Granularity.Long,
        Trivia=Granularity.Verbose, Advisory=Granularity.Advisory,
        Warning=Granularity.Terse, Error=Granularity.Quiet,
        Fatal
    };

    [Flags]
    public enum IssueTags
    {
        None=0, HasId3v1=1, HasId3v24=2, HasApe=4, Substandard=8, Overstandard=0x10,
        ProveErr=0x100, ProveWarn=0x200, Fussy=0x400,
        Success=0x01000000, Failure=0x02000000, BadTag=0x04000000, MissingHash=0x08000000,
        MetaChange=0x10000000, NameChange=0x20000000, AlbumChange=0x40000000
    }

    public class Issue : INotifyPropertyChanged
    {
        public static string GetPrefix (Severity severity)
        {
            if (severity == Severity.NoIssue)
                return String.Empty;
            else if (severity <= Severity.Advisory)
                return "  ";
            else
                return severity <= Severity.Warning ? "- " : "* ";
        }

        public class Vector : INotifyPropertyChanged
        {
            private Vector (IssueTags warnEscalator=IssueTags.None, IssueTags errEscalator=IssueTags.None)
            {
                this.items = new ObservableCollection<Issue>();
                this.Items = new ReadOnlyObservableCollection<Issue> (this.items);
                this.MaxSeverity = Severity.NoIssue;
                this.WarnEscalator = warnEscalator;
                this.ErrEscalator = errEscalator;
            }

            public class Model
            {
                public readonly Vector Data;

                public Model (IssueTags warnEscalator=IssueTags.None, IssueTags errEscalator=IssueTags.None)
                 => Data = new Issue.Vector (warnEscalator, errEscalator);

                public Issue Add (string message, Severity severity=Severity.Error, IssueTags tag=IssueTags.None,
                                  string prompt=null, Func<string> repairer=null)
                {
                    System.Diagnostics.Debug.Assert ((prompt==null) == (repairer==null));

                    if (repairer != null)
                    {
                        // Force Warning as minimum for repairables.
                        if (severity < Severity.Warning)
                            severity = Severity.Warning;
                        ++Data.RepairableCount;
                    }

                    var issue = new Issue (Data, message, severity, tag, prompt, repairer);
                    Data.items.Add (issue);

                    Severity level = issue.Level;
                    if (Data.MaxSeverity < level)
                    {
                        Data.MaxSeverity = level;
                        Data.Severest = issue;
                    }

                    Data.NotifyPropertyChanged (nameof (FixedMessage));
                    return issue;
                }

                public void Escalate (IssueTags warnEscalator, IssueTags errEscalator)
                {
                    // Accumulate escalations.
                    Data.WarnEscalator |= warnEscalator;
                    Data.ErrEscalator |= errEscalator;

                    foreach (var issue in Data.items)
                    {
                        Severity level = issue.Level;
                        if (Data.MaxSeverity < level)
                        {
                            Data.MaxSeverity = level;
                            Data.Severest = issue;
                        }
                    }
                }

                public void Clear()
                {
                    Data.items = new ObservableCollection<Issue>();
                    Data.Items = new ReadOnlyObservableCollection<Issue> (Data.items);
                }

                public bool RepairerEquals (int index, Func<string> other)
                 => Data.items[index].Repairer == other;

                public string Repair (int index)
                {
                    var issue = Data.items[index];
                    if (! issue.IsRepairable)
                        return null;

                    issue.RepairError = issue.Repairer();
                    issue.IsRepairSuccessful = issue.RepairError == null;
                    if (issue.IsRepairSuccessful.Value == true)
                        --Data.RepairableCount;

                    Data.NotifyPropertyChanged (nameof (FixedMessage));
                    return issue.RepairError;
                }
            }

            private ObservableCollection<Issue> items;
            public ReadOnlyObservableCollection<Issue> Items { get; private set; }

            public event PropertyChangedEventHandler PropertyChanged;
            public void NotifyPropertyChanged (string propName)
            { if (PropertyChanged != null) PropertyChanged (this, new PropertyChangedEventArgs (propName)); }

            public IssueTags WarnEscalator { get; private set; }
            public IssueTags ErrEscalator { get; private set; }
            public Severity MaxSeverity { get; private set; }
            public Issue Severest { get; private set; }

            private int repairableCount;
            public int RepairableCount
            {
                get { return repairableCount; }
                set { repairableCount = value; NotifyPropertyChanged (nameof (RepairableCount)); }
            }

            public bool HasError => MaxSeverity >= Severity.Error;
            public bool HasFatal => MaxSeverity >= Severity.Fatal;
        }

        private readonly Vector owner;

        public string Message { get; private set; }
        public Severity BaseLevel { get; private set; }
        public IssueTags Tag { get; private set; }
        public string RepairPrompt { get; private set; }
        public string RepairError { get; private set; }
        private Func<string> Repairer { get; set; }
        public bool? IsRepairSuccessful { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged (string propName)
        { if (PropertyChanged != null) PropertyChanged (this, new PropertyChangedEventArgs (propName)); }

        public Issue (Vector owner, string message, Severity level=Severity.Advisory, IssueTags tag=IssueTags.None,
                      string prompt=null, Func<string> repairer=null)
        {
            this.owner = owner;
            this.Message = message;
            this.BaseLevel = level;
            this.Tag = tag;
            this.RepairPrompt = prompt;
            this.Repairer = repairer;
        }

        public Severity Level
        {
            get
            {
                Severity result = BaseLevel;
                if (result < Severity.Error)
                    if ((Tag & owner.ErrEscalator) != 0)
                        result = Severity.Error;
                    else if ((Tag & owner.WarnEscalator) != 0)
                        result = Severity.Warning;
                return result;
            }
        }

        public string FixedMessage
        {
            get
            {
                string result = Message;
                if (Level >= Severity.Warning)
                    result = $"{Level}: {result}";
                if (IsRepairSuccessful == true)
                    result += " (repair successful)";
                else if (RepairError != null)
                    result += $" (repair failed: {RepairError})";
                return result;
            }
        }

        public bool Failure => (Tag & IssueTags.Failure) != 0;
        public bool Success => (Tag & IssueTags.Success) != 0;
        public bool IsRepairable => Repairer != null && IsRepairSuccessful != true;
        public bool IsReportable (Granularity granularity) => (int) Level >= (int) granularity;
        public override string ToString() => FixedMessage;
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NongIssue;
using NongFormat;

namespace NongMediaDiags
{
    public enum Interaction { None, PromptToRepair };

    public delegate void MessageSendHandler (string message, Severity severity, Likeliness repairability);
    public delegate void ReportCloseHandler ();
    public delegate void FileVisitEventHandler (string dirName, string fileName);


    public partial class Diags
    {
        public FileFormat.Vector FileFormats { get; protected set; }

        public Func<string, bool?> QuestionAsk;
        public Func<string, string, char> InputChar;
        public Func<string, string, string, string> InputLine;
        public event MessageSendHandler MessageSend;
        public event ReportCloseHandler ReportClose;
        public event FileVisitEventHandler FileVisit;

        public string Product { get; set; }
        public string ProductVersion { get; set; }

        public string Root { get; protected set; }
        public string Filter { get; private set; }
        public string Exclusion { get; private set; }
        public Interaction Response { get; private set; }
        public Granularity Scope { get; set; }
        public Hashes HashFlags { get; set; }
        public Validations ValidationFlags { get; set; }
        public IssueTags WarnEscalator { get; set; }
        public IssueTags ErrEscalator { get; set; }

        public string CurrentFile { get; private set; }
        public string CurrentDirectory { get; private set; }

        public int TotalFiles { get; set; }
        public int TotalRepairable { get; set; }
        public int TotalWarnings { get; set; }
        public int TotalErrors { get; set; }
        public int TotalRepairs { get; set; }
        public int TotalSignable { get; set; }
        public int ExpectedFiles { get; set; }


        private Diags (FileFormat.Vector formats) : this()
        {
            this.FileFormats = formats;
        }


        protected Diags()
        {
            this.QuestionAsk = QuestionAskDefault;
        }


        public bool IsParanoid
        {
            get { return (HashFlags & Hashes.PcmMD5) != 0; }
            set { HashFlags = value ? HashFlags | Hashes.PcmMD5 : HashFlags & ~ Hashes.PcmMD5; }
        }


        public bool IsWebCheckEnabled
        {
            get { return (HashFlags & Hashes.WebCheck) != 0; }
            set { HashFlags = value ? HashFlags | Hashes.WebCheck : HashFlags & ~ Hashes.WebCheck; }
        }


        public bool IsBestTags
        {
            get { return (ErrEscalator & IssueTags.BadTag) != 0; }
            set { ErrEscalator = value ? ErrEscalator | IssueTags.BadTag : ErrEscalator & ~ IssueTags.BadTag; }
        }


        public bool IsFussy
        {
            get { return (ErrEscalator & IssueTags.Fussy) != 0; }
            set { ErrEscalator = value ? ErrEscalator | IssueTags.Fussy : ErrEscalator & ~ IssueTags.Fussy; }
        }


        public string FormatListText
        {
            get
            {
                var sb = new StringBuilder();

                foreach (var item in FileFormats.Items)
                    if ((item.Subname == null || item.Subname[0] != '*'))
                    {
                        if (sb.Length != 0)
                            sb.Append (", ");
                        sb.Append (item.LongName);
                    }

                return sb.ToString();
            }
        }


        public IList<string> GetRollups (IList<string> rep, string verb)
        {
            var sb = new StringBuilder();

            // Get displayed length for right alignment.
            string fmt = "{0," + TotalFiles.ToString().Length + "}";

            if (TotalFiles != 1)
                rep.Add (String.Format (fmt + " total files " + verb, TotalFiles));

            foreach (var item in FileFormats.Items)
            {
                string par = "";
                if (item.TotalHeaderErrors != 0)
                {
                    par = " (" + item.TotalHeaderErrors + " header CRC error";
                    if (item.TotalHeaderErrors > 1)
                        par += 's';
                }
                if (item.TotalDataErrors != 0)
                {
                    par += String.IsNullOrEmpty (par)? " (" : ", ";
                    par += item.TotalDataErrors + " data CRC error";
                    if (item.TotalDataErrors > 1)
                        par += 's';
                }
                if (item.TotalMisnamed != 0)
                    par += (String.IsNullOrEmpty (par)? " (" : ", ") + item.TotalMisnamed + " misnamed";
                if (item.TotalMissing != 0)
                    par += (String.IsNullOrEmpty (par)? " (" : ", ") + item.TotalMissing + " missing";
                if (item.TotalCreated != 0)
                    par += (String.IsNullOrEmpty (par)? " (" : ", ") + item.TotalCreated + " created";
                if (item.TotalConverted != 0)
                    par += (String.IsNullOrEmpty (par)? " (" : ", ") + item.TotalConverted + " converted";
                if (item.TotalSigned != 0)
                    par += (String.IsNullOrEmpty (par)? " (" : ", ") + item.TotalSigned + " signed";

                if (! String.IsNullOrEmpty (par))
                    par += ")";

                if (item.TrueTotal == 0 && String.IsNullOrEmpty (par))
                    continue;

                sb.Clear();
                sb.AppendFormat (fmt + " " + item.Names[0], item.TrueTotal);
                if (item.Subname != null)
                    sb.Append (" (" + item.Subname + ")");
                sb.Append (" file");
                if (item.TrueTotal != 1)
                    sb.Append ('s');

                sb.Append (par);
                rep.Add (sb.ToString());
            }

            if (TotalRepairable > 0)
            {
                sb.Clear();
                sb.AppendFormat (fmt + " file", TotalRepairable);
                if (TotalRepairable != 1)
                    sb.Append ('s');
                sb.Append (" with repairable issues");
                rep.Add (sb.ToString());
            }

            if (TotalRepairs > 0)
            {
                sb.Clear();
                sb.AppendFormat (fmt + " repair", TotalRepairs);
                if (TotalRepairs != 1)
                    sb.Append ('s');
                sb.Append (" made");
                rep.Add (sb.ToString());
            }

            if (TotalFiles > 0)
            {
                sb.Clear();
                sb.AppendFormat (fmt + " file", TotalWarnings);
                if (TotalWarnings != 1)
                    sb.Append ('s');
                sb.Append (" with warnings");
                if (TotalErrors == 0 && TotalWarnings == 0)
                {
                    sb.Append (" or errors");
                    rep.Add (sb.ToString());
                }
                else
                {
                    rep.Add (sb.ToString());
                    sb.Clear();
                    sb.AppendFormat (fmt + " file", TotalErrors);
                    if (TotalErrors != 1)
                        sb.Append ('s');
                    sb.Append (" with errors");
                    rep.Add (sb.ToString());
                }
            }

            return rep;
        }


        // Model should replace this default.
        public bool? QuestionAskDefault (string prompt)
        {
            return null;
        }


        public void OnMessageSend (string message, Severity severity=Severity.NoIssue, Likeliness repairability=Likeliness.None)
        {
            if (MessageSend != null)
                MessageSend (message, severity, repairability);
        }


        public void OnReportClose()
        {
            if (ReportClose != null)
                ReportClose();
        }


        public void OnFileVisit (string directoryName, string fileName)
        {
            if (FileVisit != null)
                FileVisit (directoryName, fileName);
        }


        public static string FormatDomainVersionText
        {
            get
            {
#if NETFX_CORE
                Assembly assembly = typeof (FormatBase).GetTypeInfo().Assembly;
                string result = assembly.GetName().Version.ToString();
#else
                var assembly = Assembly.GetExecutingAssembly();
                string result = assembly.GetName().Version.ToString();
#endif
                if (result.Length > 3 && result.EndsWith (".0"))
                    result = result.Substring (0, result.Length-2);
                return result;
            }
        }
    }
}

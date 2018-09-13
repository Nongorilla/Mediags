using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.IO;
using NongIssue;
using NongCrypto;

namespace NongFormat
{
    public enum Likeliness
    { None, Possible, Probable }

    [Flags]
    public enum Hashes
    { None=0, Intrinsic=1, FileMD5=2, FileSHA1=4, FileSHA256=8, MetaSHA1=0x10, MediaSHA1=0x20, PcmCRC32=0x40, PcmMD5=0x80, WebCheck=0x100 }

    [Flags]
    public enum Validations
    { None=0, Exists=1, MD5=2, SHA1=4, SHA256=8 };

    public enum NamingStrategy
    { Manual, ArtistTitle, ShortTitle, UnloadedAlbum }

    [DebuggerDisplay (@"\{{Name}}")]
    public abstract class FormatBase : INotifyPropertyChanged
    {
        public class ModelBase
        {
            private FormatBase bind;
            public FormatBase BaseBind { get { return bind; } protected set { bind = value; } }
            public Issue.Vector.Model IssueModel { get; private set; }

            public ModelBase()
            {
                IssueModel = new Issue.Vector.Model();
            }


            public virtual void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (IssueModel.Bind.HasFatal)
                    return;

                bool hitCache = bind.fBuf != null && bind.FileSize < Int32.MaxValue;

                if ((hashFlags & Hashes.FileMD5) != 0 && bind.fileMD5 == null)
                {
                    var hasher = new Md5Hasher();
                    if (hitCache)
                        hasher.Append (bind.fBuf, 0, bind.fBuf.Length);
                    else
                        hasher.Append (bind.fbs);
                    bind.fileMD5 = hasher.GetHashAndReset();
                }

                if ((hashFlags & Hashes.FileSHA1) != 0 && bind.fileSHA1 == null)
                {
                    var hasher = new Sha1Hasher();
                    if (hitCache)
                        hasher.Append (bind.fBuf, 0, bind.fBuf.Length);
                    else
                        hasher.Append (bind.fbs);
                    bind.fileSHA1 = hasher.GetHashAndReset();
                }

                if ((hashFlags & Hashes.FileSHA256) != 0 && bind.fileSHA256 == null)
                {
                    var hasher = new Sha256Hasher();
                    if (hitCache)
                        hasher.Append (bind.fBuf, 0, bind.fBuf.Length);
                    else
                        hasher.Append (bind.fbs);
                    bind.fileSHA256 = hasher.GetHashAndReset();
                }

                if ((hashFlags & Hashes.MediaSHA1) != 0 && bind.mediaSHA1 == null)
                    if (bind.MediaCount == bind.FileSize && bind.fileSHA1 != null)
                    {
                        System.Diagnostics.Debug.Assert (bind.mediaPosition == 0);
                        bind.mediaSHA1 = bind.fileSHA1;
                    }
                    else
                    {
                        var hasher = new Sha1Hasher();
                        if (hitCache)
                            hasher.Append (bind.fBuf, (int) bind.mediaPosition, (int) bind.MediaCount);
                        else
                            hasher.Append (bind.fbs, bind.mediaPosition, bind.MediaCount);
                        bind.mediaSHA1 = hasher.GetHashAndReset();
                    }

                if ((hashFlags & Hashes.MetaSHA1) != 0 && bind.metaSHA1 == null)
                {
                    if (bind.MediaCount == 0 && bind.fileSHA1 != null)
                        bind.metaSHA1 = bind.fileSHA1;
                    else
                    {
                        var hasher = new Sha1Hasher();
                        var suffixPos = bind.mediaPosition + bind.MediaCount;
                        if (bind.mediaPosition > 0 || suffixPos < bind.FileSize)
                            if (hitCache)
                                hasher.Append (bind.fBuf, 0, (int) bind.mediaPosition, (int) suffixPos, (int) (bind.FileSize - suffixPos));
                            else
                                hasher.Append (bind.fbs, 0, bind.mediaPosition, suffixPos, bind.FileSize - suffixPos);
                        bind.metaSHA1 = hasher.GetHashAndReset();
                    }
                }
            }


            protected void CalcMark (bool assumeProbable=false)
            {
                byte[] buf = null;

                long markSize = bind.FileSize - bind.ValidSize;
                if (markSize <= 0)
                    return;

                // 1000 is somewhat arbitrary here.
                if (markSize > 1000)
                    bind.Watermark = Likeliness.Possible;
                else
                {
                    bind.fbs.Position = bind.ValidSize;
                    buf = new byte[(int) markSize];
                    int got = bind.fbs.Read (buf, 0, (int) markSize);
                    if (got != markSize)
                    {
                        IssueModel.Add ("Read failure", Severity.Fatal);
                        return;
                    }

                    bind.excess = null;
                    bind.Watermark = Likeliness.Probable;
                    if (! assumeProbable)
                        for (int ix = 0; ix < buf.Length; ++ix)
                        {
                            // Fuzzy ID of watermark bytes by checking if ASCII text. Purpose is to
                            // exclude .AVI files that do not have correct encoded sizes - a common issue.
                            var bb = buf[ix];
                            if (bb > 127 || (bb < 32 && bb != 0 && bb != 9 && bb != 0x0A && bb != 0x0D))
                            {
                                bind.Watermark = Likeliness.Possible;
                                break;
                            }
                        }
                }

                if (bind.Watermark == Likeliness.Probable)
                {
                    bind.excess = buf;
                    var caption = bind.Watermark.ToString() + " watermark, size=" + bind.ExcessSize + ".";
                    var prompt = "Trim probable watermark of " + bind.ExcessSize + " byte" + (bind.ExcessSize!=1? "s" : "");
                    IssueModel.Add (caption, Severity.Warning, IssueTags.None, prompt, TrimWatermark);
                }
            }


            public string Rename (string newName)
            {
                string p1 = System.IO.Path.GetDirectoryName (bind.Path);
                string newPath = p1 + System.IO.Path.DirectorySeparatorChar + newName;

                try
                {
                    File.Move (bind.Path, newPath);
                    bind.Path = newPath;
                    bind.Name = newName;
                    bind.NotifyPropertyChanged ("Path");
                    bind.NotifyPropertyChanged ("Name");
                }
                catch (Exception ex)
                { return ex.Message.Trim (null); }

                return null;
            }


            /// <summary>Attempt to change this file's extension to Names[0].</summary>
            /// <returns>Error text if failure; null if success.</returns>
            public string RepairWrongExtension()
            {
                if (BaseBind.Issues.HasFatal || BaseBind.ValidNames.Length == 0)
                    return "Invalid attempt";

                foreach (var vfn in BaseBind.ValidNames)
                    if (BaseBind.NamedFormat == vfn)
                        return "Invalid attempt";

                CloseFile();
                string newPath = System.IO.Path.ChangeExtension (BaseBind.Path, BaseBind.ValidNames[0]);
                try
                {
                    File.Move (BaseBind.Path, newPath);
                }
                catch (UnauthorizedAccessException ex)
                { return ex.Message.TrimEnd (null); }
                catch (IOException ex)
                { return ex.Message.TrimEnd (null); }

                BaseBind.Path = newPath;
                BaseBind.Name = System.IO.Path.GetFileName (newPath);
                return null;
            }


            protected void ResetFile()
            {
                bind.fileMD5 = null;
                bind.fileSHA1 = null;
                bind.mediaSHA1 = null;
                bind.metaSHA1 = null;
                bind.FileSize = bind.fbs==null? 0 : bind.fbs.Length;
            }


            public string TrimWatermark()
            {
                if (BaseBind.Issues.MaxSeverity >= Severity.Error || BaseBind.Watermark != Likeliness.Probable)
                    return "Invalid attempt";

                string result = null;
                if (BaseBind.fbs != null)
                    result = TrimWatermarkUpdate();
                else
                    try
                    {
                        using (BaseBind.fbs = new FileStream (BaseBind.Path, FileMode.Open, FileAccess.Write, FileShare.Read))
                        {
                            result = TrimWatermarkUpdate();
                        }
                    }
                    finally { BaseBind.fbs = null; }

                return result;
            }


            private string TrimWatermarkUpdate()
            {
                string result = null;
                try
                {
                    TruncateExcess();
                    BaseBind.Watermark = Likeliness.None;
                }
                catch (UnauthorizedAccessException ex)
                { result = ex.Message.TrimEnd (null); }
                catch (IOException ex)
                { result = ex.Message.TrimEnd (null); }
                return result;
            }


            protected void TruncateExcess()
            {
                BaseBind.fbs.SetLength (BaseBind.FileSize - BaseBind.ExcessSize);
                BaseBind.FileSize -= BaseBind.ExcessSize;
                BaseBind.excised = BaseBind.excess;
                BaseBind.excess = null;
            }


            public void ClearFile()
            { bind.fbs = null; }


            public void CloseFile()
            {
                if (bind.fbs != null)
                {
                    bind.fbs.Dispose();
                    bind.fbs = null;
                }
            }
        }


        protected Stream fbs;
        protected long mediaPosition = -1;
        protected byte[] fBuf;     // May cache entire file.
        protected byte[] excess;   // Contents of watermark or phantom tag.
        protected byte[] excised;  // Post-repair excess.

        public string Path { get; private set; }
        public string Name { get; private set; }
        public long FileSize { get; private set; }
        public long ValidSize { get; protected set; }
        public long MediaCount { get; protected set; }
        public Likeliness Watermark { get; protected set; }
        public Issue.Vector Issues { get; protected set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged (string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged (this, new PropertyChangedEventArgs (propName));
        }

        public long ExcessSize
        { get { return excess == null? 0 : excess.Length; } }

        public string NamedFormat
        { get { return System.IO.Path.GetExtension (Path).Substring (1); } }


        public FormatBase (Stream stream, string path)
        {
            this.fbs = stream;
            this.Name = System.IO.Path.GetFileName (path);
            this.Path = path;
            this.FileSize = stream.Length;
        }


        public abstract string[] ValidNames
        { get; }

        public virtual bool IsBadHeader
        { get { return false; } }

        public virtual bool IsBadData
        { get { return false; } }

        protected byte[] metaSHA1 = null;
        public string NonmediaSHA1ToHex()
        { return metaSHA1==null? null : ConvertTo.ToHexString (metaSHA1); }

        protected byte[] mediaSHA1 = null;
        public byte[] MediaSHA1 { get { var cp = new byte[mediaSHA1.Length]; mediaSHA1.CopyTo (cp, 0); return cp; } }
        public string MediaSHA1ToHex() { return mediaSHA1==null? null : ConvertTo.ToHexString (mediaSHA1); }

        private byte[] fileMD5 = null;
        public byte[] FileMD5 { get { var cp = new byte[fileMD5.Length]; fileMD5.CopyTo (cp, 0); return cp; } }
        public string FileMD5ToHex { get { return fileMD5==null? null : ConvertTo.ToHexString (fileMD5); } }
        public bool FileMD5Equals (byte[] a2) { return fileMD5.SequenceEqual (a2); }

        protected byte[] fileSHA1 = null;
        public byte[] FileSHA1 { get { var cp = new byte[fileSHA1.Length]; fileSHA1.CopyTo (cp, 0); return cp; } }
        public string FileSHA1ToHex { get { return fileSHA1==null? null : ConvertTo.ToHexString (fileSHA1); } }

        protected byte[] fileSHA256 = null;
        public string FileSHA256ToHex { get { return fileSHA256==null? null : ConvertTo.ToHexString (fileSHA256); } }

        protected static bool StartsWith (byte[] target, byte[] other)
        {
            if (target.Length < other.Length)
                return false;

            for (int ix = 0; ix < other.Length; ++ix)
                if (target[ix] != other[ix])
                    return false;

            return true;
        }

        /// <summary>
        /// Factory method for various file formats.
        /// </summary>
        /// <param name="formats">Candidate formats for result.</param>
        /// <param name="fs0">Handle to stream of unknown type.</param>
        /// <param name="path">Full name of fs0.</param>
        /// <returns>Abstract superclass of new instance.</returns>
        static public FormatBase.ModelBase CreateModel
        (IList<FileFormat> formats, Stream fs0, string path,
            Hashes hashFlags, Validations validationFlags, string filter,
            out bool isKnown, out FileFormat actual)
        {
            isKnown = false;
            actual = null;

            FormatBase.ModelBase model = null;
            var isMisname = false;
            var ext = System.IO.Path.GetExtension (path);
            if (ext.Length < 2)
                return null;
            ext = ext.Substring(1).ToLower();

            var hdr = new byte[0x2C];

            try
            {
                // Max size of first read is kinda arbitrary.
                fs0.Read (hdr, 0, hdr.Length);

                using (var scan = formats.GetEnumerator())
                {
                    for (FileFormat other = null;;)
                    {
                        if (scan.MoveNext())
                        {
                            if (scan.Current.Names.Contains (ext))
                            {
                                isKnown = true;
                                if (scan.Current.Subname != null && scan.Current.Subname[0] == '*')
                                    other = scan.Current;
                                else
                                {
                                    model = scan.Current.ModelFactory (fs0, hdr, path);
                                    if (model != null)
                                    {
                                        actual = scan.Current;
                                        break;
                                    }
                                }
                            }
                            continue;
                        }

                        if (! isKnown && filter == null)
                            return null;

                        if (other != null)
                        {
                            actual = other;
                            if (other.Subname[0] == '*')
                                return null;
                            model = other.ModelFactory (fs0, hdr, path);
                            break;
                        }

                        scan.Reset();
                        do
                        {
                            if (! scan.MoveNext())
                                return null;
                            if (scan.Current.Names.Contains (ext))
                                continue;
                            model = scan.Current.ModelFactory (fs0, hdr, path);
                        }
                        while (model == null);

                        actual = scan.Current;
                        isKnown = true;
                        isMisname = true;
                        break;
                    }
                }

                if (model != null)
                {
                    FormatBase fmt = model.BaseBind;
                    if (! fmt.Issues.HasFatal)
                    {
                        model.CalcHashes (hashFlags, validationFlags);

                        if (isMisname)
                        {
                            // This repair should go last because it must close the file.
                            ++actual.TotalMisnamed;
                            model.IssueModel.Add ("True file format is ." + actual.PrimaryName, Severity.Warning, 0,
                                                  "Rename to extension of ." + actual.PrimaryName, model.RepairWrongExtension);
                        }
                    }

                    if (fs0.CanWrite && ! fmt.Issues.HasError && fmt.Issues.RepairableCount > 0)
                        fs0 = null;  // Keeps it open.
                    fmt.fBuf = null;
                }
            }
            finally
            {
                if (fs0 != null)
                {
                    if (model != null)
                        model.BaseBind.fbs = null;
                    fs0.Dispose();
                }
            }

            return model;
        }


        public IList<string> GetDetailsHeader (Granularity scope)
        {
            var report = new List<string>();
            if (scope >= Granularity.Verbose)
                return report;

            if (mediaSHA1 != null)
            {
                var ms1 = "Media SHA1= " + MediaSHA1ToHex();
                if (scope <= Granularity.Detail && mediaPosition >= 0)
                    ms1 += String.Format (" ({0:X4}-{1:X4})", mediaPosition, mediaPosition+MediaCount-1);
                report.Add (ms1);
            }

            if (metaSHA1 != null)
                report.Add ("Meta SHA1 = " + NonmediaSHA1ToHex());

            if (fileMD5 != null)
                report.Add ("File MD5  = " + FileMD5ToHex);

            if (fileSHA1 != null)
                report.Add ("File SHA1 = " + FileSHA1ToHex);

            if (fileSHA256 != null)
                report.Add ("File SHA256 = " + FileSHA256ToHex);

            if (scope <= Granularity.Detail)
                report.Add ("File size = " + FileSize);

            return report;
        }


        public virtual void GetDetailsBody (IList<string> report, Granularity scope)
        {
            var sb = new StringBuilder ("(");
            using (var it = ((IEnumerable<string>) ValidNames).GetEnumerator())
            {
                for (it.MoveNext();;)
                {
                    sb.Append ((string) it.Current);
                    if (! it.MoveNext())
                        break;
                    sb.Append ('/');
                }
            }
            sb.Append (')');
            report.Add (sb.ToString());
        }
    }
}

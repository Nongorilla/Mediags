using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
            protected FormatBase _data;
            public FormatBase Data => _data;
            public Issue.Vector.Model IssueModel { get; private set; }

            public ModelBase()
             => IssueModel = new Issue.Vector.Model();

            public virtual void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (IssueModel.Data.HasFatal)
                    return;

                bool hitCache = Data.fBuf != null && Data.FileSize < Int32.MaxValue;

                if ((hashFlags & Hashes.FileMD5) != 0 && Data.fileMD5 == null)
                {
                    var hasher = new Md5Hasher();
                    if (hitCache)
                        hasher.Append (Data.fBuf, 0, Data.fBuf.Length);
                    else
                        hasher.Append (Data.fbs);
                    Data.fileMD5 = hasher.GetHashAndReset();
                }

                if ((hashFlags & Hashes.FileSHA1) != 0 && Data.fileSHA1 == null)
                {
                    var hasher = new Sha1Hasher();
                    if (hitCache)
                        hasher.Append (Data.fBuf, 0, Data.fBuf.Length);
                    else
                        hasher.Append (Data.fbs);
                    Data.fileSHA1 = hasher.GetHashAndReset();
                }

                if ((hashFlags & Hashes.FileSHA256) != 0 && Data.fileSHA256 == null)
                {
                    var hasher = new Sha256Hasher();
                    if (hitCache)
                        hasher.Append (Data.fBuf, 0, Data.fBuf.Length);
                    else
                        hasher.Append (Data.fbs);
                    Data.fileSHA256 = hasher.GetHashAndReset();
                }

                if ((hashFlags & Hashes.MediaSHA1) != 0 && Data.mediaSHA1 == null)
                    if (Data.MediaCount == Data.FileSize && Data.fileSHA1 != null)
                    {
                        System.Diagnostics.Debug.Assert (Data.mediaPosition == 0);
                        Data.mediaSHA1 = Data.fileSHA1;
                    }
                    else
                    {
                        var hasher = new Sha1Hasher();
                        if (hitCache)
                            hasher.Append (Data.fBuf, (int) Data.mediaPosition, (int) Data.MediaCount);
                        else
                            hasher.Append (Data.fbs, Data.mediaPosition, Data.MediaCount);
                        Data.mediaSHA1 = hasher.GetHashAndReset();
                    }

                if ((hashFlags & Hashes.MetaSHA1) != 0 && Data.metaSHA1 == null)
                {
                    if (Data.MediaCount == 0 && Data.fileSHA1 != null)
                        Data.metaSHA1 = Data.fileSHA1;
                    else
                    {
                        var hasher = new Sha1Hasher();
                        var suffixPos = Data.mediaPosition + Data.MediaCount;
                        if (Data.mediaPosition > 0 || suffixPos < Data.FileSize)
                            if (hitCache)
                                hasher.Append (Data.fBuf, 0, (int) Data.mediaPosition, (int) suffixPos, (int) (Data.FileSize - suffixPos));
                            else
                                hasher.Append (Data.fbs, 0, Data.mediaPosition, suffixPos, Data.FileSize - suffixPos);
                        Data.metaSHA1 = hasher.GetHashAndReset();
                    }
                }
            }


            protected void CalcMark (bool assumeProbable=false)
            {
                byte[] buf = null;

                long markSize = Data.FileSize - Data.ValidSize;
                if (markSize <= 0)
                    return;

                // 1000 is somewhat arbitrary here.
                if (markSize > 1000)
                    Data.Watermark = Likeliness.Possible;
                else
                {
                    Data.fbs.Position = Data.ValidSize;
                    buf = new byte[(int) markSize];
                    int got = Data.fbs.Read (buf, 0, (int) markSize);
                    if (got != markSize)
                    {
                        IssueModel.Add ("Read failure", Severity.Fatal);
                        return;
                    }

                    Data.excess = null;
                    Data.Watermark = Likeliness.Probable;
                    if (! assumeProbable)
                        for (int ix = 0; ix < buf.Length; ++ix)
                        {
                            // Fuzzy ID of watermark bytes by checking if ASCII text. Purpose is to
                            // exclude .AVI files that do not have correct encoded sizes - a common issue.
                            var bb = buf[ix];
                            if (bb > 127 || (bb < 32 && bb != 0 && bb != 9 && bb != 0x0A && bb != 0x0D))
                            {
                                Data.Watermark = Likeliness.Possible;
                                break;
                            }
                        }
                }

                if (Data.Watermark == Likeliness.Probable)
                {
                    Data.excess = buf;
                    var caption = Data.Watermark.ToString() + " watermark, size=" + Data.ExcessSize + ".";
                    var prompt = "Trim probable watermark of " + Data.ExcessSize + " byte" + (Data.ExcessSize!=1? "s" : "");
                    IssueModel.Add (caption, Severity.Warning, IssueTags.None, prompt, TrimWatermark);
                }
            }


            public string Rename (string newName)
            {
                string p1 = System.IO.Path.GetDirectoryName (Data.Path);
                string newPath = p1 + System.IO.Path.DirectorySeparatorChar + newName;

                try
                {
                    File.Move (Data.Path, newPath);
                    Data.Path = newPath;
                    Data.Name = newName;
                    Data.NotifyPropertyChanged ("Path");
                    Data.NotifyPropertyChanged ("Name");
                }
                catch (Exception ex)
                { return ex.Message.Trim (null); }

                return null;
            }


            /// <summary>Attempt to change this file's extension to Names[0].</summary>
            /// <returns>Error text if failure; null if success.</returns>
            public string RepairWrongExtension()
            {
                if (Data.Issues.HasFatal || Data.ValidNames.Length == 0)
                    return "Invalid attempt";

                foreach (var vfn in Data.ValidNames)
                    if (Data.NamedFormat == vfn)
                        return "Invalid attempt";

                CloseFile();
                string newPath = System.IO.Path.ChangeExtension (Data.Path, Data.ValidNames[0]);
                try
                {
                    File.Move (Data.Path, newPath);
                }
                catch (UnauthorizedAccessException ex)
                { return ex.Message.TrimEnd (null); }
                catch (IOException ex)
                { return ex.Message.TrimEnd (null); }

                Data.Path = newPath;
                Data.Name = System.IO.Path.GetFileName (newPath);
                return null;
            }


            protected void ResetFile()
            {
                Data.fileMD5 = null;
                Data.fileSHA1 = null;
                Data.mediaSHA1 = null;
                Data.metaSHA1 = null;
                Data.FileSize = Data.fbs==null? 0 : Data.fbs.Length;
            }


            public string TrimWatermark()
            {
                if (Data.Issues.MaxSeverity >= Severity.Error || Data.Watermark != Likeliness.Probable)
                    return "Invalid attempt";

                string result = null;
                if (Data.fbs != null)
                    result = TrimWatermarkUpdate();
                else
                    try
                    {
                        using (Data.fbs = new FileStream (Data.Path, FileMode.Open, FileAccess.Write, FileShare.Read))
                        {
                            result = TrimWatermarkUpdate();
                        }
                    }
                    finally { Data.fbs = null; }

                return result;
            }


            private string TrimWatermarkUpdate()
            {
                string result = null;
                try
                {
                    TruncateExcess();
                    Data.Watermark = Likeliness.None;
                }
                catch (UnauthorizedAccessException ex)
                { result = ex.Message.TrimEnd (null); }
                catch (IOException ex)
                { result = ex.Message.TrimEnd (null); }
                return result;
            }


            protected void TruncateExcess()
            {
                Data.fbs.SetLength (Data.FileSize - Data.ExcessSize);
                Data.FileSize -= Data.ExcessSize;
                Data.excised = Data.excess;
                Data.excess = null;
            }


            public void ClearFile()
            { Data.fbs = null; }


            public void CloseFile()
            {
                if (Data.fbs != null)
                {
                    Data.fbs.Dispose();
                    Data.fbs = null;
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


        protected FormatBase (Stream stream, string path)
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
        public bool HasMediaSHA1 => mediaSHA1 != null;

        private byte[] fileMD5 = null;
        public byte[] FileMD5 { get { var cp = new byte[fileMD5.Length]; fileMD5.CopyTo (cp, 0); return cp; } }
        public string FileMD5ToHex { get { return fileMD5==null? null : ConvertTo.ToHexString (fileMD5); } }
        public bool FileMD5Equals (byte[] a2) { return fileMD5.SequenceEqual (a2); }
        public bool HasFileMD5 => fileMD5 != null;

        protected byte[] fileSHA1 = null;
        public byte[] FileSHA1 { get { var cp = new byte[fileSHA1.Length]; fileSHA1.CopyTo (cp, 0); return cp; } }
        public string FileSHA1ToHex { get { return fileSHA1==null? null : ConvertTo.ToHexString (fileSHA1); } }
        public bool HasFileSHA1 => fileSHA1 != null;

        protected byte[] fileSHA256 = null;
        public string FileSHA256ToHex { get { return fileSHA256==null? null : ConvertTo.ToHexString (fileSHA256); } }
        public bool HasFileSHA256 => fileSHA256 != null;

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
                    FormatBase fmt = model.Data;
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
                        model.Data.fbs = null;
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

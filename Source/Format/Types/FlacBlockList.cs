using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace NongFormat
{
    public enum FlacBlockType
    { StreamInfo, Padding, Application, SeekTable, Tags, CueSheet, Picture }

    // FLAC lifted this list from the ID3v2 spec.
    public enum PicType
    {
        Other,
        Icon32, Icon, Front, Back, Leaflet, Disc, Lead, Artist, Conductor, Band,
        Composer, Lyricist, Location, Recording, Performance, Capture, Fish, Illustration, Logo, PublisherLogo
    };


    public abstract class FlacBlockItem
    {
        public int Size { get; private set; }
        public string Name { get { return BlockType.ToString(); } }

        public FlacBlockItem (int size)
        { this.Size = size; }

        public abstract FlacBlockType BlockType
        { get; }

        public override string ToString ()
        {
            return BlockType.ToString();
        }
    }


    public class FlacPadBlock : FlacBlockItem
    {
        public FlacPadBlock (int size) : base (size)
        { }

        public override FlacBlockType BlockType
        { get { return FlacBlockType.Padding; } }
    }


    public class FlacAppBlock : FlacBlockItem
    {
        public int ApplicationId { get; private set; }

        public FlacAppBlock (int size, int appId) : base (size)
        {
            this.ApplicationId = appId;
        }

        public override FlacBlockType BlockType
        { get { return FlacBlockType.Application; } }
    }


    public class FlacSeekTableBlock : FlacBlockItem
    {
        private byte[] table;

        public FlacSeekTableBlock (int size, byte[] table) : base (size)
        {
            this.table = table;
        }

        public override FlacBlockType BlockType
        { get { return FlacBlockType.SeekTable; } }

        public override string ToString ()
        {
            string result = base.ToString();
            int kk = table.Length / 18;
             result += " (" + kk + ")";
            return result;
        }
    }


    public class FlacTagsBlock : FlacBlockItem
    {
        public int StoredTagCount { get; private set; }
        private byte[] tagData;
        public string Vendor { get; private set; }
        private readonly List<string> lines;
        public ReadOnlyCollection<string> Lines { get; private set; }

        public string TagName (int index)
        {
            string lx = lines[index];
            int eqPos = lx.IndexOf ('=');
            return lines[index].Substring (0, eqPos);
        }

        public string TagValue (int index)
        {
            string lx = lines[index];
            int eqPos = lx.IndexOf ('=');
            return lines[index].Substring (eqPos+1);
        }

        public string[] GetTagValues (string tagName)
        {
            int tt = 0, tn = 0;
            for (int ii = 0; ii < lines.Count; ++ii)
                if (TagName (ii).ToLower() == tagName.ToLower())
                    ++tt;

            var result = new string[tt];
            for (int ii = 0; ii < lines.Count; ++ii)
                if (TagName (ii).ToLower() == tagName.ToLower())
                {
                    result[tn] = TagValue (ii);
                    ++tn;
                }
            return result;
        }

        public string GetTagValuesAppended (string tagName)
        {
            string result = null;
            for (int ii = 0; ii < lines.Count; ++ii)
                if (TagName (ii).ToLower() == tagName.ToLower())
                    if (result == null)
                        result = TagValue (ii);
                    else
                        result += @"\\" + TagValue (ii);
            return result;
        }

        public FlacTagsBlock (int size, byte[] rawTagData) : base (size)
        {
            this.lines = new List<string>();
            this.Lines = new ReadOnlyCollection<string> (this.lines);

            this.tagData = rawTagData;

            int len = ConvertTo.FromLit32ToInt32 (tagData, 0);
            Vendor = Encoding.UTF8.GetString (tagData, 4, len);
            int pos = len + 8;
            StoredTagCount = ConvertTo.FromLit32ToInt32 (tagData, pos - 4);

            for (var tn = 1; tn <= StoredTagCount; ++tn)
            {
                if (pos > tagData.Length)
                    break;

                len = ConvertTo.FromLit32ToInt32 (tagData, pos);
                pos += 4;
                lines.Add (Encoding.UTF8.GetString (tagData, pos, len));
                pos += len;
            }
            System.Diagnostics.Debug.Assert (pos == tagData.Length);
        }

        public override FlacBlockType BlockType
        { get { return FlacBlockType.Tags; } }
    }


    public class FlacCuesheetBlock : FlacBlockItem
    {
        public bool IsCD { get; private set; }
        public int TrackCount { get; private set; }

        public FlacCuesheetBlock (int size, bool isCD, int trackCount) : base (size)
        {
            this.IsCD = isCD;
            this.TrackCount = trackCount;
        }

        public override FlacBlockType BlockType
        { get { return FlacBlockType.CueSheet; } }

        public override string ToString ()
        {
            string result = base.ToString();
            if (IsCD)
                result += "-CD";
            return result;
        }
    }


    public class FlacPicBlock : FlacBlockItem
    {
        public PicType PicType { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public FlacPicBlock (int size, PicType picType, int width, int height) : base (size)
        {
            this.PicType = picType;
            this.Width = width;
            this.Height = height;
        }

        public override FlacBlockType BlockType
        { get { return FlacBlockType.Picture; } }

        public override string ToString ()
        {
            return base.ToString() + " (" + PicType + "-" + Width + "x" + Height + ")";
        }
    }


    public class FlacBlockList
    {
        private readonly List<FlacBlockItem> items;
        public ReadOnlyCollection<FlacBlockItem> Items { get; private set; }

        public FlacTagsBlock Tags { get; private set; }

        public FlacBlockList()
        {
            this.items = new List<FlacBlockItem>();
            this.Items = new ReadOnlyCollection<FlacBlockItem> (this.items);
        }

        public void AddPad (int size)
        {
            var block = new FlacPadBlock (size);
            items.Add (block);
        }

        public void AddApp (int size, int appId)
        {
            var block = new FlacAppBlock (size, appId);
            items.Add (block);
        }

        public void AddSeekTable (int size, byte[] table)
        {
            var block = new FlacSeekTableBlock (size, table);
            items.Add (block);
        }

        public void AddTags (int size, byte[] rawTags)
        {
            System.Diagnostics.Debug.Assert (Tags == null);

            Tags = new FlacTagsBlock (size, rawTags);
            items.Add (Tags);
        }

        public void AddCuesheet (int size, bool isCD, int trackCount)
        {
            var block = new FlacCuesheetBlock (size, isCD, trackCount);
            items.Add (block);
        }

        public void AddPic (int size, PicType picType, int width, int height)
        {
            var pic = new FlacPicBlock (size, picType, width, height);
            items.Add (pic);
        }

    }
}

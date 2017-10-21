//
// File:    GenMap1252.cs
// Project: Mediags (code generator)
// Purpose: Generate NongExtensions.map1252[] values for copy/paste.
// Usage:   Download and file in exe directory:
//          www.unicode.org/Public/UCD/latest/ucd/UnicodeData.txt
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;

namespace AppMain
{
    class Payload
    {
        public byte Octet { get; private set; }
        public string Desc { get; private set; }
        public Payload (byte octet, string desc) { this.Octet = octet; this.Desc = desc; }
        public Payload (byte char1252, char uniChar)
        {
            this.Octet = char1252;
            var toHex = ((int) uniChar).ToString ("X4");
            this.Desc = GenMap1252.udb.First (xx => xx[0]==toHex)[1];
        }
        public override string ToString() { return Octet.ToString ("X2"); }
    }


    class GenMap1252
    {
        public static readonly List<string[]> udb = new List<string[]>();
        private static readonly Encoding cp1252 = Encoding.GetEncoding (1252, new EncoderReplacementFallback (""), new DecoderReplacementFallback ("="));

        public static readonly SortedDictionary<int,Payload> map1252 = new SortedDictionary<int,Payload>();
        static void map1252Add (int codePoint, byte octet1252)
        {
            var toHex = codePoint.ToString ("X4");
            var desc = GenMap1252.udb.First (xx => xx[0]==toHex)[1] + "††";
            map1252.Add (codePoint, new Payload (octet1252, desc));
        }
        static void map1252Add (int codePoint, byte octet1252, string desc)
        {
            map1252.Add (codePoint, new Payload (octet1252, desc));
        }

        static void Main (string[] args)
        {
            // See comment at top for usage.
            using (var db = new StreamReader ("UnicodeData.txt"))
                while (! db.EndOfStream)
                    udb.Add (db.ReadLine().Split(';'));

            for (byte[] b1252 = new byte[] { 0 }; ; ++b1252[0])
            {
                string utf16 = cp1252.GetString (b1252);
                int p32 = Char.ConvertToUtf32 (utf16, 0);
                var row = udb.First (xx => Int32.Parse (xx[0], NumberStyles.AllowHexSpecifier) == p32);
                if (p32 != b1252[0])
                    GenMap1252.map1252Add (p32, b1252[0], row[1] + "**");

                if (b1252[0] == 0xFF) break;
            }
            var totalExactMaps = GenMap1252.map1252.Count;

            foreach (var lx in udb)
                if (lx[1].Contains ("LATIN"))
                {
                    for (var ch = 'A'; ch <= 'Z'; ++ch)
                        ProcessLine (ch, "LATIN CAPITAL LETTER ", lx);
                    for (var ch = 'a'; ch <= 'z'; ++ch)
                        ProcessLine (ch, "LATIN SMALL LETTER ", lx);
                }

            // There are many more potential custom remaps like these:
            map1252Add (Char.ConvertToUtf32 ("⁓", 0), (byte) '~');
            map1252Add (Char.ConvertToUtf32 ("‒", 0), (byte) '-');
            map1252Add (Char.ConvertToUtf32 ("―", 0), (byte) '-');

            Console.WriteLine ("// Generated from v8.0 of www.unicode.org/Public/UCD/latest/ucd/UnicodeData.txt");
            Console.WriteLine ("// Total = " + map1252.Count + ", Scrubbed = " + (map1252.Count - totalExactMaps));
            var b1 = new byte[1];
            var countdown = map1252.Count;
            foreach (var kv in map1252)
            {
                char delim = --countdown == 0? ' ': ',';
                b1[0] = kv.Value.Octet;
                char[] char1252 = cp1252.GetChars (b1);
                Console.WriteLine ("0x" + kv.Key.ToString ("X6") + kv.Value.Octet.ToString("X2") + delim + " // " + kv.Value.Desc);
            }

            /* Output:

            0x00010041, // LATIN CAPITAL LETTER A WITH MACRON
            0x00010161, // LATIN SMALL LETTER A WITH MACRON
            .
            .
            .
            0x0E007979, // TAG LATIN SMALL LETTER Y
            0x0E007A7A  // TAG LATIN SMALL LETTER Z

            */
        }


        static void ProcessLine (char cleanLetter, string filterText, string[] lx)
        {
            if (lx[1].Contains (filterText + Char.ToUpper(cleanLetter) + " WITH") || lx[1].EndsWith (filterText + Char.ToUpper(cleanLetter)))
            {
                int point;
                var isOk = Int32.TryParse (lx[0], NumberStyles.AllowHexSpecifier, null, out point);
                System.Diagnostics.Debug.Assert (isOk);

                string utf16Char = Char.ConvertFromUtf32 (point);
                byte[] utf16Bytes = Encoding.Unicode.GetBytes (utf16Char);
                byte[] cp1252Byte = Encoding.Convert (Encoding.Unicode, cp1252, utf16Bytes);

                if (cp1252Byte.Length == 0)
                    GenMap1252.map1252Add (point, (byte) cleanLetter, lx[1]);
            }
        }
    }
}

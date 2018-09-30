using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NongFormat
{
    public static partial class StringBuilderExtensions
    {
        public static StringBuilder AppendHexString (this StringBuilder sb, byte[] data)
        {
            foreach (byte octet in data)
                sb.Append (octet.ToString ("X2"));
            return sb;
        }
    }


    public static partial class StreamExtensions
    {
        // Wobbly Transformation Format 8
        // 1- to 7-byte uncooked & extended UTF-8 for up to 36 bits of storage.
        // See wikipedia.org/wiki/UTF-8#WTF-8
        // On exit:
        //   returns <0 if read or encoding error;
        //   else returns decoding with stream advanced to next unread byte, buf contains encoding.
        public static long ReadWobbly (this Stream stream, out byte[] buf)
        {
            int octet = stream.ReadByte();
            if (octet < 0)
            { buf = null; return octet; }

            if ((octet & 0x80) == 0)
            { buf = new byte[] { (byte) octet }; return octet; }

            if (octet == 0xFF)
            { buf = new byte[] { (byte) octet }; return -1; }

            int followCount = 1;
            byte mask;
            for (mask = 0x20; (octet & mask) != 0; mask >>= 1)
                ++followCount;

            buf = new byte[1+followCount];
            buf[0] = (byte) octet;
            int got = stream.Read (buf, 1, followCount);
            if (got != followCount)
                return -9;

            long result = octet & (mask-1);
            for (int ix = 1; ix < buf.Length; ++ix)
            {
                octet = buf[ix];
                if ((octet & 0xC0) != 0x80)
                    return -ix-1;
                result = (result << 6) | (uint) (octet & 0x3F);
            }

            return result;
        }
    }


    /// <summary>
    /// Extend the functionality of System.Convert and System.BitConverter.
    /// </summary>
    public static class ConvertTo
    {
        public static Int32 FromBig16ToInt32 (byte[] data, int index)
        { unchecked { return data[index] << 8 | data[index+1]; } }

        public static Int32 FromBig24ToInt32 (byte[] data, int index)
        { unchecked { return data[index] << 16 | data[index+1] << 8 | data[index+2]; } }

        public static UInt32 FromBig24ToUInt32 (byte[] data, int index)
        { unchecked { return (UInt32) data[index] << 16 | (UInt32) data[index+1] << 8 | data[index+2]; } }

        public static Int32 FromBig32ToInt32 (byte[] data, int index)
        { unchecked { return data[index] << 24 | data[index+1] << 16 | data[index+2] << 8 | data[index+3]; } }

        public static UInt32 FromBig32ToUInt32 (byte[] data, int index)
        { unchecked { return (UInt32) data[index] << 24 | (UInt32) data[index+1] << 16 | (UInt32) data[index+2] << 8 | data[index+3]; } }

        public static Int32 FromLit16ToInt32 (byte[] data, int index)
        { unchecked { return data[index] | data[index+1] << 8; } }

        public static Int32 FromLit32ToInt32 (byte[] data, int index)
        { unchecked { return data[index] | data[index+1] << 8 | data[index+2] << 16 | data[index+3] << 24; } }

        public static UInt32 FromLit32ToUInt32 (byte[] data, int index)
        { unchecked { return data[index] | (UInt32) data[index+1] << 8 | (UInt32) data[index+2] << 16 | (UInt32) data[index+3] << 24; } }


        public static string FromAsciizToString (byte[] data, int offset = 0)
        {
            int stop = offset;
            while (stop < data.Length && data[stop] != 0)
                ++stop;
            return Encoding.ASCII.GetString (data, offset, stop - offset);
        }


        public static string FromAsciiToString (byte[] data, int offset, int length)
        {
            string result = String.Empty;
            for (; --length >= 0; ++offset)
                if (data[offset] >= 32 && data[offset] < 127)
                    result += (char) data[offset];
                else
                    break;
            return result;
        }


        public static string ToBitString (byte[] data, int length)
        {
            var sb = new StringBuilder (length * 10 - 1);
            for (int ix = 0;;)
            {
                for (int mask = 0x80;;)
                {
                    sb.Append ((data[ix] & mask) == 0 ? '0' : '1');
                    mask >>= 1;
                    if (mask == 0)
                        break;
                    else if (mask == 8)
                        sb.Append (' ');
                }
                if (++ix >= length)
                    break;
                sb.Append (' ');
            }
            return sb.ToString();
        }


        public static string ToBitString (int value, int bitCount)
        {
            var sb = new StringBuilder (bitCount + (bitCount>>2));
            if (bitCount < 0 || bitCount >= 32)
            {
                sb.Append ((value & 0x80000000) == 0 ? '0' : '1');
                bitCount = 31;
            }
            for (int mask = 1 << (bitCount - 1);;)
            {
                sb.Append ((value & mask) == 0 ? '0' : '1');
                mask >>= 1;
                if (mask == 0)
                    break;
                if ((mask & 0x08888888) != 0)
                    sb.Append (' ');
            }
            return sb.ToString();
        }


        public static string ToHexString (byte[] data)
        {
            var sb = new StringBuilder (data.Length * 2);
            return sb.AppendHexString (data).ToString();
        }


        public static byte[] FromHexStringToBytes (string hs, int start, int len)
        {
            var hash = new byte[len];
            for (var hx = 0; hx < len; ++hx)
            {
                if (! Byte.TryParse (hs.Substring (start+hx*2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte octet))
                    return null;
                hash[hx] = octet;
            }
            return hash;
        }
    }


    public static class Map1252
    {
        public static int Length => kv.Length;
        public static int At (int index) => kv[index];
        public static int ArrayBinarySearch (int searchKey) => Array.BinarySearch<int> (kv, searchKey);

        public static int To1252Bestfit (int codePoint)
        {
            System.Diagnostics.Debug.Assert (codePoint >= 0 && codePoint <= 0x10FFFF);

            if (codePoint <= 0xFF)
                return codePoint;

            int key = codePoint << 8;
            unchecked
            {
                // Binary search with deferred equality detection.
                // Another way is Array.BinarySearch but this method benchmarks 30% better.
                for (int lo = 0, hi = kv.Length;;)  // inclusive low, exclusive high
                {
                    int width = hi - lo;
                    if (width == 0)
                    {
                        if (lo < kv.Length)
                        {
                            int val = kv[lo] - key;
                            if (val >= 0 && val <= 0xFF)
                                return (byte) val;
                        }
                        return -1;
                    }
                    // The jitter is flakey about IDIV or SAR, so force SAR for speed.
                    int pivot = lo + (width >> 1);
                    if (kv[pivot] - key < 0)
                        lo = pivot + 1;
                    else
                        hi = pivot;
                }
            }
        }

        public static string ToClean1252FileName (string dirtyUnicode)
        {
            var b1 = new byte[1];
            var sb = new StringBuilder (dirtyUnicode.Length);

            // Names can't end with a period or space.
            int usableLength;
            for (usableLength = dirtyUnicode.Length; usableLength > 0; --usableLength)
            {
                char lastChar = dirtyUnicode[usableLength-1];
                if (lastChar != '.' && lastChar !=' ')
                    break;
            }

            for (var ix = 0; ix < usableLength; ++ix)
            {
                char cu = dirtyUnicode[ix];
                if (Path.GetInvalidFileNameChars().Contains (cu))
                    switch (cu)
                    {
                        case '/':
                            if (ix == 0 || ix >= dirtyUnicode.Length-7 || dirtyUnicode.Substring (ix-1, 3) != " / ")
                                sb.Append ('-');
                            else
                            {
                                // Replace " / " with "; ".
                                sb.Remove (sb.Length-1, 1);
                                sb.Append ("; ");
                                ++ix;
                            }
                            continue;

                        case ':':
                            sb.Append (ix == 0 || ix >= dirtyUnicode.Length-7 || dirtyUnicode[ix+1] != ' '? "." : " -");
                            continue;

                        case '?': continue;
                        case '"': sb.Append ('\''); continue;
                        case '<': sb.Append ('‹'); continue;
                        case '>': sb.Append ('›'); continue;
                        default:  sb.Append ('-'); continue;
                    }

                int codePoint = Char.ConvertToUtf32 (dirtyUnicode, ix);
                int refit = To1252Bestfit (codePoint);
                if (Char.IsHighSurrogate (cu))
                    ++ix;

                if (refit >= 0)
                {
                    b1[0] = (byte) refit;
                    char[] cleanUnicode = LogBuffer.cp1252.GetChars (b1);
                    sb.Append (cleanUnicode);
                    continue;
                }

                // No refit to 1252 so dash it out.
                sb.Append ('-');
            }

            return sb.ToString();
        }

        // Best fit Unicode to Windows-1252 map. Byte layout is key-key-key-value.
        private static readonly int[] kv = new int[]
        {
            // Generated from v8.0 of www.unicode.org/Public/UCD/latest/ucd/UnicodeData.txt
            0x00010041, // LATIN CAPITAL LETTER A WITH MACRON
            0x00010161, // LATIN SMALL LETTER A WITH MACRON
            0x00010241, // LATIN CAPITAL LETTER A WITH BREVE
            0x00010361, // LATIN SMALL LETTER A WITH BREVE
            0x00010441, // LATIN CAPITAL LETTER A WITH OGONEK
            0x00010561, // LATIN SMALL LETTER A WITH OGONEK
            0x00010643, // LATIN CAPITAL LETTER C WITH ACUTE
            0x00010763, // LATIN SMALL LETTER C WITH ACUTE
            0x00010843, // LATIN CAPITAL LETTER C WITH CIRCUMFLEX
            0x00010963, // LATIN SMALL LETTER C WITH CIRCUMFLEX
            0x00010A43, // LATIN CAPITAL LETTER C WITH DOT ABOVE
            0x00010B63, // LATIN SMALL LETTER C WITH DOT ABOVE
            0x00010C43, // LATIN CAPITAL LETTER C WITH CARON
            0x00010D63, // LATIN SMALL LETTER C WITH CARON
            0x00010E44, // LATIN CAPITAL LETTER D WITH CARON
            0x00010F64, // LATIN SMALL LETTER D WITH CARON
            0x00011044, // LATIN CAPITAL LETTER D WITH STROKE
            0x00011164, // LATIN SMALL LETTER D WITH STROKE
            0x00011245, // LATIN CAPITAL LETTER E WITH MACRON
            0x00011365, // LATIN SMALL LETTER E WITH MACRON
            0x00011445, // LATIN CAPITAL LETTER E WITH BREVE
            0x00011565, // LATIN SMALL LETTER E WITH BREVE
            0x00011645, // LATIN CAPITAL LETTER E WITH DOT ABOVE
            0x00011765, // LATIN SMALL LETTER E WITH DOT ABOVE
            0x00011845, // LATIN CAPITAL LETTER E WITH OGONEK
            0x00011965, // LATIN SMALL LETTER E WITH OGONEK
            0x00011A45, // LATIN CAPITAL LETTER E WITH CARON
            0x00011B65, // LATIN SMALL LETTER E WITH CARON
            0x00011C47, // LATIN CAPITAL LETTER G WITH CIRCUMFLEX
            0x00011D67, // LATIN SMALL LETTER G WITH CIRCUMFLEX
            0x00011E47, // LATIN CAPITAL LETTER G WITH BREVE
            0x00011F67, // LATIN SMALL LETTER G WITH BREVE
            0x00012047, // LATIN CAPITAL LETTER G WITH DOT ABOVE
            0x00012167, // LATIN SMALL LETTER G WITH DOT ABOVE
            0x00012247, // LATIN CAPITAL LETTER G WITH CEDILLA
            0x00012367, // LATIN SMALL LETTER G WITH CEDILLA
            0x00012448, // LATIN CAPITAL LETTER H WITH CIRCUMFLEX
            0x00012568, // LATIN SMALL LETTER H WITH CIRCUMFLEX
            0x00012648, // LATIN CAPITAL LETTER H WITH STROKE
            0x00012768, // LATIN SMALL LETTER H WITH STROKE
            0x00012849, // LATIN CAPITAL LETTER I WITH TILDE
            0x00012969, // LATIN SMALL LETTER I WITH TILDE
            0x00012A49, // LATIN CAPITAL LETTER I WITH MACRON
            0x00012B69, // LATIN SMALL LETTER I WITH MACRON
            0x00012C49, // LATIN CAPITAL LETTER I WITH BREVE
            0x00012D69, // LATIN SMALL LETTER I WITH BREVE
            0x00012E49, // LATIN CAPITAL LETTER I WITH OGONEK
            0x00012F69, // LATIN SMALL LETTER I WITH OGONEK
            0x00013049, // LATIN CAPITAL LETTER I WITH DOT ABOVE
            0x0001344A, // LATIN CAPITAL LETTER J WITH CIRCUMFLEX
            0x0001356A, // LATIN SMALL LETTER J WITH CIRCUMFLEX
            0x0001364B, // LATIN CAPITAL LETTER K WITH CEDILLA
            0x0001376B, // LATIN SMALL LETTER K WITH CEDILLA
            0x0001394C, // LATIN CAPITAL LETTER L WITH ACUTE
            0x00013A6C, // LATIN SMALL LETTER L WITH ACUTE
            0x00013B4C, // LATIN CAPITAL LETTER L WITH CEDILLA
            0x00013C6C, // LATIN SMALL LETTER L WITH CEDILLA
            0x00013D4C, // LATIN CAPITAL LETTER L WITH CARON
            0x00013E6C, // LATIN SMALL LETTER L WITH CARON
            0x00013F4C, // LATIN CAPITAL LETTER L WITH MIDDLE DOT
            0x0001406C, // LATIN SMALL LETTER L WITH MIDDLE DOT
            0x0001414C, // LATIN CAPITAL LETTER L WITH STROKE
            0x0001426C, // LATIN SMALL LETTER L WITH STROKE
            0x0001434E, // LATIN CAPITAL LETTER N WITH ACUTE
            0x0001446E, // LATIN SMALL LETTER N WITH ACUTE
            0x0001454E, // LATIN CAPITAL LETTER N WITH CEDILLA
            0x0001466E, // LATIN SMALL LETTER N WITH CEDILLA
            0x0001474E, // LATIN CAPITAL LETTER N WITH CARON
            0x0001486E, // LATIN SMALL LETTER N WITH CARON
            0x00014C4F, // LATIN CAPITAL LETTER O WITH MACRON
            0x00014D6F, // LATIN SMALL LETTER O WITH MACRON
            0x00014E4F, // LATIN CAPITAL LETTER O WITH BREVE
            0x00014F6F, // LATIN SMALL LETTER O WITH BREVE
            0x0001504F, // LATIN CAPITAL LETTER O WITH DOUBLE ACUTE
            0x0001516F, // LATIN SMALL LETTER O WITH DOUBLE ACUTE
            0x0001528C, // LATIN CAPITAL LIGATURE OE**
            0x0001539C, // LATIN SMALL LIGATURE OE**
            0x00015452, // LATIN CAPITAL LETTER R WITH ACUTE
            0x00015572, // LATIN SMALL LETTER R WITH ACUTE
            0x00015652, // LATIN CAPITAL LETTER R WITH CEDILLA
            0x00015772, // LATIN SMALL LETTER R WITH CEDILLA
            0x00015852, // LATIN CAPITAL LETTER R WITH CARON
            0x00015972, // LATIN SMALL LETTER R WITH CARON
            0x00015A53, // LATIN CAPITAL LETTER S WITH ACUTE
            0x00015B73, // LATIN SMALL LETTER S WITH ACUTE
            0x00015C53, // LATIN CAPITAL LETTER S WITH CIRCUMFLEX
            0x00015D73, // LATIN SMALL LETTER S WITH CIRCUMFLEX
            0x00015E53, // LATIN CAPITAL LETTER S WITH CEDILLA
            0x00015F73, // LATIN SMALL LETTER S WITH CEDILLA
            0x0001608A, // LATIN CAPITAL LETTER S WITH CARON**
            0x0001619A, // LATIN SMALL LETTER S WITH CARON**
            0x00016254, // LATIN CAPITAL LETTER T WITH CEDILLA
            0x00016374, // LATIN SMALL LETTER T WITH CEDILLA
            0x00016454, // LATIN CAPITAL LETTER T WITH CARON
            0x00016574, // LATIN SMALL LETTER T WITH CARON
            0x00016654, // LATIN CAPITAL LETTER T WITH STROKE
            0x00016774, // LATIN SMALL LETTER T WITH STROKE
            0x00016855, // LATIN CAPITAL LETTER U WITH TILDE
            0x00016975, // LATIN SMALL LETTER U WITH TILDE
            0x00016A55, // LATIN CAPITAL LETTER U WITH MACRON
            0x00016B75, // LATIN SMALL LETTER U WITH MACRON
            0x00016C55, // LATIN CAPITAL LETTER U WITH BREVE
            0x00016D75, // LATIN SMALL LETTER U WITH BREVE
            0x00016E55, // LATIN CAPITAL LETTER U WITH RING ABOVE
            0x00016F75, // LATIN SMALL LETTER U WITH RING ABOVE
            0x00017055, // LATIN CAPITAL LETTER U WITH DOUBLE ACUTE
            0x00017175, // LATIN SMALL LETTER U WITH DOUBLE ACUTE
            0x00017255, // LATIN CAPITAL LETTER U WITH OGONEK
            0x00017375, // LATIN SMALL LETTER U WITH OGONEK
            0x00017457, // LATIN CAPITAL LETTER W WITH CIRCUMFLEX
            0x00017577, // LATIN SMALL LETTER W WITH CIRCUMFLEX
            0x00017659, // LATIN CAPITAL LETTER Y WITH CIRCUMFLEX
            0x00017779, // LATIN SMALL LETTER Y WITH CIRCUMFLEX
            0x0001789F, // LATIN CAPITAL LETTER Y WITH DIAERESIS**
            0x0001795A, // LATIN CAPITAL LETTER Z WITH ACUTE
            0x00017A7A, // LATIN SMALL LETTER Z WITH ACUTE
            0x00017B5A, // LATIN CAPITAL LETTER Z WITH DOT ABOVE
            0x00017C7A, // LATIN SMALL LETTER Z WITH DOT ABOVE
            0x00017D8E, // LATIN CAPITAL LETTER Z WITH CARON**
            0x00017E9E, // LATIN SMALL LETTER Z WITH CARON**
            0x00018062, // LATIN SMALL LETTER B WITH STROKE
            0x00018142, // LATIN CAPITAL LETTER B WITH HOOK
            0x00018242, // LATIN CAPITAL LETTER B WITH TOPBAR
            0x00018362, // LATIN SMALL LETTER B WITH TOPBAR
            0x00018743, // LATIN CAPITAL LETTER C WITH HOOK
            0x00018863, // LATIN SMALL LETTER C WITH HOOK
            0x00018A44, // LATIN CAPITAL LETTER D WITH HOOK
            0x00018B44, // LATIN CAPITAL LETTER D WITH TOPBAR
            0x00018C64, // LATIN SMALL LETTER D WITH TOPBAR
            0x00019146, // LATIN CAPITAL LETTER F WITH HOOK
            0x00019283, // LATIN SMALL LETTER F WITH HOOK**
            0x00019347, // LATIN CAPITAL LETTER G WITH HOOK
            0x00019749, // LATIN CAPITAL LETTER I WITH STROKE
            0x0001984B, // LATIN CAPITAL LETTER K WITH HOOK
            0x0001996B, // LATIN SMALL LETTER K WITH HOOK
            0x00019A6C, // LATIN SMALL LETTER L WITH BAR
            0x00019D4E, // LATIN CAPITAL LETTER N WITH LEFT HOOK
            0x00019E6E, // LATIN SMALL LETTER N WITH LONG RIGHT LEG
            0x00019F4F, // LATIN CAPITAL LETTER O WITH MIDDLE TILDE
            0x0001A04F, // LATIN CAPITAL LETTER O WITH HORN
            0x0001A16F, // LATIN SMALL LETTER O WITH HORN
            0x0001A450, // LATIN CAPITAL LETTER P WITH HOOK
            0x0001A570, // LATIN SMALL LETTER P WITH HOOK
            0x0001AB74, // LATIN SMALL LETTER T WITH PALATAL HOOK
            0x0001AC54, // LATIN CAPITAL LETTER T WITH HOOK
            0x0001AD74, // LATIN SMALL LETTER T WITH HOOK
            0x0001AE54, // LATIN CAPITAL LETTER T WITH RETROFLEX HOOK
            0x0001AF55, // LATIN CAPITAL LETTER U WITH HORN
            0x0001B075, // LATIN SMALL LETTER U WITH HORN
            0x0001B256, // LATIN CAPITAL LETTER V WITH HOOK
            0x0001B359, // LATIN CAPITAL LETTER Y WITH HOOK
            0x0001B479, // LATIN SMALL LETTER Y WITH HOOK
            0x0001B55A, // LATIN CAPITAL LETTER Z WITH STROKE
            0x0001B67A, // LATIN SMALL LETTER Z WITH STROKE
            0x0001C544, // LATIN CAPITAL LETTER D WITH SMALL LETTER Z WITH CARON
            0x0001C84C, // LATIN CAPITAL LETTER L WITH SMALL LETTER J
            0x0001CB4E, // LATIN CAPITAL LETTER N WITH SMALL LETTER J
            0x0001CD41, // LATIN CAPITAL LETTER A WITH CARON
            0x0001CE61, // LATIN SMALL LETTER A WITH CARON
            0x0001CF49, // LATIN CAPITAL LETTER I WITH CARON
            0x0001D069, // LATIN SMALL LETTER I WITH CARON
            0x0001D14F, // LATIN CAPITAL LETTER O WITH CARON
            0x0001D26F, // LATIN SMALL LETTER O WITH CARON
            0x0001D355, // LATIN CAPITAL LETTER U WITH CARON
            0x0001D475, // LATIN SMALL LETTER U WITH CARON
            0x0001D555, // LATIN CAPITAL LETTER U WITH DIAERESIS AND MACRON
            0x0001D675, // LATIN SMALL LETTER U WITH DIAERESIS AND MACRON
            0x0001D755, // LATIN CAPITAL LETTER U WITH DIAERESIS AND ACUTE
            0x0001D875, // LATIN SMALL LETTER U WITH DIAERESIS AND ACUTE
            0x0001D955, // LATIN CAPITAL LETTER U WITH DIAERESIS AND CARON
            0x0001DA75, // LATIN SMALL LETTER U WITH DIAERESIS AND CARON
            0x0001DB55, // LATIN CAPITAL LETTER U WITH DIAERESIS AND GRAVE
            0x0001DC75, // LATIN SMALL LETTER U WITH DIAERESIS AND GRAVE
            0x0001DE41, // LATIN CAPITAL LETTER A WITH DIAERESIS AND MACRON
            0x0001DF61, // LATIN SMALL LETTER A WITH DIAERESIS AND MACRON
            0x0001E041, // LATIN CAPITAL LETTER A WITH DOT ABOVE AND MACRON
            0x0001E161, // LATIN SMALL LETTER A WITH DOT ABOVE AND MACRON
            0x0001E447, // LATIN CAPITAL LETTER G WITH STROKE
            0x0001E567, // LATIN SMALL LETTER G WITH STROKE
            0x0001E647, // LATIN CAPITAL LETTER G WITH CARON
            0x0001E767, // LATIN SMALL LETTER G WITH CARON
            0x0001E84B, // LATIN CAPITAL LETTER K WITH CARON
            0x0001E96B, // LATIN SMALL LETTER K WITH CARON
            0x0001EA4F, // LATIN CAPITAL LETTER O WITH OGONEK
            0x0001EB6F, // LATIN SMALL LETTER O WITH OGONEK
            0x0001EC4F, // LATIN CAPITAL LETTER O WITH OGONEK AND MACRON
            0x0001ED6F, // LATIN SMALL LETTER O WITH OGONEK AND MACRON
            0x0001F06A, // LATIN SMALL LETTER J WITH CARON
            0x0001F244, // LATIN CAPITAL LETTER D WITH SMALL LETTER Z
            0x0001F447, // LATIN CAPITAL LETTER G WITH ACUTE
            0x0001F567, // LATIN SMALL LETTER G WITH ACUTE
            0x0001F84E, // LATIN CAPITAL LETTER N WITH GRAVE
            0x0001F96E, // LATIN SMALL LETTER N WITH GRAVE
            0x0001FA41, // LATIN CAPITAL LETTER A WITH RING ABOVE AND ACUTE
            0x0001FB61, // LATIN SMALL LETTER A WITH RING ABOVE AND ACUTE
            0x0001FE4F, // LATIN CAPITAL LETTER O WITH STROKE AND ACUTE
            0x0001FF6F, // LATIN SMALL LETTER O WITH STROKE AND ACUTE
            0x00020041, // LATIN CAPITAL LETTER A WITH DOUBLE GRAVE
            0x00020161, // LATIN SMALL LETTER A WITH DOUBLE GRAVE
            0x00020241, // LATIN CAPITAL LETTER A WITH INVERTED BREVE
            0x00020361, // LATIN SMALL LETTER A WITH INVERTED BREVE
            0x00020445, // LATIN CAPITAL LETTER E WITH DOUBLE GRAVE
            0x00020565, // LATIN SMALL LETTER E WITH DOUBLE GRAVE
            0x00020645, // LATIN CAPITAL LETTER E WITH INVERTED BREVE
            0x00020765, // LATIN SMALL LETTER E WITH INVERTED BREVE
            0x00020849, // LATIN CAPITAL LETTER I WITH DOUBLE GRAVE
            0x00020969, // LATIN SMALL LETTER I WITH DOUBLE GRAVE
            0x00020A49, // LATIN CAPITAL LETTER I WITH INVERTED BREVE
            0x00020B69, // LATIN SMALL LETTER I WITH INVERTED BREVE
            0x00020C4F, // LATIN CAPITAL LETTER O WITH DOUBLE GRAVE
            0x00020D6F, // LATIN SMALL LETTER O WITH DOUBLE GRAVE
            0x00020E4F, // LATIN CAPITAL LETTER O WITH INVERTED BREVE
            0x00020F6F, // LATIN SMALL LETTER O WITH INVERTED BREVE
            0x00021052, // LATIN CAPITAL LETTER R WITH DOUBLE GRAVE
            0x00021172, // LATIN SMALL LETTER R WITH DOUBLE GRAVE
            0x00021252, // LATIN CAPITAL LETTER R WITH INVERTED BREVE
            0x00021372, // LATIN SMALL LETTER R WITH INVERTED BREVE
            0x00021455, // LATIN CAPITAL LETTER U WITH DOUBLE GRAVE
            0x00021575, // LATIN SMALL LETTER U WITH DOUBLE GRAVE
            0x00021655, // LATIN CAPITAL LETTER U WITH INVERTED BREVE
            0x00021775, // LATIN SMALL LETTER U WITH INVERTED BREVE
            0x00021853, // LATIN CAPITAL LETTER S WITH COMMA BELOW
            0x00021973, // LATIN SMALL LETTER S WITH COMMA BELOW
            0x00021A54, // LATIN CAPITAL LETTER T WITH COMMA BELOW
            0x00021B74, // LATIN SMALL LETTER T WITH COMMA BELOW
            0x00021E48, // LATIN CAPITAL LETTER H WITH CARON
            0x00021F68, // LATIN SMALL LETTER H WITH CARON
            0x0002204E, // LATIN CAPITAL LETTER N WITH LONG RIGHT LEG
            0x00022164, // LATIN SMALL LETTER D WITH CURL
            0x0002245A, // LATIN CAPITAL LETTER Z WITH HOOK
            0x0002257A, // LATIN SMALL LETTER Z WITH HOOK
            0x00022641, // LATIN CAPITAL LETTER A WITH DOT ABOVE
            0x00022761, // LATIN SMALL LETTER A WITH DOT ABOVE
            0x00022845, // LATIN CAPITAL LETTER E WITH CEDILLA
            0x00022965, // LATIN SMALL LETTER E WITH CEDILLA
            0x00022A4F, // LATIN CAPITAL LETTER O WITH DIAERESIS AND MACRON
            0x00022B6F, // LATIN SMALL LETTER O WITH DIAERESIS AND MACRON
            0x00022C4F, // LATIN CAPITAL LETTER O WITH TILDE AND MACRON
            0x00022D6F, // LATIN SMALL LETTER O WITH TILDE AND MACRON
            0x00022E4F, // LATIN CAPITAL LETTER O WITH DOT ABOVE
            0x00022F6F, // LATIN SMALL LETTER O WITH DOT ABOVE
            0x0002304F, // LATIN CAPITAL LETTER O WITH DOT ABOVE AND MACRON
            0x0002316F, // LATIN SMALL LETTER O WITH DOT ABOVE AND MACRON
            0x00023259, // LATIN CAPITAL LETTER Y WITH MACRON
            0x00023379, // LATIN SMALL LETTER Y WITH MACRON
            0x0002346C, // LATIN SMALL LETTER L WITH CURL
            0x0002356E, // LATIN SMALL LETTER N WITH CURL
            0x00023674, // LATIN SMALL LETTER T WITH CURL
            0x00023A41, // LATIN CAPITAL LETTER A WITH STROKE
            0x00023B43, // LATIN CAPITAL LETTER C WITH STROKE
            0x00023C63, // LATIN SMALL LETTER C WITH STROKE
            0x00023D4C, // LATIN CAPITAL LETTER L WITH BAR
            0x00023E54, // LATIN CAPITAL LETTER T WITH DIAGONAL STROKE
            0x00023F73, // LATIN SMALL LETTER S WITH SWASH TAIL
            0x0002407A, // LATIN SMALL LETTER Z WITH SWASH TAIL
            0x00024342, // LATIN CAPITAL LETTER B WITH STROKE
            0x00024645, // LATIN CAPITAL LETTER E WITH STROKE
            0x00024765, // LATIN SMALL LETTER E WITH STROKE
            0x0002484A, // LATIN CAPITAL LETTER J WITH STROKE
            0x0002496A, // LATIN SMALL LETTER J WITH STROKE
            0x00024B71, // LATIN SMALL LETTER Q WITH HOOK TAIL
            0x00024C52, // LATIN CAPITAL LETTER R WITH STROKE
            0x00024D72, // LATIN SMALL LETTER R WITH STROKE
            0x00024E59, // LATIN CAPITAL LETTER Y WITH STROKE
            0x00024F79, // LATIN SMALL LETTER Y WITH STROKE
            0x00025362, // LATIN SMALL LETTER B WITH HOOK
            0x00025563, // LATIN SMALL LETTER C WITH CURL
            0x00025664, // LATIN SMALL LETTER D WITH TAIL
            0x00025764, // LATIN SMALL LETTER D WITH HOOK
            0x00026067, // LATIN SMALL LETTER G WITH HOOK
            0x00026668, // LATIN SMALL LETTER H WITH HOOK
            0x00026869, // LATIN SMALL LETTER I WITH STROKE
            0x00026B6C, // LATIN SMALL LETTER L WITH MIDDLE TILDE
            0x00026C6C, // LATIN SMALL LETTER L WITH BELT
            0x00026D6C, // LATIN SMALL LETTER L WITH RETROFLEX HOOK
            0x0002716D, // LATIN SMALL LETTER M WITH HOOK
            0x0002726E, // LATIN SMALL LETTER N WITH LEFT HOOK
            0x0002736E, // LATIN SMALL LETTER N WITH RETROFLEX HOOK
            0x00027C72, // LATIN SMALL LETTER R WITH LONG LEG
            0x00027D72, // LATIN SMALL LETTER R WITH TAIL
            0x00027E72, // LATIN SMALL LETTER R WITH FISHHOOK
            0x00028273, // LATIN SMALL LETTER S WITH HOOK
            0x00028874, // LATIN SMALL LETTER T WITH RETROFLEX HOOK
            0x00028B76, // LATIN SMALL LETTER V WITH HOOK
            0x0002907A, // LATIN SMALL LETTER Z WITH RETROFLEX HOOK
            0x0002917A, // LATIN SMALL LETTER Z WITH CURL
            0x00029D6A, // LATIN SMALL LETTER J WITH CROSSED-TAIL
            0x0002A071, // LATIN SMALL LETTER Q WITH HOOK
            0x0002C688, // MODIFIER LETTER CIRCUMFLEX ACCENT**
            0x0002DC98, // SMALL TILDE**
            0x00036361, // COMBINING LATIN SMALL LETTER A
            0x00036465, // COMBINING LATIN SMALL LETTER E
            0x00036569, // COMBINING LATIN SMALL LETTER I
            0x0003666F, // COMBINING LATIN SMALL LETTER O
            0x00036775, // COMBINING LATIN SMALL LETTER U
            0x00036863, // COMBINING LATIN SMALL LETTER C
            0x00036964, // COMBINING LATIN SMALL LETTER D
            0x00036A68, // COMBINING LATIN SMALL LETTER H
            0x00036B6D, // COMBINING LATIN SMALL LETTER M
            0x00036C72, // COMBINING LATIN SMALL LETTER R
            0x00036D74, // COMBINING LATIN SMALL LETTER T
            0x00036E76, // COMBINING LATIN SMALL LETTER V
            0x00036F78, // COMBINING LATIN SMALL LETTER X
            0x001D6C62, // LATIN SMALL LETTER B WITH MIDDLE TILDE
            0x001D6D64, // LATIN SMALL LETTER D WITH MIDDLE TILDE
            0x001D6E66, // LATIN SMALL LETTER F WITH MIDDLE TILDE
            0x001D6F6D, // LATIN SMALL LETTER M WITH MIDDLE TILDE
            0x001D706E, // LATIN SMALL LETTER N WITH MIDDLE TILDE
            0x001D7170, // LATIN SMALL LETTER P WITH MIDDLE TILDE
            0x001D7272, // LATIN SMALL LETTER R WITH MIDDLE TILDE
            0x001D7372, // LATIN SMALL LETTER R WITH FISHHOOK AND MIDDLE TILDE
            0x001D7473, // LATIN SMALL LETTER S WITH MIDDLE TILDE
            0x001D7574, // LATIN SMALL LETTER T WITH MIDDLE TILDE
            0x001D767A, // LATIN SMALL LETTER Z WITH MIDDLE TILDE
            0x001D7D70, // LATIN SMALL LETTER P WITH STROKE
            0x001D8062, // LATIN SMALL LETTER B WITH PALATAL HOOK
            0x001D8164, // LATIN SMALL LETTER D WITH PALATAL HOOK
            0x001D8266, // LATIN SMALL LETTER F WITH PALATAL HOOK
            0x001D8367, // LATIN SMALL LETTER G WITH PALATAL HOOK
            0x001D846B, // LATIN SMALL LETTER K WITH PALATAL HOOK
            0x001D856C, // LATIN SMALL LETTER L WITH PALATAL HOOK
            0x001D866D, // LATIN SMALL LETTER M WITH PALATAL HOOK
            0x001D876E, // LATIN SMALL LETTER N WITH PALATAL HOOK
            0x001D8870, // LATIN SMALL LETTER P WITH PALATAL HOOK
            0x001D8972, // LATIN SMALL LETTER R WITH PALATAL HOOK
            0x001D8A73, // LATIN SMALL LETTER S WITH PALATAL HOOK
            0x001D8C76, // LATIN SMALL LETTER V WITH PALATAL HOOK
            0x001D8D78, // LATIN SMALL LETTER X WITH PALATAL HOOK
            0x001D8E7A, // LATIN SMALL LETTER Z WITH PALATAL HOOK
            0x001D8F61, // LATIN SMALL LETTER A WITH RETROFLEX HOOK
            0x001D9164, // LATIN SMALL LETTER D WITH HOOK AND TAIL
            0x001D9265, // LATIN SMALL LETTER E WITH RETROFLEX HOOK
            0x001D9669, // LATIN SMALL LETTER I WITH RETROFLEX HOOK
            0x001D9975, // LATIN SMALL LETTER U WITH RETROFLEX HOOK
            0x001DDA67, // COMBINING LATIN SMALL LETTER G
            0x001DDC6B, // COMBINING LATIN SMALL LETTER K
            0x001DDD6C, // COMBINING LATIN SMALL LETTER L
            0x001DE06E, // COMBINING LATIN SMALL LETTER N
            0x001DE473, // COMBINING LATIN SMALL LETTER S
            0x001DE67A, // COMBINING LATIN SMALL LETTER Z
            0x001DE862, // COMBINING LATIN SMALL LETTER B
            0x001DEB66, // COMBINING LATIN SMALL LETTER F
            0x001DEC6C, // COMBINING LATIN SMALL LETTER L WITH DOUBLE MIDDLE TILDE
            0x001DED6F, // COMBINING LATIN SMALL LETTER O WITH LIGHT CENTRALIZATION STROKE
            0x001DEE70, // COMBINING LATIN SMALL LETTER P
            0x001DF075, // COMBINING LATIN SMALL LETTER U WITH LIGHT CENTRALIZATION STROKE
            0x001DF177, // COMBINING LATIN SMALL LETTER W
            0x001DF261, // COMBINING LATIN SMALL LETTER A WITH DIAERESIS
            0x001DF36F, // COMBINING LATIN SMALL LETTER O WITH DIAERESIS
            0x001DF475, // COMBINING LATIN SMALL LETTER U WITH DIAERESIS
            0x001E0041, // LATIN CAPITAL LETTER A WITH RING BELOW
            0x001E0161, // LATIN SMALL LETTER A WITH RING BELOW
            0x001E0242, // LATIN CAPITAL LETTER B WITH DOT ABOVE
            0x001E0362, // LATIN SMALL LETTER B WITH DOT ABOVE
            0x001E0442, // LATIN CAPITAL LETTER B WITH DOT BELOW
            0x001E0562, // LATIN SMALL LETTER B WITH DOT BELOW
            0x001E0642, // LATIN CAPITAL LETTER B WITH LINE BELOW
            0x001E0762, // LATIN SMALL LETTER B WITH LINE BELOW
            0x001E0843, // LATIN CAPITAL LETTER C WITH CEDILLA AND ACUTE
            0x001E0963, // LATIN SMALL LETTER C WITH CEDILLA AND ACUTE
            0x001E0A44, // LATIN CAPITAL LETTER D WITH DOT ABOVE
            0x001E0B64, // LATIN SMALL LETTER D WITH DOT ABOVE
            0x001E0C44, // LATIN CAPITAL LETTER D WITH DOT BELOW
            0x001E0D64, // LATIN SMALL LETTER D WITH DOT BELOW
            0x001E0E44, // LATIN CAPITAL LETTER D WITH LINE BELOW
            0x001E0F64, // LATIN SMALL LETTER D WITH LINE BELOW
            0x001E1044, // LATIN CAPITAL LETTER D WITH CEDILLA
            0x001E1164, // LATIN SMALL LETTER D WITH CEDILLA
            0x001E1244, // LATIN CAPITAL LETTER D WITH CIRCUMFLEX BELOW
            0x001E1364, // LATIN SMALL LETTER D WITH CIRCUMFLEX BELOW
            0x001E1445, // LATIN CAPITAL LETTER E WITH MACRON AND GRAVE
            0x001E1565, // LATIN SMALL LETTER E WITH MACRON AND GRAVE
            0x001E1645, // LATIN CAPITAL LETTER E WITH MACRON AND ACUTE
            0x001E1765, // LATIN SMALL LETTER E WITH MACRON AND ACUTE
            0x001E1845, // LATIN CAPITAL LETTER E WITH CIRCUMFLEX BELOW
            0x001E1965, // LATIN SMALL LETTER E WITH CIRCUMFLEX BELOW
            0x001E1A45, // LATIN CAPITAL LETTER E WITH TILDE BELOW
            0x001E1B65, // LATIN SMALL LETTER E WITH TILDE BELOW
            0x001E1C45, // LATIN CAPITAL LETTER E WITH CEDILLA AND BREVE
            0x001E1D65, // LATIN SMALL LETTER E WITH CEDILLA AND BREVE
            0x001E1E46, // LATIN CAPITAL LETTER F WITH DOT ABOVE
            0x001E1F66, // LATIN SMALL LETTER F WITH DOT ABOVE
            0x001E2047, // LATIN CAPITAL LETTER G WITH MACRON
            0x001E2167, // LATIN SMALL LETTER G WITH MACRON
            0x001E2248, // LATIN CAPITAL LETTER H WITH DOT ABOVE
            0x001E2368, // LATIN SMALL LETTER H WITH DOT ABOVE
            0x001E2448, // LATIN CAPITAL LETTER H WITH DOT BELOW
            0x001E2568, // LATIN SMALL LETTER H WITH DOT BELOW
            0x001E2648, // LATIN CAPITAL LETTER H WITH DIAERESIS
            0x001E2768, // LATIN SMALL LETTER H WITH DIAERESIS
            0x001E2848, // LATIN CAPITAL LETTER H WITH CEDILLA
            0x001E2968, // LATIN SMALL LETTER H WITH CEDILLA
            0x001E2A48, // LATIN CAPITAL LETTER H WITH BREVE BELOW
            0x001E2B68, // LATIN SMALL LETTER H WITH BREVE BELOW
            0x001E2C49, // LATIN CAPITAL LETTER I WITH TILDE BELOW
            0x001E2D69, // LATIN SMALL LETTER I WITH TILDE BELOW
            0x001E2E49, // LATIN CAPITAL LETTER I WITH DIAERESIS AND ACUTE
            0x001E2F69, // LATIN SMALL LETTER I WITH DIAERESIS AND ACUTE
            0x001E304B, // LATIN CAPITAL LETTER K WITH ACUTE
            0x001E316B, // LATIN SMALL LETTER K WITH ACUTE
            0x001E324B, // LATIN CAPITAL LETTER K WITH DOT BELOW
            0x001E336B, // LATIN SMALL LETTER K WITH DOT BELOW
            0x001E344B, // LATIN CAPITAL LETTER K WITH LINE BELOW
            0x001E356B, // LATIN SMALL LETTER K WITH LINE BELOW
            0x001E364C, // LATIN CAPITAL LETTER L WITH DOT BELOW
            0x001E376C, // LATIN SMALL LETTER L WITH DOT BELOW
            0x001E384C, // LATIN CAPITAL LETTER L WITH DOT BELOW AND MACRON
            0x001E396C, // LATIN SMALL LETTER L WITH DOT BELOW AND MACRON
            0x001E3A4C, // LATIN CAPITAL LETTER L WITH LINE BELOW
            0x001E3B6C, // LATIN SMALL LETTER L WITH LINE BELOW
            0x001E3C4C, // LATIN CAPITAL LETTER L WITH CIRCUMFLEX BELOW
            0x001E3D6C, // LATIN SMALL LETTER L WITH CIRCUMFLEX BELOW
            0x001E3E4D, // LATIN CAPITAL LETTER M WITH ACUTE
            0x001E3F6D, // LATIN SMALL LETTER M WITH ACUTE
            0x001E404D, // LATIN CAPITAL LETTER M WITH DOT ABOVE
            0x001E416D, // LATIN SMALL LETTER M WITH DOT ABOVE
            0x001E424D, // LATIN CAPITAL LETTER M WITH DOT BELOW
            0x001E436D, // LATIN SMALL LETTER M WITH DOT BELOW
            0x001E444E, // LATIN CAPITAL LETTER N WITH DOT ABOVE
            0x001E456E, // LATIN SMALL LETTER N WITH DOT ABOVE
            0x001E464E, // LATIN CAPITAL LETTER N WITH DOT BELOW
            0x001E476E, // LATIN SMALL LETTER N WITH DOT BELOW
            0x001E484E, // LATIN CAPITAL LETTER N WITH LINE BELOW
            0x001E496E, // LATIN SMALL LETTER N WITH LINE BELOW
            0x001E4A4E, // LATIN CAPITAL LETTER N WITH CIRCUMFLEX BELOW
            0x001E4B6E, // LATIN SMALL LETTER N WITH CIRCUMFLEX BELOW
            0x001E4C4F, // LATIN CAPITAL LETTER O WITH TILDE AND ACUTE
            0x001E4D6F, // LATIN SMALL LETTER O WITH TILDE AND ACUTE
            0x001E4E4F, // LATIN CAPITAL LETTER O WITH TILDE AND DIAERESIS
            0x001E4F6F, // LATIN SMALL LETTER O WITH TILDE AND DIAERESIS
            0x001E504F, // LATIN CAPITAL LETTER O WITH MACRON AND GRAVE
            0x001E516F, // LATIN SMALL LETTER O WITH MACRON AND GRAVE
            0x001E524F, // LATIN CAPITAL LETTER O WITH MACRON AND ACUTE
            0x001E536F, // LATIN SMALL LETTER O WITH MACRON AND ACUTE
            0x001E5450, // LATIN CAPITAL LETTER P WITH ACUTE
            0x001E5570, // LATIN SMALL LETTER P WITH ACUTE
            0x001E5650, // LATIN CAPITAL LETTER P WITH DOT ABOVE
            0x001E5770, // LATIN SMALL LETTER P WITH DOT ABOVE
            0x001E5852, // LATIN CAPITAL LETTER R WITH DOT ABOVE
            0x001E5972, // LATIN SMALL LETTER R WITH DOT ABOVE
            0x001E5A52, // LATIN CAPITAL LETTER R WITH DOT BELOW
            0x001E5B72, // LATIN SMALL LETTER R WITH DOT BELOW
            0x001E5C52, // LATIN CAPITAL LETTER R WITH DOT BELOW AND MACRON
            0x001E5D72, // LATIN SMALL LETTER R WITH DOT BELOW AND MACRON
            0x001E5E52, // LATIN CAPITAL LETTER R WITH LINE BELOW
            0x001E5F72, // LATIN SMALL LETTER R WITH LINE BELOW
            0x001E6053, // LATIN CAPITAL LETTER S WITH DOT ABOVE
            0x001E6173, // LATIN SMALL LETTER S WITH DOT ABOVE
            0x001E6253, // LATIN CAPITAL LETTER S WITH DOT BELOW
            0x001E6373, // LATIN SMALL LETTER S WITH DOT BELOW
            0x001E6453, // LATIN CAPITAL LETTER S WITH ACUTE AND DOT ABOVE
            0x001E6573, // LATIN SMALL LETTER S WITH ACUTE AND DOT ABOVE
            0x001E6653, // LATIN CAPITAL LETTER S WITH CARON AND DOT ABOVE
            0x001E6773, // LATIN SMALL LETTER S WITH CARON AND DOT ABOVE
            0x001E6853, // LATIN CAPITAL LETTER S WITH DOT BELOW AND DOT ABOVE
            0x001E6973, // LATIN SMALL LETTER S WITH DOT BELOW AND DOT ABOVE
            0x001E6A54, // LATIN CAPITAL LETTER T WITH DOT ABOVE
            0x001E6B74, // LATIN SMALL LETTER T WITH DOT ABOVE
            0x001E6C54, // LATIN CAPITAL LETTER T WITH DOT BELOW
            0x001E6D74, // LATIN SMALL LETTER T WITH DOT BELOW
            0x001E6E54, // LATIN CAPITAL LETTER T WITH LINE BELOW
            0x001E6F74, // LATIN SMALL LETTER T WITH LINE BELOW
            0x001E7054, // LATIN CAPITAL LETTER T WITH CIRCUMFLEX BELOW
            0x001E7174, // LATIN SMALL LETTER T WITH CIRCUMFLEX BELOW
            0x001E7255, // LATIN CAPITAL LETTER U WITH DIAERESIS BELOW
            0x001E7375, // LATIN SMALL LETTER U WITH DIAERESIS BELOW
            0x001E7455, // LATIN CAPITAL LETTER U WITH TILDE BELOW
            0x001E7575, // LATIN SMALL LETTER U WITH TILDE BELOW
            0x001E7655, // LATIN CAPITAL LETTER U WITH CIRCUMFLEX BELOW
            0x001E7775, // LATIN SMALL LETTER U WITH CIRCUMFLEX BELOW
            0x001E7855, // LATIN CAPITAL LETTER U WITH TILDE AND ACUTE
            0x001E7975, // LATIN SMALL LETTER U WITH TILDE AND ACUTE
            0x001E7A55, // LATIN CAPITAL LETTER U WITH MACRON AND DIAERESIS
            0x001E7B75, // LATIN SMALL LETTER U WITH MACRON AND DIAERESIS
            0x001E7C56, // LATIN CAPITAL LETTER V WITH TILDE
            0x001E7D76, // LATIN SMALL LETTER V WITH TILDE
            0x001E7E56, // LATIN CAPITAL LETTER V WITH DOT BELOW
            0x001E7F76, // LATIN SMALL LETTER V WITH DOT BELOW
            0x001E8057, // LATIN CAPITAL LETTER W WITH GRAVE
            0x001E8177, // LATIN SMALL LETTER W WITH GRAVE
            0x001E8257, // LATIN CAPITAL LETTER W WITH ACUTE
            0x001E8377, // LATIN SMALL LETTER W WITH ACUTE
            0x001E8457, // LATIN CAPITAL LETTER W WITH DIAERESIS
            0x001E8577, // LATIN SMALL LETTER W WITH DIAERESIS
            0x001E8657, // LATIN CAPITAL LETTER W WITH DOT ABOVE
            0x001E8777, // LATIN SMALL LETTER W WITH DOT ABOVE
            0x001E8857, // LATIN CAPITAL LETTER W WITH DOT BELOW
            0x001E8977, // LATIN SMALL LETTER W WITH DOT BELOW
            0x001E8A58, // LATIN CAPITAL LETTER X WITH DOT ABOVE
            0x001E8B78, // LATIN SMALL LETTER X WITH DOT ABOVE
            0x001E8C58, // LATIN CAPITAL LETTER X WITH DIAERESIS
            0x001E8D78, // LATIN SMALL LETTER X WITH DIAERESIS
            0x001E8E59, // LATIN CAPITAL LETTER Y WITH DOT ABOVE
            0x001E8F79, // LATIN SMALL LETTER Y WITH DOT ABOVE
            0x001E905A, // LATIN CAPITAL LETTER Z WITH CIRCUMFLEX
            0x001E917A, // LATIN SMALL LETTER Z WITH CIRCUMFLEX
            0x001E925A, // LATIN CAPITAL LETTER Z WITH DOT BELOW
            0x001E937A, // LATIN SMALL LETTER Z WITH DOT BELOW
            0x001E945A, // LATIN CAPITAL LETTER Z WITH LINE BELOW
            0x001E957A, // LATIN SMALL LETTER Z WITH LINE BELOW
            0x001E9668, // LATIN SMALL LETTER H WITH LINE BELOW
            0x001E9774, // LATIN SMALL LETTER T WITH DIAERESIS
            0x001E9877, // LATIN SMALL LETTER W WITH RING ABOVE
            0x001E9979, // LATIN SMALL LETTER Y WITH RING ABOVE
            0x001E9A61, // LATIN SMALL LETTER A WITH RIGHT HALF RING
            0x001EA041, // LATIN CAPITAL LETTER A WITH DOT BELOW
            0x001EA161, // LATIN SMALL LETTER A WITH DOT BELOW
            0x001EA241, // LATIN CAPITAL LETTER A WITH HOOK ABOVE
            0x001EA361, // LATIN SMALL LETTER A WITH HOOK ABOVE
            0x001EA441, // LATIN CAPITAL LETTER A WITH CIRCUMFLEX AND ACUTE
            0x001EA561, // LATIN SMALL LETTER A WITH CIRCUMFLEX AND ACUTE
            0x001EA641, // LATIN CAPITAL LETTER A WITH CIRCUMFLEX AND GRAVE
            0x001EA761, // LATIN SMALL LETTER A WITH CIRCUMFLEX AND GRAVE
            0x001EA841, // LATIN CAPITAL LETTER A WITH CIRCUMFLEX AND HOOK ABOVE
            0x001EA961, // LATIN SMALL LETTER A WITH CIRCUMFLEX AND HOOK ABOVE
            0x001EAA41, // LATIN CAPITAL LETTER A WITH CIRCUMFLEX AND TILDE
            0x001EAB61, // LATIN SMALL LETTER A WITH CIRCUMFLEX AND TILDE
            0x001EAC41, // LATIN CAPITAL LETTER A WITH CIRCUMFLEX AND DOT BELOW
            0x001EAD61, // LATIN SMALL LETTER A WITH CIRCUMFLEX AND DOT BELOW
            0x001EAE41, // LATIN CAPITAL LETTER A WITH BREVE AND ACUTE
            0x001EAF61, // LATIN SMALL LETTER A WITH BREVE AND ACUTE
            0x001EB041, // LATIN CAPITAL LETTER A WITH BREVE AND GRAVE
            0x001EB161, // LATIN SMALL LETTER A WITH BREVE AND GRAVE
            0x001EB241, // LATIN CAPITAL LETTER A WITH BREVE AND HOOK ABOVE
            0x001EB361, // LATIN SMALL LETTER A WITH BREVE AND HOOK ABOVE
            0x001EB441, // LATIN CAPITAL LETTER A WITH BREVE AND TILDE
            0x001EB561, // LATIN SMALL LETTER A WITH BREVE AND TILDE
            0x001EB641, // LATIN CAPITAL LETTER A WITH BREVE AND DOT BELOW
            0x001EB761, // LATIN SMALL LETTER A WITH BREVE AND DOT BELOW
            0x001EB845, // LATIN CAPITAL LETTER E WITH DOT BELOW
            0x001EB965, // LATIN SMALL LETTER E WITH DOT BELOW
            0x001EBA45, // LATIN CAPITAL LETTER E WITH HOOK ABOVE
            0x001EBB65, // LATIN SMALL LETTER E WITH HOOK ABOVE
            0x001EBC45, // LATIN CAPITAL LETTER E WITH TILDE
            0x001EBD65, // LATIN SMALL LETTER E WITH TILDE
            0x001EBE45, // LATIN CAPITAL LETTER E WITH CIRCUMFLEX AND ACUTE
            0x001EBF65, // LATIN SMALL LETTER E WITH CIRCUMFLEX AND ACUTE
            0x001EC045, // LATIN CAPITAL LETTER E WITH CIRCUMFLEX AND GRAVE
            0x001EC165, // LATIN SMALL LETTER E WITH CIRCUMFLEX AND GRAVE
            0x001EC245, // LATIN CAPITAL LETTER E WITH CIRCUMFLEX AND HOOK ABOVE
            0x001EC365, // LATIN SMALL LETTER E WITH CIRCUMFLEX AND HOOK ABOVE
            0x001EC445, // LATIN CAPITAL LETTER E WITH CIRCUMFLEX AND TILDE
            0x001EC565, // LATIN SMALL LETTER E WITH CIRCUMFLEX AND TILDE
            0x001EC645, // LATIN CAPITAL LETTER E WITH CIRCUMFLEX AND DOT BELOW
            0x001EC765, // LATIN SMALL LETTER E WITH CIRCUMFLEX AND DOT BELOW
            0x001EC849, // LATIN CAPITAL LETTER I WITH HOOK ABOVE
            0x001EC969, // LATIN SMALL LETTER I WITH HOOK ABOVE
            0x001ECA49, // LATIN CAPITAL LETTER I WITH DOT BELOW
            0x001ECB69, // LATIN SMALL LETTER I WITH DOT BELOW
            0x001ECC4F, // LATIN CAPITAL LETTER O WITH DOT BELOW
            0x001ECD6F, // LATIN SMALL LETTER O WITH DOT BELOW
            0x001ECE4F, // LATIN CAPITAL LETTER O WITH HOOK ABOVE
            0x001ECF6F, // LATIN SMALL LETTER O WITH HOOK ABOVE
            0x001ED04F, // LATIN CAPITAL LETTER O WITH CIRCUMFLEX AND ACUTE
            0x001ED16F, // LATIN SMALL LETTER O WITH CIRCUMFLEX AND ACUTE
            0x001ED24F, // LATIN CAPITAL LETTER O WITH CIRCUMFLEX AND GRAVE
            0x001ED36F, // LATIN SMALL LETTER O WITH CIRCUMFLEX AND GRAVE
            0x001ED44F, // LATIN CAPITAL LETTER O WITH CIRCUMFLEX AND HOOK ABOVE
            0x001ED56F, // LATIN SMALL LETTER O WITH CIRCUMFLEX AND HOOK ABOVE
            0x001ED64F, // LATIN CAPITAL LETTER O WITH CIRCUMFLEX AND TILDE
            0x001ED76F, // LATIN SMALL LETTER O WITH CIRCUMFLEX AND TILDE
            0x001ED84F, // LATIN CAPITAL LETTER O WITH CIRCUMFLEX AND DOT BELOW
            0x001ED96F, // LATIN SMALL LETTER O WITH CIRCUMFLEX AND DOT BELOW
            0x001EDA4F, // LATIN CAPITAL LETTER O WITH HORN AND ACUTE
            0x001EDB6F, // LATIN SMALL LETTER O WITH HORN AND ACUTE
            0x001EDC4F, // LATIN CAPITAL LETTER O WITH HORN AND GRAVE
            0x001EDD6F, // LATIN SMALL LETTER O WITH HORN AND GRAVE
            0x001EDE4F, // LATIN CAPITAL LETTER O WITH HORN AND HOOK ABOVE
            0x001EDF6F, // LATIN SMALL LETTER O WITH HORN AND HOOK ABOVE
            0x001EE04F, // LATIN CAPITAL LETTER O WITH HORN AND TILDE
            0x001EE16F, // LATIN SMALL LETTER O WITH HORN AND TILDE
            0x001EE24F, // LATIN CAPITAL LETTER O WITH HORN AND DOT BELOW
            0x001EE36F, // LATIN SMALL LETTER O WITH HORN AND DOT BELOW
            0x001EE455, // LATIN CAPITAL LETTER U WITH DOT BELOW
            0x001EE575, // LATIN SMALL LETTER U WITH DOT BELOW
            0x001EE655, // LATIN CAPITAL LETTER U WITH HOOK ABOVE
            0x001EE775, // LATIN SMALL LETTER U WITH HOOK ABOVE
            0x001EE855, // LATIN CAPITAL LETTER U WITH HORN AND ACUTE
            0x001EE975, // LATIN SMALL LETTER U WITH HORN AND ACUTE
            0x001EEA55, // LATIN CAPITAL LETTER U WITH HORN AND GRAVE
            0x001EEB75, // LATIN SMALL LETTER U WITH HORN AND GRAVE
            0x001EEC55, // LATIN CAPITAL LETTER U WITH HORN AND HOOK ABOVE
            0x001EED75, // LATIN SMALL LETTER U WITH HORN AND HOOK ABOVE
            0x001EEE55, // LATIN CAPITAL LETTER U WITH HORN AND TILDE
            0x001EEF75, // LATIN SMALL LETTER U WITH HORN AND TILDE
            0x001EF055, // LATIN CAPITAL LETTER U WITH HORN AND DOT BELOW
            0x001EF175, // LATIN SMALL LETTER U WITH HORN AND DOT BELOW
            0x001EF259, // LATIN CAPITAL LETTER Y WITH GRAVE
            0x001EF379, // LATIN SMALL LETTER Y WITH GRAVE
            0x001EF459, // LATIN CAPITAL LETTER Y WITH DOT BELOW
            0x001EF579, // LATIN SMALL LETTER Y WITH DOT BELOW
            0x001EF659, // LATIN CAPITAL LETTER Y WITH HOOK ABOVE
            0x001EF779, // LATIN SMALL LETTER Y WITH HOOK ABOVE
            0x001EF859, // LATIN CAPITAL LETTER Y WITH TILDE
            0x001EF979, // LATIN SMALL LETTER Y WITH TILDE
            0x001EFE59, // LATIN CAPITAL LETTER Y WITH LOOP
            0x001EFF79, // LATIN SMALL LETTER Y WITH LOOP
            0x0020122D, // FIGURE DASH††
            0x00201396, // EN DASH**
            0x00201497, // EM DASH**
            0x0020152D, // HORIZONTAL BAR††
            0x00201891, // LEFT SINGLE QUOTATION MARK**
            0x00201992, // RIGHT SINGLE QUOTATION MARK**
            0x00201A82, // SINGLE LOW-9 QUOTATION MARK**
            0x00201C93, // LEFT DOUBLE QUOTATION MARK**
            0x00201D94, // RIGHT DOUBLE QUOTATION MARK**
            0x00201E84, // DOUBLE LOW-9 QUOTATION MARK**
            0x00202086, // DAGGER**
            0x00202187, // DOUBLE DAGGER**
            0x00202295, // BULLET**
            0x00202685, // HORIZONTAL ELLIPSIS**
            0x00203089, // PER MILLE SIGN**
            0x0020398B, // SINGLE LEFT-POINTING ANGLE QUOTATION MARK**
            0x00203A9B, // SINGLE RIGHT-POINTING ANGLE QUOTATION MARK**
            0x0020537E, // SWUNG DASH††
            0x00207169, // SUPERSCRIPT LATIN SMALL LETTER I
            0x00207F6E, // SUPERSCRIPT LATIN SMALL LETTER N
            0x0020AC80, // EURO SIGN**
            0x00212299, // TRADE MARK SIGN**
            0x00249C61, // PARENTHESIZED LATIN SMALL LETTER A
            0x00249D62, // PARENTHESIZED LATIN SMALL LETTER B
            0x00249E63, // PARENTHESIZED LATIN SMALL LETTER C
            0x00249F64, // PARENTHESIZED LATIN SMALL LETTER D
            0x0024A065, // PARENTHESIZED LATIN SMALL LETTER E
            0x0024A166, // PARENTHESIZED LATIN SMALL LETTER F
            0x0024A267, // PARENTHESIZED LATIN SMALL LETTER G
            0x0024A368, // PARENTHESIZED LATIN SMALL LETTER H
            0x0024A469, // PARENTHESIZED LATIN SMALL LETTER I
            0x0024A56A, // PARENTHESIZED LATIN SMALL LETTER J
            0x0024A66B, // PARENTHESIZED LATIN SMALL LETTER K
            0x0024A76C, // PARENTHESIZED LATIN SMALL LETTER L
            0x0024A86D, // PARENTHESIZED LATIN SMALL LETTER M
            0x0024A96E, // PARENTHESIZED LATIN SMALL LETTER N
            0x0024AA6F, // PARENTHESIZED LATIN SMALL LETTER O
            0x0024AB70, // PARENTHESIZED LATIN SMALL LETTER P
            0x0024AC71, // PARENTHESIZED LATIN SMALL LETTER Q
            0x0024AD72, // PARENTHESIZED LATIN SMALL LETTER R
            0x0024AE73, // PARENTHESIZED LATIN SMALL LETTER S
            0x0024AF74, // PARENTHESIZED LATIN SMALL LETTER T
            0x0024B075, // PARENTHESIZED LATIN SMALL LETTER U
            0x0024B176, // PARENTHESIZED LATIN SMALL LETTER V
            0x0024B277, // PARENTHESIZED LATIN SMALL LETTER W
            0x0024B378, // PARENTHESIZED LATIN SMALL LETTER X
            0x0024B479, // PARENTHESIZED LATIN SMALL LETTER Y
            0x0024B57A, // PARENTHESIZED LATIN SMALL LETTER Z
            0x0024B641, // CIRCLED LATIN CAPITAL LETTER A
            0x0024B742, // CIRCLED LATIN CAPITAL LETTER B
            0x0024B843, // CIRCLED LATIN CAPITAL LETTER C
            0x0024B944, // CIRCLED LATIN CAPITAL LETTER D
            0x0024BA45, // CIRCLED LATIN CAPITAL LETTER E
            0x0024BB46, // CIRCLED LATIN CAPITAL LETTER F
            0x0024BC47, // CIRCLED LATIN CAPITAL LETTER G
            0x0024BD48, // CIRCLED LATIN CAPITAL LETTER H
            0x0024BE49, // CIRCLED LATIN CAPITAL LETTER I
            0x0024BF4A, // CIRCLED LATIN CAPITAL LETTER J
            0x0024C04B, // CIRCLED LATIN CAPITAL LETTER K
            0x0024C14C, // CIRCLED LATIN CAPITAL LETTER L
            0x0024C24D, // CIRCLED LATIN CAPITAL LETTER M
            0x0024C34E, // CIRCLED LATIN CAPITAL LETTER N
            0x0024C44F, // CIRCLED LATIN CAPITAL LETTER O
            0x0024C550, // CIRCLED LATIN CAPITAL LETTER P
            0x0024C651, // CIRCLED LATIN CAPITAL LETTER Q
            0x0024C752, // CIRCLED LATIN CAPITAL LETTER R
            0x0024C853, // CIRCLED LATIN CAPITAL LETTER S
            0x0024C954, // CIRCLED LATIN CAPITAL LETTER T
            0x0024CA55, // CIRCLED LATIN CAPITAL LETTER U
            0x0024CB56, // CIRCLED LATIN CAPITAL LETTER V
            0x0024CC57, // CIRCLED LATIN CAPITAL LETTER W
            0x0024CD58, // CIRCLED LATIN CAPITAL LETTER X
            0x0024CE59, // CIRCLED LATIN CAPITAL LETTER Y
            0x0024CF5A, // CIRCLED LATIN CAPITAL LETTER Z
            0x0024D061, // CIRCLED LATIN SMALL LETTER A
            0x0024D162, // CIRCLED LATIN SMALL LETTER B
            0x0024D263, // CIRCLED LATIN SMALL LETTER C
            0x0024D364, // CIRCLED LATIN SMALL LETTER D
            0x0024D465, // CIRCLED LATIN SMALL LETTER E
            0x0024D566, // CIRCLED LATIN SMALL LETTER F
            0x0024D667, // CIRCLED LATIN SMALL LETTER G
            0x0024D768, // CIRCLED LATIN SMALL LETTER H
            0x0024D869, // CIRCLED LATIN SMALL LETTER I
            0x0024D96A, // CIRCLED LATIN SMALL LETTER J
            0x0024DA6B, // CIRCLED LATIN SMALL LETTER K
            0x0024DB6C, // CIRCLED LATIN SMALL LETTER L
            0x0024DC6D, // CIRCLED LATIN SMALL LETTER M
            0x0024DD6E, // CIRCLED LATIN SMALL LETTER N
            0x0024DE6F, // CIRCLED LATIN SMALL LETTER O
            0x0024DF70, // CIRCLED LATIN SMALL LETTER P
            0x0024E071, // CIRCLED LATIN SMALL LETTER Q
            0x0024E172, // CIRCLED LATIN SMALL LETTER R
            0x0024E273, // CIRCLED LATIN SMALL LETTER S
            0x0024E374, // CIRCLED LATIN SMALL LETTER T
            0x0024E475, // CIRCLED LATIN SMALL LETTER U
            0x0024E576, // CIRCLED LATIN SMALL LETTER V
            0x0024E677, // CIRCLED LATIN SMALL LETTER W
            0x0024E778, // CIRCLED LATIN SMALL LETTER X
            0x0024E879, // CIRCLED LATIN SMALL LETTER Y
            0x0024E97A, // CIRCLED LATIN SMALL LETTER Z
            0x002C604C, // LATIN CAPITAL LETTER L WITH DOUBLE BAR
            0x002C616C, // LATIN SMALL LETTER L WITH DOUBLE BAR
            0x002C624C, // LATIN CAPITAL LETTER L WITH MIDDLE TILDE
            0x002C6350, // LATIN CAPITAL LETTER P WITH STROKE
            0x002C6452, // LATIN CAPITAL LETTER R WITH TAIL
            0x002C6561, // LATIN SMALL LETTER A WITH STROKE
            0x002C6674, // LATIN SMALL LETTER T WITH DIAGONAL STROKE
            0x002C6748, // LATIN CAPITAL LETTER H WITH DESCENDER
            0x002C6868, // LATIN SMALL LETTER H WITH DESCENDER
            0x002C694B, // LATIN CAPITAL LETTER K WITH DESCENDER
            0x002C6A6B, // LATIN SMALL LETTER K WITH DESCENDER
            0x002C6B5A, // LATIN CAPITAL LETTER Z WITH DESCENDER
            0x002C6C7A, // LATIN SMALL LETTER Z WITH DESCENDER
            0x002C6E4D, // LATIN CAPITAL LETTER M WITH HOOK
            0x002C7176, // LATIN SMALL LETTER V WITH RIGHT HOOK
            0x002C7257, // LATIN CAPITAL LETTER W WITH HOOK
            0x002C7377, // LATIN SMALL LETTER W WITH HOOK
            0x002C7476, // LATIN SMALL LETTER V WITH CURL
            0x002C7865, // LATIN SMALL LETTER E WITH NOTCH
            0x002C7A6F, // LATIN SMALL LETTER O WITH LOW RING INSIDE
            0x002C7E53, // LATIN CAPITAL LETTER S WITH SWASH TAIL
            0x002C7F5A, // LATIN CAPITAL LETTER Z WITH SWASH TAIL
            0x00A7404B, // LATIN CAPITAL LETTER K WITH STROKE
            0x00A7416B, // LATIN SMALL LETTER K WITH STROKE
            0x00A7424B, // LATIN CAPITAL LETTER K WITH DIAGONAL STROKE
            0x00A7436B, // LATIN SMALL LETTER K WITH DIAGONAL STROKE
            0x00A7444B, // LATIN CAPITAL LETTER K WITH STROKE AND DIAGONAL STROKE
            0x00A7456B, // LATIN SMALL LETTER K WITH STROKE AND DIAGONAL STROKE
            0x00A7484C, // LATIN CAPITAL LETTER L WITH HIGH STROKE
            0x00A7496C, // LATIN SMALL LETTER L WITH HIGH STROKE
            0x00A74A4F, // LATIN CAPITAL LETTER O WITH LONG STROKE OVERLAY
            0x00A74B6F, // LATIN SMALL LETTER O WITH LONG STROKE OVERLAY
            0x00A74C4F, // LATIN CAPITAL LETTER O WITH LOOP
            0x00A74D6F, // LATIN SMALL LETTER O WITH LOOP
            0x00A75050, // LATIN CAPITAL LETTER P WITH STROKE THROUGH DESCENDER
            0x00A75170, // LATIN SMALL LETTER P WITH STROKE THROUGH DESCENDER
            0x00A75250, // LATIN CAPITAL LETTER P WITH FLOURISH
            0x00A75370, // LATIN SMALL LETTER P WITH FLOURISH
            0x00A75450, // LATIN CAPITAL LETTER P WITH SQUIRREL TAIL
            0x00A75570, // LATIN SMALL LETTER P WITH SQUIRREL TAIL
            0x00A75651, // LATIN CAPITAL LETTER Q WITH STROKE THROUGH DESCENDER
            0x00A75771, // LATIN SMALL LETTER Q WITH STROKE THROUGH DESCENDER
            0x00A75851, // LATIN CAPITAL LETTER Q WITH DIAGONAL STROKE
            0x00A75971, // LATIN SMALL LETTER Q WITH DIAGONAL STROKE
            0x00A75E56, // LATIN CAPITAL LETTER V WITH DIAGONAL STROKE
            0x00A75F76, // LATIN SMALL LETTER V WITH DIAGONAL STROKE
            0x00A78E6C, // LATIN SMALL LETTER L WITH RETROFLEX HOOK AND BELT
            0x00A7904E, // LATIN CAPITAL LETTER N WITH DESCENDER
            0x00A7916E, // LATIN SMALL LETTER N WITH DESCENDER
            0x00A79243, // LATIN CAPITAL LETTER C WITH BAR
            0x00A79363, // LATIN SMALL LETTER C WITH BAR
            0x00A79463, // LATIN SMALL LETTER C WITH PALATAL HOOK
            0x00A79568, // LATIN SMALL LETTER H WITH PALATAL HOOK
            0x00A79642, // LATIN CAPITAL LETTER B WITH FLOURISH
            0x00A79762, // LATIN SMALL LETTER B WITH FLOURISH
            0x00A79846, // LATIN CAPITAL LETTER F WITH STROKE
            0x00A79966, // LATIN SMALL LETTER F WITH STROKE
            0x00A7A047, // LATIN CAPITAL LETTER G WITH OBLIQUE STROKE
            0x00A7A167, // LATIN SMALL LETTER G WITH OBLIQUE STROKE
            0x00A7A24B, // LATIN CAPITAL LETTER K WITH OBLIQUE STROKE
            0x00A7A36B, // LATIN SMALL LETTER K WITH OBLIQUE STROKE
            0x00A7A44E, // LATIN CAPITAL LETTER N WITH OBLIQUE STROKE
            0x00A7A56E, // LATIN SMALL LETTER N WITH OBLIQUE STROKE
            0x00A7A652, // LATIN CAPITAL LETTER R WITH OBLIQUE STROKE
            0x00A7A772, // LATIN SMALL LETTER R WITH OBLIQUE STROKE
            0x00A7A853, // LATIN CAPITAL LETTER S WITH OBLIQUE STROKE
            0x00A7A973, // LATIN SMALL LETTER S WITH OBLIQUE STROKE
            0x00A7AA48, // LATIN CAPITAL LETTER H WITH HOOK
            0x00A7AD4C, // LATIN CAPITAL LETTER L WITH BELT
            0x00A7B24A, // LATIN CAPITAL LETTER J WITH CROSSED-TAIL
            0x00AB3465, // LATIN SMALL LETTER E WITH FLOURISH
            0x00AB376C, // LATIN SMALL LETTER L WITH INVERTED LAZY S
            0x00AB386C, // LATIN SMALL LETTER L WITH DOUBLE MIDDLE TILDE
            0x00AB396C, // LATIN SMALL LETTER L WITH MIDDLE RING
            0x00AB3A6D, // LATIN SMALL LETTER M WITH CROSSED-TAIL
            0x00AB3B6E, // LATIN SMALL LETTER N WITH CROSSED-TAIL
            0x00AB4772, // LATIN SMALL LETTER R WITHOUT HANDLE
            0x00AB4972, // LATIN SMALL LETTER R WITH CROSSED-TAIL
            0x00AB4E75, // LATIN SMALL LETTER U WITH SHORT RIGHT LEG
            0x00AB5275, // LATIN SMALL LETTER U WITH LEFT HOOK
            0x00AB5678, // LATIN SMALL LETTER X WITH LOW RIGHT RING
            0x00AB5778, // LATIN SMALL LETTER X WITH LONG LEFT LEG
            0x00AB5878, // LATIN SMALL LETTER X WITH LONG LEFT LEG AND LOW RIGHT RING
            0x00AB5978, // LATIN SMALL LETTER X WITH LONG LEFT LEG WITH SERIF
            0x00AB5A79, // LATIN SMALL LETTER Y WITH SHORT RIGHT LEG
            0x00FF2141, // FULLWIDTH LATIN CAPITAL LETTER A
            0x00FF2242, // FULLWIDTH LATIN CAPITAL LETTER B
            0x00FF2343, // FULLWIDTH LATIN CAPITAL LETTER C
            0x00FF2444, // FULLWIDTH LATIN CAPITAL LETTER D
            0x00FF2545, // FULLWIDTH LATIN CAPITAL LETTER E
            0x00FF2646, // FULLWIDTH LATIN CAPITAL LETTER F
            0x00FF2747, // FULLWIDTH LATIN CAPITAL LETTER G
            0x00FF2848, // FULLWIDTH LATIN CAPITAL LETTER H
            0x00FF2949, // FULLWIDTH LATIN CAPITAL LETTER I
            0x00FF2A4A, // FULLWIDTH LATIN CAPITAL LETTER J
            0x00FF2B4B, // FULLWIDTH LATIN CAPITAL LETTER K
            0x00FF2C4C, // FULLWIDTH LATIN CAPITAL LETTER L
            0x00FF2D4D, // FULLWIDTH LATIN CAPITAL LETTER M
            0x00FF2E4E, // FULLWIDTH LATIN CAPITAL LETTER N
            0x00FF2F4F, // FULLWIDTH LATIN CAPITAL LETTER O
            0x00FF3050, // FULLWIDTH LATIN CAPITAL LETTER P
            0x00FF3151, // FULLWIDTH LATIN CAPITAL LETTER Q
            0x00FF3252, // FULLWIDTH LATIN CAPITAL LETTER R
            0x00FF3353, // FULLWIDTH LATIN CAPITAL LETTER S
            0x00FF3454, // FULLWIDTH LATIN CAPITAL LETTER T
            0x00FF3555, // FULLWIDTH LATIN CAPITAL LETTER U
            0x00FF3656, // FULLWIDTH LATIN CAPITAL LETTER V
            0x00FF3757, // FULLWIDTH LATIN CAPITAL LETTER W
            0x00FF3858, // FULLWIDTH LATIN CAPITAL LETTER X
            0x00FF3959, // FULLWIDTH LATIN CAPITAL LETTER Y
            0x00FF3A5A, // FULLWIDTH LATIN CAPITAL LETTER Z
            0x00FF4161, // FULLWIDTH LATIN SMALL LETTER A
            0x00FF4262, // FULLWIDTH LATIN SMALL LETTER B
            0x00FF4363, // FULLWIDTH LATIN SMALL LETTER C
            0x00FF4464, // FULLWIDTH LATIN SMALL LETTER D
            0x00FF4565, // FULLWIDTH LATIN SMALL LETTER E
            0x00FF4666, // FULLWIDTH LATIN SMALL LETTER F
            0x00FF4767, // FULLWIDTH LATIN SMALL LETTER G
            0x00FF4868, // FULLWIDTH LATIN SMALL LETTER H
            0x00FF4969, // FULLWIDTH LATIN SMALL LETTER I
            0x00FF4A6A, // FULLWIDTH LATIN SMALL LETTER J
            0x00FF4B6B, // FULLWIDTH LATIN SMALL LETTER K
            0x00FF4C6C, // FULLWIDTH LATIN SMALL LETTER L
            0x00FF4D6D, // FULLWIDTH LATIN SMALL LETTER M
            0x00FF4E6E, // FULLWIDTH LATIN SMALL LETTER N
            0x00FF4F6F, // FULLWIDTH LATIN SMALL LETTER O
            0x00FF5070, // FULLWIDTH LATIN SMALL LETTER P
            0x00FF5171, // FULLWIDTH LATIN SMALL LETTER Q
            0x00FF5272, // FULLWIDTH LATIN SMALL LETTER R
            0x00FF5373, // FULLWIDTH LATIN SMALL LETTER S
            0x00FF5474, // FULLWIDTH LATIN SMALL LETTER T
            0x00FF5575, // FULLWIDTH LATIN SMALL LETTER U
            0x00FF5676, // FULLWIDTH LATIN SMALL LETTER V
            0x00FF5777, // FULLWIDTH LATIN SMALL LETTER W
            0x00FF5878, // FULLWIDTH LATIN SMALL LETTER X
            0x00FF5979, // FULLWIDTH LATIN SMALL LETTER Y
            0x00FF5A7A, // FULLWIDTH LATIN SMALL LETTER Z
            0x01F11041, // PARENTHESIZED LATIN CAPITAL LETTER A
            0x01F11142, // PARENTHESIZED LATIN CAPITAL LETTER B
            0x01F11243, // PARENTHESIZED LATIN CAPITAL LETTER C
            0x01F11344, // PARENTHESIZED LATIN CAPITAL LETTER D
            0x01F11445, // PARENTHESIZED LATIN CAPITAL LETTER E
            0x01F11546, // PARENTHESIZED LATIN CAPITAL LETTER F
            0x01F11647, // PARENTHESIZED LATIN CAPITAL LETTER G
            0x01F11748, // PARENTHESIZED LATIN CAPITAL LETTER H
            0x01F11849, // PARENTHESIZED LATIN CAPITAL LETTER I
            0x01F1194A, // PARENTHESIZED LATIN CAPITAL LETTER J
            0x01F11A4B, // PARENTHESIZED LATIN CAPITAL LETTER K
            0x01F11B4C, // PARENTHESIZED LATIN CAPITAL LETTER L
            0x01F11C4D, // PARENTHESIZED LATIN CAPITAL LETTER M
            0x01F11D4E, // PARENTHESIZED LATIN CAPITAL LETTER N
            0x01F11E4F, // PARENTHESIZED LATIN CAPITAL LETTER O
            0x01F11F50, // PARENTHESIZED LATIN CAPITAL LETTER P
            0x01F12051, // PARENTHESIZED LATIN CAPITAL LETTER Q
            0x01F12152, // PARENTHESIZED LATIN CAPITAL LETTER R
            0x01F12253, // PARENTHESIZED LATIN CAPITAL LETTER S
            0x01F12354, // PARENTHESIZED LATIN CAPITAL LETTER T
            0x01F12455, // PARENTHESIZED LATIN CAPITAL LETTER U
            0x01F12556, // PARENTHESIZED LATIN CAPITAL LETTER V
            0x01F12657, // PARENTHESIZED LATIN CAPITAL LETTER W
            0x01F12758, // PARENTHESIZED LATIN CAPITAL LETTER X
            0x01F12859, // PARENTHESIZED LATIN CAPITAL LETTER Y
            0x01F1295A, // PARENTHESIZED LATIN CAPITAL LETTER Z
            0x01F12A53, // TORTOISE SHELL BRACKETED LATIN CAPITAL LETTER S
            0x01F12B43, // CIRCLED ITALIC LATIN CAPITAL LETTER C
            0x01F12C52, // CIRCLED ITALIC LATIN CAPITAL LETTER R
            0x01F13041, // SQUARED LATIN CAPITAL LETTER A
            0x01F13142, // SQUARED LATIN CAPITAL LETTER B
            0x01F13243, // SQUARED LATIN CAPITAL LETTER C
            0x01F13344, // SQUARED LATIN CAPITAL LETTER D
            0x01F13445, // SQUARED LATIN CAPITAL LETTER E
            0x01F13546, // SQUARED LATIN CAPITAL LETTER F
            0x01F13647, // SQUARED LATIN CAPITAL LETTER G
            0x01F13748, // SQUARED LATIN CAPITAL LETTER H
            0x01F13849, // SQUARED LATIN CAPITAL LETTER I
            0x01F1394A, // SQUARED LATIN CAPITAL LETTER J
            0x01F13A4B, // SQUARED LATIN CAPITAL LETTER K
            0x01F13B4C, // SQUARED LATIN CAPITAL LETTER L
            0x01F13C4D, // SQUARED LATIN CAPITAL LETTER M
            0x01F13D4E, // SQUARED LATIN CAPITAL LETTER N
            0x01F13E4F, // SQUARED LATIN CAPITAL LETTER O
            0x01F13F50, // SQUARED LATIN CAPITAL LETTER P
            0x01F14051, // SQUARED LATIN CAPITAL LETTER Q
            0x01F14152, // SQUARED LATIN CAPITAL LETTER R
            0x01F14253, // SQUARED LATIN CAPITAL LETTER S
            0x01F14354, // SQUARED LATIN CAPITAL LETTER T
            0x01F14455, // SQUARED LATIN CAPITAL LETTER U
            0x01F14556, // SQUARED LATIN CAPITAL LETTER V
            0x01F14657, // SQUARED LATIN CAPITAL LETTER W
            0x01F14758, // SQUARED LATIN CAPITAL LETTER X
            0x01F14859, // SQUARED LATIN CAPITAL LETTER Y
            0x01F1495A, // SQUARED LATIN CAPITAL LETTER Z
            0x01F15041, // NEGATIVE CIRCLED LATIN CAPITAL LETTER A
            0x01F15142, // NEGATIVE CIRCLED LATIN CAPITAL LETTER B
            0x01F15243, // NEGATIVE CIRCLED LATIN CAPITAL LETTER C
            0x01F15344, // NEGATIVE CIRCLED LATIN CAPITAL LETTER D
            0x01F15445, // NEGATIVE CIRCLED LATIN CAPITAL LETTER E
            0x01F15546, // NEGATIVE CIRCLED LATIN CAPITAL LETTER F
            0x01F15647, // NEGATIVE CIRCLED LATIN CAPITAL LETTER G
            0x01F15748, // NEGATIVE CIRCLED LATIN CAPITAL LETTER H
            0x01F15849, // NEGATIVE CIRCLED LATIN CAPITAL LETTER I
            0x01F1594A, // NEGATIVE CIRCLED LATIN CAPITAL LETTER J
            0x01F15A4B, // NEGATIVE CIRCLED LATIN CAPITAL LETTER K
            0x01F15B4C, // NEGATIVE CIRCLED LATIN CAPITAL LETTER L
            0x01F15C4D, // NEGATIVE CIRCLED LATIN CAPITAL LETTER M
            0x01F15D4E, // NEGATIVE CIRCLED LATIN CAPITAL LETTER N
            0x01F15E4F, // NEGATIVE CIRCLED LATIN CAPITAL LETTER O
            0x01F15F50, // NEGATIVE CIRCLED LATIN CAPITAL LETTER P
            0x01F16051, // NEGATIVE CIRCLED LATIN CAPITAL LETTER Q
            0x01F16152, // NEGATIVE CIRCLED LATIN CAPITAL LETTER R
            0x01F16253, // NEGATIVE CIRCLED LATIN CAPITAL LETTER S
            0x01F16354, // NEGATIVE CIRCLED LATIN CAPITAL LETTER T
            0x01F16455, // NEGATIVE CIRCLED LATIN CAPITAL LETTER U
            0x01F16556, // NEGATIVE CIRCLED LATIN CAPITAL LETTER V
            0x01F16657, // NEGATIVE CIRCLED LATIN CAPITAL LETTER W
            0x01F16758, // NEGATIVE CIRCLED LATIN CAPITAL LETTER X
            0x01F16859, // NEGATIVE CIRCLED LATIN CAPITAL LETTER Y
            0x01F1695A, // NEGATIVE CIRCLED LATIN CAPITAL LETTER Z
            0x01F17041, // NEGATIVE SQUARED LATIN CAPITAL LETTER A
            0x01F17142, // NEGATIVE SQUARED LATIN CAPITAL LETTER B
            0x01F17243, // NEGATIVE SQUARED LATIN CAPITAL LETTER C
            0x01F17344, // NEGATIVE SQUARED LATIN CAPITAL LETTER D
            0x01F17445, // NEGATIVE SQUARED LATIN CAPITAL LETTER E
            0x01F17546, // NEGATIVE SQUARED LATIN CAPITAL LETTER F
            0x01F17647, // NEGATIVE SQUARED LATIN CAPITAL LETTER G
            0x01F17748, // NEGATIVE SQUARED LATIN CAPITAL LETTER H
            0x01F17849, // NEGATIVE SQUARED LATIN CAPITAL LETTER I
            0x01F1794A, // NEGATIVE SQUARED LATIN CAPITAL LETTER J
            0x01F17A4B, // NEGATIVE SQUARED LATIN CAPITAL LETTER K
            0x01F17B4C, // NEGATIVE SQUARED LATIN CAPITAL LETTER L
            0x01F17C4D, // NEGATIVE SQUARED LATIN CAPITAL LETTER M
            0x01F17D4E, // NEGATIVE SQUARED LATIN CAPITAL LETTER N
            0x01F17E4F, // NEGATIVE SQUARED LATIN CAPITAL LETTER O
            0x01F17F50, // NEGATIVE SQUARED LATIN CAPITAL LETTER P
            0x01F18051, // NEGATIVE SQUARED LATIN CAPITAL LETTER Q
            0x01F18152, // NEGATIVE SQUARED LATIN CAPITAL LETTER R
            0x01F18253, // NEGATIVE SQUARED LATIN CAPITAL LETTER S
            0x01F18354, // NEGATIVE SQUARED LATIN CAPITAL LETTER T
            0x01F18455, // NEGATIVE SQUARED LATIN CAPITAL LETTER U
            0x01F18556, // NEGATIVE SQUARED LATIN CAPITAL LETTER V
            0x01F18657, // NEGATIVE SQUARED LATIN CAPITAL LETTER W
            0x01F18758, // NEGATIVE SQUARED LATIN CAPITAL LETTER X
            0x01F18859, // NEGATIVE SQUARED LATIN CAPITAL LETTER Y
            0x01F1895A, // NEGATIVE SQUARED LATIN CAPITAL LETTER Z
            0x01F18A50, // CROSSED NEGATIVE SQUARED LATIN CAPITAL LETTER P
            0x0E004141, // TAG LATIN CAPITAL LETTER A
            0x0E004242, // TAG LATIN CAPITAL LETTER B
            0x0E004343, // TAG LATIN CAPITAL LETTER C
            0x0E004444, // TAG LATIN CAPITAL LETTER D
            0x0E004545, // TAG LATIN CAPITAL LETTER E
            0x0E004646, // TAG LATIN CAPITAL LETTER F
            0x0E004747, // TAG LATIN CAPITAL LETTER G
            0x0E004848, // TAG LATIN CAPITAL LETTER H
            0x0E004949, // TAG LATIN CAPITAL LETTER I
            0x0E004A4A, // TAG LATIN CAPITAL LETTER J
            0x0E004B4B, // TAG LATIN CAPITAL LETTER K
            0x0E004C4C, // TAG LATIN CAPITAL LETTER L
            0x0E004D4D, // TAG LATIN CAPITAL LETTER M
            0x0E004E4E, // TAG LATIN CAPITAL LETTER N
            0x0E004F4F, // TAG LATIN CAPITAL LETTER O
            0x0E005050, // TAG LATIN CAPITAL LETTER P
            0x0E005151, // TAG LATIN CAPITAL LETTER Q
            0x0E005252, // TAG LATIN CAPITAL LETTER R
            0x0E005353, // TAG LATIN CAPITAL LETTER S
            0x0E005454, // TAG LATIN CAPITAL LETTER T
            0x0E005555, // TAG LATIN CAPITAL LETTER U
            0x0E005656, // TAG LATIN CAPITAL LETTER V
            0x0E005757, // TAG LATIN CAPITAL LETTER W
            0x0E005858, // TAG LATIN CAPITAL LETTER X
            0x0E005959, // TAG LATIN CAPITAL LETTER Y
            0x0E005A5A, // TAG LATIN CAPITAL LETTER Z
            0x0E006161, // TAG LATIN SMALL LETTER A
            0x0E006262, // TAG LATIN SMALL LETTER B
            0x0E006363, // TAG LATIN SMALL LETTER C
            0x0E006464, // TAG LATIN SMALL LETTER D
            0x0E006565, // TAG LATIN SMALL LETTER E
            0x0E006666, // TAG LATIN SMALL LETTER F
            0x0E006767, // TAG LATIN SMALL LETTER G
            0x0E006868, // TAG LATIN SMALL LETTER H
            0x0E006969, // TAG LATIN SMALL LETTER I
            0x0E006A6A, // TAG LATIN SMALL LETTER J
            0x0E006B6B, // TAG LATIN SMALL LETTER K
            0x0E006C6C, // TAG LATIN SMALL LETTER L
            0x0E006D6D, // TAG LATIN SMALL LETTER M
            0x0E006E6E, // TAG LATIN SMALL LETTER N
            0x0E006F6F, // TAG LATIN SMALL LETTER O
            0x0E007070, // TAG LATIN SMALL LETTER P
            0x0E007171, // TAG LATIN SMALL LETTER Q
            0x0E007272, // TAG LATIN SMALL LETTER R
            0x0E007373, // TAG LATIN SMALL LETTER S
            0x0E007474, // TAG LATIN SMALL LETTER T
            0x0E007575, // TAG LATIN SMALL LETTER U
            0x0E007676, // TAG LATIN SMALL LETTER V
            0x0E007777, // TAG LATIN SMALL LETTER W
            0x0E007878, // TAG LATIN SMALL LETTER X
            0x0E007979, // TAG LATIN SMALL LETTER Y
            0x0E007A7A  // TAG LATIN SMALL LETTER Z
        };
    }
}

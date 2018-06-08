using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using NongFormat;

namespace UnitTest
{
    [TestClass]
    public class TestExtensions
    {
        [TestMethod]
        public void Test_ToBitString2A()
        {
            int val = unchecked ((int) 0xFFFF1E96);

            string result10 = ConvertTo.ToBitString (val, 10);
            string result16 = ConvertTo.ToBitString (val, 16);
            string result32 = ConvertTo.ToBitString (val, 32);

            Assert.AreEqual ("10 1001 0110", result10);
            Assert.AreEqual ("0001 1110 1001 0110", result16);
            Assert.AreEqual ("1111 1111 1111 1111 0001 1110 1001 0110", result32);
        }


        [TestMethod]
        public void Test_ToBitString2B()
        {
            var buf = new byte[] { 0x12, 0x34, 0x5E, 0x78 };
            var result = ConvertTo.ToBitString (buf, 3);
            Assert.AreEqual ("0001 0010 0011 0100 0101 1110", result);
        }


        [TestMethod]
        public void Test_ToHexString1()
        {
            var buf = new byte[] { 0x12, 0xA4, 0xCD, 0x45 };
            var result = ConvertTo.ToHexString (buf);

            Assert.AreEqual ("12A4CD45", result);
        }


        [TestMethod]
        public void Test_Big16ToInt()
        {
            var buf = new byte[] { 0x12, 0xA4, 0xCD, 0x45 };
            var result = ConvertTo.FromBig16ToInt32 (buf, 1);

            Assert.AreEqual (0xA4CD, result);
        }


        [TestMethod]
        public void Test_Big24ToInt()
        {
            var buf = new byte[] { 0x12, 0xA4, 0xCD, 0x45 };
            var result = ConvertTo.FromBig24ToInt32 (buf, 1);


            Assert.AreEqual (result.GetType(), typeof (Int32));
            Assert.AreEqual (0xA4CD45, result);
        }


        [TestMethod]
        public void Test_Big32ToInt()
        {
            var buf = new byte[] { 0x12, 0xFF, 0xFF, 0xFF, 0xFE, 0xED };
            var result = ConvertTo.FromBig32ToInt32 (buf, 1);

            Assert.AreEqual (result.GetType(), typeof (Int32));
            Assert.AreEqual (-2, result);
        }


        [TestMethod]
        public void Test_Big32ToUInt()
        {
            var buf = new byte[] { 0x12, 0xFF, 0xA4, 0xCD, 0x45, 0xED };
            var result = ConvertTo.FromBig32ToUInt32 (buf, 1);

            Assert.AreEqual (result.GetType(), typeof (UInt32));
            Assert.AreEqual (0xFFA4CD45, result);
        }


        [TestMethod]
        public void Test_Lit16ToInt()
        {
            var buf = new byte[] { 0x12, 0xA4, 0xCD, 0x45 };
            var result = ConvertTo.FromLit16ToInt32 (buf, 1);

            Assert.AreEqual (0xCDA4, result);
        }


        [TestMethod]
        public void Test_Lit32ToInt()
        {
            var buf = new byte[] { 0x12, 0xFD, 0xFF, 0xFF, 0xFF, 0xED };
            var result = ConvertTo.FromLit32ToInt32 (buf, 1);

            Assert.AreEqual (result.GetType(), typeof (Int32));
            Assert.AreEqual (-3, result);
        }


        [TestMethod]
        public void Test_AsciizToString()
        {
            var buf = new byte[] { 0x30, 0x31, 0x42, 0x43, 0x44, 0, 0x46, 0x47 };
            var result = ConvertTo.FromAsciizToString (buf, 2);

            Assert.AreEqual (result.GetType(), typeof (string));
            Assert.AreEqual ("BCD", result);
        }


        [TestMethod]
        public void Test_Wobbly1()
        {
            byte[] buf;

            var s1 = new MemoryStream (new byte[] { 0x61 });
            var r1 = s1.ReadWobbly (out buf);
            Assert.AreEqual (0x61, r1);
            Assert.AreEqual (1, buf.Length);
            Assert.AreEqual (0x61, buf[0]);

            r1 = new MemoryStream (new byte[] { 0xFF } ).ReadWobbly (out buf);
            Assert.IsTrue (r1 < 0);
            Assert.AreEqual (1, buf.Length);
            Assert.AreEqual (0xFF, buf[0]);

            r1 = new MemoryStream (new byte[] { 0xC3, 0xBF } ).ReadWobbly (out buf);
            Assert.AreEqual (0xFF, r1);
            Assert.AreEqual (2, buf.Length);
            Assert.AreEqual (0xC3, buf[0]);
            Assert.AreEqual (0xBF, buf[1]);

            r1 = new MemoryStream (new byte[] { 0xDF, 0xBF } ).ReadWobbly (out buf);
            Assert.AreEqual (0x7FF, r1);
            Assert.AreEqual (2, buf.Length);
            Assert.AreEqual (0xDF, buf[0]);
            Assert.AreEqual (0xBF, buf[1]);

            var buf3 = new byte[] { 0xEF, 0xBF, 0xBF };
            var sFFFF = new MemoryStream (buf3).ReadWobbly (out buf);
            Assert.AreEqual (0xFFFF, sFFFF);
            Assert.AreEqual (3, buf.Length);
            Assert.IsTrue (buf3.SequenceEqual (buf));

            var buf4bad = new byte[] { 0xF7, 0xBF, 0x00, 0xBF };
            var bad4 = new MemoryStream (buf4bad).ReadWobbly (out buf);
            Assert.IsTrue (bad4 < 0);
            Assert.AreEqual (4, buf.Length);
            Assert.IsTrue (buf4bad.SequenceEqual (buf));

            var buf7 = new byte[] { 0xFE, 0xBF, 0xBF, 0xBF, 0xBF, 0xBF, 0xBF };
            var maxResult = new MemoryStream (buf7).ReadWobbly (out buf);
            Assert.AreEqual (0xFFFFFFFFF, maxResult);
            Assert.AreEqual (7, buf.Length);
            Assert.IsTrue (buf7.SequenceEqual (buf));
        }


        [TestMethod]
        public void Test_Map1252Order()
        {
            Assert.AreNotEqual (0, Map1252.Length);

            int prev = 0;
            for (var ix = 0; ix < Map1252.Length; ++ix)
            {
                int key = Map1252.At (ix) >> 8;
                byte value = unchecked ((byte) Map1252.At (ix));

                // Table must contain ordered, distinct keys.
                Assert.IsTrue (key > prev);

                // Not against the spec, just not expected.
                Assert.IsTrue (value != 0);

                prev = key;
            }
        }


        [TestMethod]
        public void Test_Map1252Search()
        {
            for (var ix = 0; ix < Map1252.Length; ++ix)
            {
                var expectedResult = Map1252.At (ix) & 0xFF;
                var result = Map1252.To1252Bestfit (Map1252.At (ix) >> 8);

                Assert.IsTrue (expectedResult > 0);
                Assert.IsTrue (result == expectedResult);
            }

            var minvalResult = Map1252.To1252Bestfit (0);
            var maxvalResult = Map1252.To1252Bestfit (0x10FFFF);

            Assert.IsTrue (minvalResult == 0);

            // U+10FFFF not expected and not actually against the spec.
            Assert.IsTrue (maxvalResult < 0);
        }


        [TestMethod]
        public void Test_ToClean1252FileName1()
        {
            var aZ = Map1252.ToClean1252FileName ("aZ");
            var c1ControlSpots = Map1252.ToClean1252FileName ("€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸ");
            var quote = Map1252.ToClean1252FileName ("\"");
            var heart = Map1252.ToClean1252FileName ("❤");
            var question = Map1252.ToClean1252FileName ("?");
            var strokedH = Map1252.ToClean1252FileName ("Ħ");
            var backslash = Map1252.ToClean1252FileName ("\\");
            var spacedSlash = Map1252.ToClean1252FileName ("foo / bar.mp3");
            var notFileChars = Map1252.ToClean1252FileName ("<>");
            var fullwidth = Map1252.ToClean1252FileName ("Ａｚfullwidth\U0001F175astral");

            Assert.AreEqual ("aZ", aZ);
            Assert.AreEqual ("€‚ƒ„…†‡ˆ‰Š‹ŒŽ‘’“”•–—˜™š›œžŸ", c1ControlSpots);
            Assert.AreEqual ("'", quote);
            Assert.AreEqual ("-", heart);
            Assert.AreEqual ("", question);
            Assert.AreEqual ("H", strokedH);
            Assert.AreEqual ("-", backslash);
            Assert.AreEqual ("foo; bar.mp3", spacedSlash);
            Assert.AreEqual ("‹›", notFileChars);
            Assert.AreEqual ("AzfullwidthFastral", fullwidth);
        }
    }
}

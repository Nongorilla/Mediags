using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using NongFormat;
using NongIssue;

namespace UnitTest
{
    [TestClass]
    public class TestPng
    {
        [TestMethod]
        public void Test_Png_1 ()
        {
            PngFormat.Model pngModel;
            PngFormat png;

            var fn = @"Targets\Singles\Tile1.png";
            var file = new FileInfo (fn);

            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                pngModel = new PngFormat.Model (fs, hdr, fs.Name);
                Assert.IsNotNull (pngModel);

                pngModel.CalcHashes (Hashes.Intrinsic, Validations.None);
            }

            png = pngModel.Data;

            Assert.AreEqual (Severity.Noise, png.Issues.MaxSeverity);
            Assert.AreEqual (1, png.Issues.Items.Count);

            Assert.AreEqual (19, png.Width);
            Assert.AreEqual (16, png.Height);
            Assert.AreEqual (0, png.BadCrcCount);

            foreach (var chunk in png.Chunks.Items)
            {
                Assert.AreEqual (chunk.StoredCRC, chunk.ActualCRC);
            }
        }
    }
}

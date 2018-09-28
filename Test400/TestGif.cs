using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using NongIssue;
using NongFormat;

namespace UnitTest
{
    [TestClass]
    public class TestGif
    {
        [TestMethod]
        public void Test_Gif_1()
        {
            GifFormat.Model gifModel;

            var fn = @"Targets\Singles\pic300x301.gif";
            var file = new FileInfo (fn);

            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                gifModel = GifFormat.CreateModel (fs, hdr, fs.Name);
                Assert.IsNotNull (gifModel);
            }

            GifFormat gif = gifModel.Data;
            Assert.AreEqual (300, gif.Width);
            Assert.AreEqual (301, gif.Height);

            Assert.IsTrue (gif.Issues.MaxSeverity == Severity.NoIssue);
            Assert.AreEqual (0, gif.Issues.Items.Count);
        }
    }
}

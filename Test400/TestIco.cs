using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using NongIssue;
using NongFormat;
using NongMediaDiags;

namespace UnitTest
{
    [TestClass]
    public class TestIco
    {
        [TestMethod]
        public void Test_Ico_1 ()
        {
            IcoFormat fmt;

            var fn = @"Targets\Singles\Korean.ico";
            var file = new FileInfo (fn);

            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                IcoFormat.Model icoModel = IcoFormat.CreateModel (fs, hdr, fs.Name);
                fmt = icoModel.Bind;

                Assert.IsNull (fmt.FileMD5ToHex);
                Assert.IsNull (fmt.FileSHA1ToHex);
                icoModel.CalcHashes (Hashes.FileMD5 | Hashes.FileSHA1, 0);
                Assert.IsNotNull (fmt.FileMD5ToHex);
                Assert.AreEqual ("aac4e9f76f7e67bc6cad5f8c356fd53cdf70135f", fmt.FileSHA1ToHex.ToLower());
            }

            Assert.IsNotNull (fmt);
            Assert.IsTrue (fmt.Issues.MaxSeverity == Severity.NoIssue);
            Assert.AreEqual (0, fmt.Issues.Items.Count);

            Assert.AreEqual (3, fmt.Count);
            foreach (var item in fmt.Icons)
            {
                Assert.AreEqual (16, item.Width);
                Assert.AreEqual (16, item.Height);
                Assert.IsFalse (item.IsPNG);
            }
        }


        [TestMethod]
        public void Test_Ico_Misnamed ()
        {
            var model = new Diags.Model (null);

            var fn = @"Targets\Singles\DutchIco.jpeg";
            var file = new FileInfo (fn);

            bool isKnown;
            FileFormat actual;
            var fs = new FileStream (fn, FileMode.Open);
            var fmt = FormatBase.CreateModel (model.Data.FileFormats.Items, fs, fn, 0, 0, null, out isKnown, out actual);
            var fb = fmt.BaseBind;

            Assert.IsNotNull (fmt);
            Assert.IsTrue (fb.Issues.MaxSeverity == Severity.Warning);
            Assert.AreEqual (1, fb.Issues.Items.Count);
            Assert.IsInstanceOfType (fmt, typeof (IcoFormat.Model));
            Assert.AreEqual ("ico", actual.PrimaryName);

            Assert.AreEqual (1, fb.Issues.RepairableCount);

            string errMsg = fmt.IssueModel.Repair (0);

            Assert.AreEqual (0, fb.Issues.RepairableCount);
            Assert.IsNull (errMsg);
        }
    }
}

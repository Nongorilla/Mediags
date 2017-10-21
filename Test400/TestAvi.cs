using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using NongIssue;
using NongFormat;
using NongMediaDiags;

namespace UnitTest
{
    [TestClass]
    public class TestAvi
    {
        [TestMethod]
        public void Test_Avi_1()
        {
            var fn = @"Targets\Singles\CrimeBlimp.avi";
            var file = new FileInfo (fn);

            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.ReadWrite))
            {
                var hdr = new byte[0x2C];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                AviFormat.Model aviModel = AviFormat.CreateModel (fs, hdr, fs.Name);
                AviFormat avi = aviModel.Bind;

                Assert.IsNotNull (avi);
                long fileSize = avi.FileSize;

                Assert.AreEqual (Severity.Warning, avi.Issues.MaxSeverity);
                Assert.AreEqual (1, avi.Issues.Items.Count);
                Assert.AreEqual (1, avi.Issues.RepairableCount);
                Assert.IsTrue (avi.Issues.Items[0].IsRepairable);
                Assert.AreEqual (5, avi.ExcessSize);
                Assert.AreEqual (Likeliness.Probable, avi.Watermark);

                string errMess = aviModel.IssueModel.Repair (0);
                Assert.IsNull (errMess);
                Assert.AreEqual (fileSize-5, avi.FileSize);
            }
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using NongFormat;
using NongIssue;

namespace UnitTest
{
    [TestClass]
    public class TestSha256
    {
        [TestMethod]
        public void Test_Sha256_OK3()
        {
            var fn = @"Targets\Hashes\OK03.sha256";
            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var file = new FileInfo (fn);
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                Sha256Format.Model shaModel = Sha256Format.CreateModel (fs, hdr, fs.Name);
                Assert.IsNotNull (shaModel);

                Sha256Format sha = shaModel.Data;

                Assert.IsTrue (sha.Issues.MaxSeverity == Severity.NoIssue);
                Assert.AreEqual (0, sha.Issues.Items.Count);
                Assert.AreEqual (2, sha.HashedFiles.Items.Count);

                shaModel.CalcHashes (0, Validations.SHA256);
                Assert.AreEqual (1, sha.Issues.Items.Count);
                Assert.AreEqual (Severity.Advisory, sha.Issues.MaxSeverity);
            }
        }

        [TestMethod]
        public void Test_Sha256_Empty()
        {
            var fn = @"Targets\Hashes\0bytes.sha256";
            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (0, got);

                Sha256Format.Model shaModel = Sha256Format.CreateModel (fs, hdr, fs.Name);
                Assert.IsNotNull (shaModel);

                Sha256Format sha = shaModel.Data;

                Assert.IsTrue (sha.Issues.MaxSeverity == Severity.NoIssue);
                Assert.AreEqual (0, sha.Issues.Items.Count);

                Assert.AreEqual (0, sha.HashedFiles.Items.Count);

                shaModel.CalcHashes (0, Validations.SHA256);
                Assert.AreEqual (1, sha.Issues.Items.Count);
                Assert.AreEqual (Severity.Advisory, sha.Issues.MaxSeverity);
            }
        }
    }
}

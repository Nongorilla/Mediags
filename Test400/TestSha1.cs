using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.IO;
using NongFormat;
using NongIssue;

namespace UnitTest
{
    [TestClass]
    public class TestSha1
    {
        [TestMethod]
        public void Test_Sha1_1 ()
        {
            var fn = @"Targets\Hashes\OK01.sha1";
            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var file = new FileInfo (fn);
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                Sha1Format.Model shaModel = Sha1Format.CreateModel (fs, hdr, fs.Name);
                Assert.IsNotNull (shaModel);

                Sha1Format sha = shaModel.Bind;

                Assert.IsTrue (sha.Issues.MaxSeverity == Severity.NoIssue);
                Assert.AreEqual (0, sha.Issues.Items.Count);
                Assert.AreEqual (2, sha.HashedFiles.Items.Count);

                shaModel.CalcHashes (0, Validations.SHA1);
                Assert.AreEqual (1, sha.Issues.Items.Count);
                Assert.AreEqual (Severity.Advisory, sha.Issues.MaxSeverity);
            }
        }


        [TestMethod]
        public void Test_Sha1_2 ()
        {
            var fn = @"Targets\Hashes\Bad01.sha1";
            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var file = new FileInfo (fn);
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                Sha1Format.Model shaModel = Sha1Format.CreateModel (fs, hdr, fs.Name);
                Assert.IsNotNull (shaModel);

                Sha1Format sha = shaModel.Bind;

                Assert.IsTrue (sha.Issues.MaxSeverity == Severity.NoIssue);
                Assert.AreEqual (0, sha.Issues.Items.Count);
                Assert.AreEqual (2, sha.HashedFiles.Items.Count);

                shaModel.CalcHashes (0, Validations.SHA1);
                Assert.AreEqual (2, sha.Issues.Items.Count);
                Assert.AreEqual (Severity.Error, sha.Issues.MaxSeverity);
            }
        }
    }
}

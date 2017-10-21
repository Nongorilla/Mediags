using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.IO;
using NongFormat;
using NongIssue;

namespace UnitTest
{
    [TestClass]
    public class TestM3u
    {
        [TestMethod]
        public void Test_M3u_1 ()
        {
            var fn = @"Targets\Hashes\OK02.m3u";
            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var info = new FileInfo (fn);
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                M3uFormat.Model model = M3uFormat.CreateModel (fs, hdr, fs.Name);
                M3uFormat bind = model.Bind;

                Assert.IsNotNull (model);
                Assert.IsTrue (bind.Issues.MaxSeverity == Severity.NoIssue);
                Assert.AreEqual (0, bind.Issues.Items.Count);
                Assert.AreEqual (3, bind.Files.Items.Count);

                foreach (var item in bind.Files.Items)
                    Assert.IsNull (item.IsFound);

                model.CalcHashes (Hashes.None, Validations.Exists);

                Assert.AreEqual (1, bind.Issues.Items.Count);
                Assert.AreEqual (Severity.Advisory, bind.Issues.MaxSeverity);
                foreach (var item in bind.Files.Items)
                    Assert.IsTrue (item.IsFound.Value);
            }
        }

        [TestMethod]
        public void Test_M3u_2 ()
        {
            var fn = @"Targets\Hashes\Bad02.m3u";
            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var info = new FileInfo (fn);
                var hdr = new byte[0x20];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                M3uFormat.Model model = M3uFormat.CreateModel (fs, hdr, fs.Name);
                M3uFormat bind = model.Bind;

                Assert.IsNotNull (model);
                Assert.IsTrue (bind.Issues.MaxSeverity == Severity.NoIssue);
                Assert.AreEqual (0, bind.Issues.Items.Count);
                Assert.AreEqual (3, bind.Files.Items.Count);

                foreach (var item in bind.Files.Items)
                    Assert.IsNull (item.IsFound);

                model.CalcHashes (Hashes.None, Validations.Exists);

                Assert.AreEqual (2, bind.Issues.Items.Count);
                Assert.AreEqual (Severity.Error, bind.Issues.MaxSeverity);
                Assert.IsTrue (bind.Files.Items[0].IsFound.Value);
                Assert.IsFalse (bind.Files.Items[1].IsFound.Value);
                Assert.IsTrue (bind.Files.Items[2].IsFound.Value);
            }
        }
    }
}

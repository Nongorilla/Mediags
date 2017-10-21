using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using NongFormat;
using NongIssue;

namespace UnitTest
{
    [TestClass]
    public class TestFlac
    {
        [TestMethod]
        public void Test_Flac_1 ()
        {
            var fn = @"Targets\Singles\03-Silence.flac";
            var flacInfo = new FileInfo (fn);

            using (var fs = new FileStream (fn, FileMode.Open))
            {
                var hdr = new byte[0x2C];
                fs.Read (hdr, 0, hdr.Length);

                FlacFormat.Model flacModel = FlacFormat.CreateModel (fs, hdr, fn);
                Assert.IsNotNull (flacModel.Bind);

                Assert.IsTrue (flacModel.Bind.Issues.MaxSeverity == Severity.NoIssue);

                var isBadHdr = flacModel.Bind.IsBadHeader;
                Assert.IsFalse (isBadHdr);

                var isBadData = flacModel.Bind.IsBadData;
                Assert.IsFalse (isBadData);
            }
        }


        [TestMethod]
        public void Test_Flac_2 ()
        {
            var fn = @"Targets\Singles\04-BadCRC.flac";
            var flacInfo = new FileInfo (fn);

            using (var fs = new FileStream (fn, FileMode.Open))
            {
                var hdr = new byte[0x2C];
                fs.Read (hdr, 0, hdr.Length);

                FlacFormat.Model flacModel = FlacFormat.CreateModel (fs, hdr, fn);
                Assert.IsNotNull (flacModel);

                flacModel.CalcHashes (Hashes.Intrinsic, Validations.None);
                Assert.IsTrue (flacModel.Bind.Issues.MaxSeverity == Severity.Error);

                var isBadHdr = flacModel.Bind.IsBadHeader;
                Assert.IsFalse (isBadHdr);

                var isBadData = flacModel.Bind.IsBadData;
                Assert.IsTrue (isBadData);
            }
        }
    }
}

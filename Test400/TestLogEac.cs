using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using NongFormat;
using NongIssue;
using NongMediaDiags;

namespace UnitTest
{
    [TestClass]
    public class TestLogEac
    {
        readonly string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        [TestMethod]
        public void Test_LogEac_FlacRip_OK1()
        {
            LogEacFormat.Model logModel;
            LogEacFormat log;

            var fn = @"Targets\Rips\Tester - 2000 - FLAC Silence OK1\Tester - 2000 - FLAC Silence OK1.log";

            using (var fs = new FileStream (fn, FileMode.Open))
            {
                var hdr = new byte[0x28];
                fs.Read (hdr, 0, hdr.Length);
                logModel = LogEacFormat.CreateModel (fs, hdr, fn);
                log = logModel.Bind;
            }

            Assert.AreEqual (3, log.Tracks.Items.Count);

            for (var ix = 0; ix < log.Tracks.Items.Count; ++ix)
            {
                LogEacTrack track = log.Tracks.Items[ix];

                Assert.AreEqual (ix + 1, track.Number);
                Assert.IsTrue (track.HasOK);
                Assert.AreEqual (track.TestCRC, track.CopyCRC);
            }

            var dInfo = new DirectoryInfo (baseDir + @"\Targets\Rips\Tester - 2000 - FLAC Silence OK1");
            var flacInfos = dInfo.GetFiles ("*.flac");

            Assert.AreEqual (3, flacInfos.Length);

            var flacMods = new List<FlacFormat.Model>();
            foreach (var flacInfo in flacInfos)
            {
                var ffs = new FileStream (flacInfo.FullName, FileMode.Open, FileAccess.Read);
                var hdr = new byte[0x28];
                ffs.Read (hdr, 0, hdr.Length);

                FlacFormat.Model flacModel = FlacFormat.CreateModel (ffs, hdr, flacInfo.FullName);
                Assert.IsNotNull (flacModel);

                flacModel.CalcHashes (Hashes.Intrinsic|Hashes.PcmCRC32, Validations.None);
                flacMods.Add (flacModel);
            }

            logModel.MatchFlacs (flacMods);

            for (var ix = 0; ix < log.Tracks.Items.Count; ++ix)
            {
                var track = log.Tracks.Items[ix];
                var flac = track.Match;
                var trackNumTag = flac.GetTag ("TRACKNUMBER");

                Assert.IsNotNull (flac);
                Assert.AreEqual (1, flac.Issues.Items.Count);
                Assert.AreEqual (Severity.Noise, flac.Issues.MaxSeverity);
                Assert.AreEqual ((ix+1).ToString(), trackNumTag);
                Assert.IsFalse (flac.IsBadHeader);
                Assert.IsFalse (flac.IsBadData);
            }

            Assert.AreEqual (0x6522DF69u, log.Tracks.Items[0].Match.ActualPcmCRC32);
            Assert.AreEqual (0x003E740Du, log.Tracks.Items[1].Match.ActualPcmCRC32);
            Assert.AreEqual (0xFAB5205Fu, log.Tracks.Items[2].Match.ActualPcmCRC32);
        }


        [TestMethod]
        public void Test_LogEac_FlacRip_Bad1()
        {
            LogEacFormat.Model logModel;
            LogEacFormat log;

            var fn = @"Targets\Rips\Tester - 2000 - FLAC Silence Bad1\Tester - 2000 - FLAC Silence Bad1.log";

            using (var fs = new FileStream (fn, FileMode.Open))
            {
                var hdr = new byte[0x28];
                fs.Read (hdr, 0, hdr.Length);
                logModel = LogEacFormat.CreateModel (fs, hdr, fn);
                log = logModel.Bind;
            }

            Assert.AreEqual (3, log.Tracks.Items.Count);

            for (var ix = 0; ix < log.Tracks.Items.Count; ++ix)
            {
                LogEacTrack track = log.Tracks.Items[ix];

                Assert.AreEqual (ix + 1, track.Number);
                Assert.IsTrue (track.HasOK);
                Assert.AreEqual (track.TestCRC, track.CopyCRC);
            }

            var dInfo = new DirectoryInfo (baseDir + "\\Targets\\Rips\\Tester - 2000 - FLAC Silence Bad1");
            var flacInfos = dInfo.GetFiles ("*.flac");

            Assert.AreEqual (3, flacInfos.Length);

            var flacMods = new List<FlacFormat.Model>();
            foreach (var flacInfo in flacInfos)
            {
                var ffs = new FileStream (flacInfo.FullName, FileMode.Open, FileAccess.Read);
                var hdr = new byte[0x28];
                ffs.Read (hdr, 0, hdr.Length);

                var flacModel = FlacFormat.CreateModel (ffs, hdr, flacInfo.FullName);
                Assert.IsNotNull (flacModel);

                Assert.IsTrue (flacModel.Bind.Issues.MaxSeverity < Severity.Warning);
                flacModel.CalcHashes (Hashes.Intrinsic|Hashes.PcmCRC32, Validations.None);
                flacMods.Add (flacModel);
            }

            Assert.AreEqual (3, flacMods.Count);
            logModel.MatchFlacs (flacMods);

            Assert.IsTrue (log.Issues.MaxSeverity == Severity.Fatal);
        }


        [TestMethod]
        public void Test_LogEac_FlacRip_NotSuper_1()
        {
            var dn = @"Targets\Rips\Tester - 2000 - FLAC Not Super";

            var model = new FlacDiags.Model (dn, Granularity.Verbose);
            model.Bind.WillProve = true;
            var result = model.ValidateFlacsDeep (null);

            Assert.AreEqual (Severity.Error, result);
        }


        [TestMethod]
        public void Test_LogEac_FlacRip_NotSuper()
        {
            var dn = @"Targets\Rips\Tester - 2000 - FLAC Not Super";
            var fn = dn + @"\Tester - 2000 - FLAC Not Super.log";

            var model = new FlacDiags.Model (dn, Granularity.Verbose);

            Severity status = model.ValidateFlacRip (dn, null, false, false);
            Assert.AreEqual (Severity.Error, status);
            Assert.AreEqual (1, model.RipModel.Bind.Log.Issues.Items.Count (x => x.Level == Severity.Error));
            Assert.AreEqual (4, model.RipModel.Bind.Log.Issues.Items.Count (x => x.Level == Severity.Warning));
        }


        [TestMethod]
        public void Test_LogEac_FlacRip_OK2()
        {
            var ripper = "CHK1";
            var dn = baseDir + @"\Targets\Rips\Tester - 2002 - FLAC Silence OK2";
            var workName = "Tester - 2002 - FLAC Silence OK2";
            var logName = dn + @"\" + workName + ".log";
            var md5Name = dn + @"\" + workName + ".FLAC." + ripper + ".md5";
            var newLogName = workName + ".FLAC." + ripper + ".log";

            var model = new FlacDiags.Model (dn, Granularity.Verbose);
            model.Bind.WillProve = false;
            model.Bind.Product = "UberFLAC";
            model.Bind.ProductVersion = "0.8.3.1";
            Severity status = model.ValidateFlacRip (dn, ripper, false, false);

            Assert.IsNotNull (model.RipModel.Bind.M3u);
            Assert.IsFalse (model.RipModel.Bind.M3u.Issues.HasError);

            Assert.IsNotNull (model.RipModel.Bind.Md5);
            Assert.IsFalse (model.RipModel.Bind.Md5.Issues.HasError);
            Assert.IsTrue (model.RipModel.Bind.Md5.History.LastAction.ToLower().StartsWith ("ripped"));

            using (var fs = new FileStream (md5Name, FileMode.Open))
            {
                var hdr = new byte[0x2C];
                fs.Read (hdr, 0, hdr.Length);
                Md5Format.Model md5Model = Md5Format.CreateModel (fs, hdr, fs.Name);
                md5Model.CalcHashes (Hashes.Intrinsic, Validations.MD5);
                Assert.IsFalse (md5Model.Bind.Issues.HasError);
            }
        }

        [TestMethod]
        public void Test_LogEac_StrictWeb()
        {
            var dn = baseDir + @"\Targets\EacLogs";
            var log1Name = dn + @"\Nightmare.log";

            var model = new Diags.Model (dn);
            model.Bind.ErrEscalator |= IssueTags.ProveErr;

            // Uncomment next line to test hash verification.  Requires the interweb.
            // model.Bind.HashFlags |= Hashes.WebCheck;

            var s1 = new FileStream (log1Name, FileMode.Open);
            var h1 = new byte[0x2C];
            s1.Read (h1, 0, h1.Length);
            var log1Model = LogEacFormat.CreateModel (s1, h1, dn);
            log1Model.CalcHashes (model.Bind.HashFlags, 0);
            log1Model.IssueModel.Escalate (model.Bind.WarnEscalator, model.Bind.ErrEscalator);

            var s2 = new FileStream (dn+"\\EAC1NoHashOrCT.log", FileMode.Open);
            var h2 = new byte[0x2C];
            s2.Read (h2, 0, h1.Length);
            var log2Model = LogEacFormat.CreateModel (s2, h2, dn);
            log2Model.CalcHashes (model.Bind.HashFlags, 0);
            log2Model.IssueModel.Escalate (model.Bind.WarnEscalator, model.Bind.ErrEscalator);

            var b1 = log1Model.Bind;
            Assert.IsFalse (b1.Issues.HasError);

            Assert.IsNotNull (log1Model.Bind.ShIssue);

            if ((model.Bind.HashFlags & Hashes.WebCheck) != 0)
                Assert.IsTrue (log1Model.Bind.ShIssue.Success == true);
            else
                Assert.IsTrue (log1Model.Bind.ShIssue.Success == false);

            var b2 = log2Model.Bind;
            Assert.IsTrue (b2.Issues.HasError);
            Assert.IsTrue (log1Model.Bind.ShIssue.Success == false);
            Assert.IsFalse (log1Model.Bind.ShIssue.Failure == true);
        }
    }
}

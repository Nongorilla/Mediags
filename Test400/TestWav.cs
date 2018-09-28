using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using NongIssue;
using NongFormat;

namespace UnitTest
{
    [TestClass]
    public class TestWav
    {
        [TestMethod]
        public void Test_Wav_1()
        {
            WavFormat.Model wavModel;

            var fn = @"Targets\Singles\StereoSilence10.wav";
            var file = new FileInfo (fn);

            using (var fs = new FileStream (fn, FileMode.Open, FileAccess.Read))
            {
                var hdr = new byte[0x2C];
                var got = fs.Read (hdr, 0, hdr.Length);
                Assert.AreEqual (hdr.Length, got);

                wavModel = WavFormat.CreateModel (fs, hdr, fs.Name);
                Assert.IsNotNull (wavModel);
            }

            WavFormat wav = wavModel.Data;

            Assert.IsTrue (wav.Issues.MaxSeverity == Severity.NoIssue);
            Assert.AreEqual (0, wav.Issues.Items.Count);

            Assert.AreEqual (2, wav.ChannelCount);
            Assert.AreEqual (44100u, wav.SampleRate);
            Assert.IsTrue (wav.HasTags);
        }
    }
}

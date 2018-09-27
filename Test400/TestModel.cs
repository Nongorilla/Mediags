using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NongFormat;
using NongMediaDiags;

namespace UnitTest
{
    [TestClass]
    public class TestModel
    {
        [TestMethod]
        public void Test_FrameworkProfile()
        {
            var assembly = System.Reflection.Assembly.GetAssembly (typeof (FormatBase));
            var att = assembly.GetCustomAttributes(false).OfType<System.Runtime.Versioning.TargetFrameworkAttribute>().Single();

            // The Controller/View assembly should match.
            Assert.AreEqual (".NETFramework,Version=v4.0,Profile=Client", att.FrameworkName);
        }

        [TestMethod]
        public void Test_FormatList()
        {
            var model = new Diags.Model (null);

            var formatsListText = model.Data.FormatListText;
            Assert.AreEqual ("ape, asf/wmv/wma, avi/divx, cue, db (Thumbs), flac, flv, gif, ico, jpg/jpeg, log (EAC), log (XLD), m3u, m3u8, m4a, md5, mkv/mka, mov/qt, mp3, mp4, mpg/mpeg/vob, ogg, png, sha1, sha1x, sha256, wav", formatsListText);
        }
    }
}

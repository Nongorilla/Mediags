using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text;
using NongFormat;
using NongIssue;
using AppViewModel;

namespace UnitTest
{
    public class MockDiagsController : IUi
    {
        StringBuilder console = new StringBuilder();
        DiagsPresenter.Model model;
        public DiagsPresenter ModelView { get; private set; }

        public MockDiagsController (string[] args)
        {
            model = new DiagsPresenter.Model (this);
            ModelView = model.Data;
            ModelView.Scope = Granularity.Detail;
            ModelView.HashFlags = Hashes.Intrinsic;
            ModelView.Root = args != null && args.Length > 0 ? args[args.Length-1] : null;
        }

        public string BrowseFile()
         => throw new NotImplementedException();

        public void ConsoleZoom (int delta)
         => throw new NotImplementedException();

        public void FileProgress (string dirName, string fileName)
        {
        }

        public void SetText (string message)
        {
            console.Clear();
            console.AppendLine (message);
        }

        public void ShowLine (string message, Severity severity, Likeliness repairability)
        {
            console.AppendLine (message);
        }

        public IList<string> GetHeadings()
         => new List<string> { "Console", ".m3u", ".mp3", ".ogg" };
    }


    [TestClass]
    public class TestMvvm
    {
        [TestMethod]
        public void Test_MvvmM3u()
        {
            var mdc = new MockDiagsController (new string[] { @"Targets\Hashes\Bad02.m3u" });
            mdc.ModelView.DoParse.Execute (null);
            M3uFormat m3u = mdc.ModelView.M3u;
            Assert.IsNotNull (m3u);
            Assert.AreEqual (2, m3u.Files.FoundCount);
            Assert.AreEqual (3, m3u.Files.Items.Count);

            mdc.ModelView.Root = @"Targets\Hashes\OK02.m3u";
            mdc.ModelView.DoParse.Execute (null);
            m3u = mdc.ModelView.M3u;
            Assert.IsNotNull (m3u);
            Assert.AreEqual (3, m3u.Files.FoundCount);
            Assert.AreEqual (3, m3u.Files.Items.Count);
            Assert.AreEqual (m3u.Name, "OK02.m3u");

            mdc.ModelView.CurrentTabNumber = 1;
            mdc.ModelView.NavFirst.Execute (null);
            Assert.AreEqual (mdc.ModelView.M3u.Name, "Bad02.m3u");
        }

        [TestMethod]
        public void Test_MvvmMp3()
        {
            var args = new string[] { @"Targets\Singles\02-WalkedOn.mp3" };
            var mdc = new MockDiagsController (args);

            Assert.IsNotNull (mdc.ModelView);

            mdc.ModelView.DoParse.Execute (null);
            Mp3Format mp3 = mdc.ModelView.Mp3;

            Assert.IsNotNull (mp3);
            Assert.IsFalse (mp3.HasId3v1Phantom);
            Assert.IsTrue (mp3.IsBadData);
            Assert.AreEqual (Severity.Error, mp3.Issues.MaxSeverity);
        }
    }
}

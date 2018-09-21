using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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
            ModelView = model.View;
            ModelView.Scope = Granularity.Detail;
            ModelView.HashFlags = Hashes.Intrinsic;
            ModelView.Root = args != null && args.Length > 0 ? args[args.Length-1] : null;
        }

        public string BrowseFile()
         => throw new NotImplementedException();

        public void ConsoleZoom (int delta)
         => throw new NotImplementedException();

        public string CurrentFormat()
         => null;

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
    }


    [TestClass]
    public class TestMvvm
    {
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

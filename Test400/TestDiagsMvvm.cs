using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text;
using NongFormat;
using NongIssue;
using AppViewModel;

namespace UnitTest
{
    public class MockDiagsUi : IUi
    {
        private readonly MockDiagsController view;
        private readonly DiagsPresenter.Model model;
        private readonly StringBuilder console;

        public MockDiagsUi (MockDiagsController view, DiagsPresenter.Model model)
        {
            this.model = model;
            this.view = view;
            console = new StringBuilder();
        }

        public string BrowseFile()
        {
            throw new NotImplementedException();
        }

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

    public interface IMockDiagsIUiFactory
    {
        IUi Create (MockDiagsController controller, DiagsPresenter.Model model);
    }


    public class MockDiagsUiFactory : IMockDiagsIUiFactory
    {
        public IUi Create (MockDiagsController controller, DiagsPresenter.Model model)
         => new MockDiagsUi (controller, model);
    }

    public class MockDiagsController
    {
        public DiagsPresenter ModelView { get; private set; }

        public MockDiagsController (string rootName = null)
        {
            var factory = new MockDiagsUiFactory();
            var model = new DiagsPresenter.Model ((m) => factory.Create (this, m));
            ModelView = model.View;
            ModelView.Scope = Granularity.Detail;
            ModelView.HashFlags = Hashes.Intrinsic;
            ModelView.Root = rootName;
        }
    }


    [TestClass]
    public class TestMvvm
    {
        [TestMethod]
        public void Test_MvvmMp3()
        {
            var fn = @"Targets\Singles\01-Phantom.mp3";
            var mdc = new MockDiagsController (fn);

            Assert.IsNotNull (mdc.ModelView);

            mdc.ModelView.DoParse.Execute (null);
            Mp3Format mp3 = mdc.ModelView.Mp3;

            Assert.IsNotNull (mp3);
            Assert.IsTrue (mp3.HasId3v1Phantom);
        }
    }
}

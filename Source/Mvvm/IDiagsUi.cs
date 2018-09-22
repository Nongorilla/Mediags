using System.Collections.Generic;
using NongFormat;
using NongIssue;

namespace AppViewModel
{
    public interface IUi
    {
        string BrowseFile();
        void FileProgress (string dirName, string fileName);
        void ShowLine (string message, Severity severity, Likeliness repairability);
        void SetText (string message);
        void ConsoleZoom (int delta);
        IList<string> GetHeadings();
    }
}

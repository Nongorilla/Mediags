using System.Windows.Controls;

namespace AppController
{
    public partial class HistoryControl : UserControl
    {
        public HistoryControl()
        { InitializeComponent(); }

        public void ScrollToEnd()
        {
            if (histList.Items.Count > 1)
                histList.ScrollIntoView (histList.Items[histList.Items.Count-1]);
        }

        private void histList_Loaded (object sender, System.Windows.RoutedEventArgs e)
        {
            ScrollToEnd();
        }
    }
}

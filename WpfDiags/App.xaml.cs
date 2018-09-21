using System.Windows;

namespace AppController
{
    public partial class App : Application
    {
        protected override void OnStartup (StartupEventArgs supArgs)
        {
            base.OnStartup (supArgs);
            MainWindow = new WpfDiagsView (supArgs.Args); //, new WpfDiagsUiFactory());
            MainWindow.Show();
        }
    }
}

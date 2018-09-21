using System.Windows;

namespace AppView
{
    public partial class App : Application
    {
        protected override void OnStartup (StartupEventArgs supArgs)
        {
            base.OnStartup (supArgs);
            MainWindow = new WpfDiagsView (supArgs.Args);
            MainWindow.Show();
        }
    }
}

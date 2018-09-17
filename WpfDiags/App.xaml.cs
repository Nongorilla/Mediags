using System.Windows;
using AppViewModel;

namespace AppController
{
    public class WpfDiagsUiFactory : IWpfDiagsIUiFactory
    {
        public IUi Create (WpfDiagsController controller, DiagsPresenter.Model model)
         => new WpfDiagsUi (controller, model);
    }

    public partial class App : Application
    {
        protected override void OnStartup (StartupEventArgs supArgs)
        {
            base.OnStartup (supArgs);
            MainWindow = new WpfDiagsController (supArgs.Args, new WpfDiagsUiFactory());
            MainWindow.Show();
        }
    }
}

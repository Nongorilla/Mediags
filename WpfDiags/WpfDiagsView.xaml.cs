using System.Windows;
using AppViewModel;
using NongFormat;
using NongIssue;

namespace AppView
{
    public partial class WpfDiagsView : Window, IUi
    {
        private readonly string[] args;
        private DiagsPresenter.Model presenterModel;

        public WpfDiagsView (string[] args)
        {
            this.args = args;
            InitializeComponent();
        }

        public void Window_Loaded (object sender, RoutedEventArgs ea)
        {
            presenterModel = new DiagsPresenter.Model (this);
            presenterModel.Data.Scope = Granularity.Detail;
            presenterModel.Data.HashFlags = Hashes.Intrinsic;

            //TODO parse command line
            presenterModel.Data.Root = args.Length > 0 ? args[args.Length-1] : null;

            DataContext = presenterModel.Data;
        }
    }
}

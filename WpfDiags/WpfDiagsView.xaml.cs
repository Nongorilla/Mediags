using System.Windows;
using AppViewModel;
using NongFormat;
using NongIssue;

namespace AppView
{
    public partial class WpfDiagsView : Window, IDiagsUi
    {
        private readonly string[] args;
        private DiagsPresenter.Model viewModel;

        public WpfDiagsView (string[] args)
        {
            this.args = args;
            InitializeComponent();
        }

        public void Window_Loaded (object sender, RoutedEventArgs ea)
        {
            viewModel = new DiagsPresenter.Model (this);
            viewModel.Data.Scope = Granularity.Detail;
            viewModel.Data.HashFlags = Hashes.Intrinsic;

            //TODO parse command line
            viewModel.Data.Root = args.Length > 0 ? args[args.Length-1] : null;
            DataContext = viewModel.Data;
        }
    }
}

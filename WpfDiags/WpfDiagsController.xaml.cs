using System;
using System.Windows;
using System.Windows.Data;
using AppViewModel;
using NongFormat;
using NongIssue;

namespace AppController
{
    public class ComparisonConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        { return value.Equals (param); }

        public object ConvertBack (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        { return value.Equals (true) ? param : Binding.DoNothing; }
    }

    public class HashToggle : IValueConverter
    {
        public object Convert (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        => ((int) value & (int) param) != 0;

        public object ConvertBack (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        => value.Equals (true) ? (Hashes) param : (Hashes) ~ (int) param;
    }

    public class ValidationToggle : IValueConverter
    {
        public object Convert (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        => ((int) value & (int) param) != 0;

        public object ConvertBack (object value, Type targetType, object param, System.Globalization.CultureInfo culture)
        => value.Equals (true) ? (Validations) param : (Validations) ~(int) param;
    }

    public interface IWpfDiagsIUiFactory
    {
        IUi Create (WpfDiagsController controller, DiagsPresenter.Model model);
    }

    public partial class WpfDiagsController : Window
    {
        private readonly string[] args;
        private readonly IWpfDiagsIUiFactory iUiFactory;
        private DiagsPresenter.Model model;

        public WpfDiagsController (string[] args, IWpfDiagsIUiFactory factory)
        {
            this.args = args;
            this.iUiFactory = factory;
            InitializeComponent();
        }

        public void Window_Loaded (object sender, RoutedEventArgs ea)
        {
            model = new DiagsPresenter.Model ((m) => iUiFactory.Create (this, m));

            if (args.Length > 0)
                model.View.Root = args[0];
            model.View.Scope = Granularity.Detail;
            model.View.HashFlags = Hashes.Intrinsic;

            DataContext = model.View;
        }
    }
}

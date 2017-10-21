using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NongFormat;

namespace AppController
{
    public partial class LogEacControl : UserControl
    {
        public LogEacControl()
        { InitializeComponent(); }


        private void log_PreviewMouseLeftButtonDown (object sender, MouseButtonEventArgs e)
        {
            var lvi = sender as ListViewItem;
            if (sender is ListViewItem)
            {
                var track = lvi.Content as LogEacTrack;
                if (track != null)
                {
                    var flac = track.Match;
                    if (flac != null)
                    {
                        var bx = new ListBox();
                        foreach (var tag in flac.Blocks.Tags.Lines)
                            bx.Items.Add (tag);

                        var pp = new Popup();
                        pp.Child = bx;
                        pp.PlacementTarget = lvi;
                        pp.Placement = PlacementMode.MousePoint;
                        pp.StaysOpen = false;
                        pp.IsOpen = true;
                    }
                }
            }
        }


        void HeaderClicked (object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;

            if (header != null && header.Role != GridViewColumnHeaderRole.Padding)
            {
                header.Column.Width = 0;
                header.Column.Width = Double.NaN;
            }
        }
    }
}

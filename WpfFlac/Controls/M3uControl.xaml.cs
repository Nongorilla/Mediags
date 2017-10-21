using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NongFormat;

namespace AppController
{
    public partial class M3uControl : UserControl
    {
        public M3uControl()
        { InitializeComponent(); }


        private void m3uList_MouseMove (object sender, MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var m3u = DataContext as M3uFormat;
                var nameBlock = e.OriginalSource as System.Windows.Controls.TextBlock;
                if (m3u != null && nameBlock != null)
                {
                    var cargo = new string[] { Path.GetDirectoryName (m3u.Path) + Path.DirectorySeparatorChar + nameBlock.Text };
                    var data = new DataObject (DataFormats.FileDrop, cargo);
                    DragDrop.DoDragDrop (this, data, DragDropEffects.Copy);
                }
            }
        }
    }
}

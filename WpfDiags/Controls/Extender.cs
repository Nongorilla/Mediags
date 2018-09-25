using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace AppView
{
    public class Extender : DependencyObject
    {
        private static readonly DependencyProperty ScrollToBottomProperty = DependencyProperty.RegisterAttached ("ScrollToBottom", typeof (bool), typeof (Extender), new UIPropertyMetadata (default (bool), OnScrollToBottomChanged));

        public static bool GetScrollToBottom (DependencyObject obj)
         => (bool) obj.GetValue (ScrollToBottomProperty);

        public static void SetScrollToBottom (DependencyObject obj, bool value)
         => obj.SetValue (ScrollToBottomProperty, value);

        public static void OnScrollToBottomChanged (DependencyObject dOb, DependencyPropertyChangedEventArgs ea)
        {
            var listBox = (ListBox) dOb;
            var items = listBox.Items;
            var scrollToBottomHandler = new NotifyCollectionChangedEventHandler ((x, e) =>
            {
                if (items.Count > 0)
                    listBox.ScrollIntoView (items[items.Count - 1]);
            });

            System.Diagnostics.Debug.Assert ((bool) ea.NewValue);
            ((INotifyCollectionChanged) items.SourceCollection).CollectionChanged += scrollToBottomHandler;
        }
    }
}

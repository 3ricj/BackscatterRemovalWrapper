using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using BsxtBatch.App.ViewModels;

namespace BsxtBatch.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        // Auto-scroll the log. The scroll MUST be deferred: ScrollIntoView called
        // synchronously inside CollectionChanged runs before the ListBox's
        // ItemContainerGenerator has applied the Add, which throws
        // "An ItemsControl is inconsistent with its items source". Dispatching at
        // Background priority lets the generator catch up first.
        ((INotifyCollectionChanged)_viewModel.LogLines).CollectionChanged += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (LogBox.Items.Count > 0)
                        LogBox.ScrollIntoView(LogBox.Items[^1]);
                }
                catch
                {
                    // A failed auto-scroll must never take the app down.
                }
            }, DispatcherPriority.Background);
        };
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            _viewModel.AddDroppedPaths(paths);
    }
}

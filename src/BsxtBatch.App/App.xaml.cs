using System.Windows;
using BsxtBatch.Core.Models;

namespace BsxtBatch.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // BatchJob raises PropertyChanged from the batch's worker (STA) thread; route those
        // notifications onto the WPF dispatcher so the job list updates safely.
        BatchJob.UiMarshal = action =>
        {
            var dispatcher = Dispatcher;
            if (dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        };

        base.OnStartup(e);
    }
}

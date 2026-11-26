using System.Windows;
using System.Windows.Media.Imaging;

namespace LiveCG.App;
public partial class App : Application
{
    public App()
    {
#if LIVE_CG_STARTUP_DIAGNOSTICS
        StartupDiagnostics.Begin(this);
#endif
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is Window window&&window.Icon is null)window.Icon=new BitmapImage(new Uri("pack://application:,,,/LiveCG-EFF;component/Assets/LiveCG.ico"));}));
    }
    protected override void OnStartup(StartupEventArgs e)
    {
#if !LIVE_CG_STARTUP_DIAGNOSTICS
        DispatcherUnhandledException += (_, args) =>
        { MessageBox.Show(args.Exception.Message,"LiveCG-EFF · 오류",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true; };
#endif
        base.OnStartup(e);
    }
}

using System.Windows;
using System.Windows.Threading;

namespace SmartWater.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvt;
    private AppServices? _svc;
    private TrayIcon? _tray;
    private MainWindow? _win;
    private DispatcherTimer? _tick;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mutex = new Mutex(true, "SmartWaterMonitor.SingleInstance", out bool first);
        _showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "SmartWaterMonitor.ShowWindow");
        if (!first) { _showEvt.Set(); Shutdown(); return; }       // نسخة تعمل: نُظهر نافذتها فقط

        DispatcherUnhandledException += (_, a) =>
        {
            FileLog.Write("ERROR", "استثناء غير معالَج: " + a.Exception);
            a.Handled = true;                                       // لا تسقط المراقبة 24 ساعة بسبب خطأ في الواجهة
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => FileLog.Write("FATAL", a.ExceptionObject?.ToString() ?? "");

        try
        {
            _svc = new AppServices();
            _win = new MainWindow(_svc);
            _tray = new TrayIcon(_svc);
            _tray.OpenRequested += ShowMain;
            _tray.ExitRequested += ExitApp;
            _svc.Notify += m => Dispatcher.BeginInvoke(new Action(() => _tray?.Balloon(m)));
            _win.Swung += t => _tray?.Balloon(t.Length > 120 ? t[..120] : t);
            ThreadPool.RegisterWaitForSingleObject(_showEvt, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMain)), null, -1, false);

            bool minimized = e.Args.Contains("--minimized");
            if (!minimized) ShowMain();
            else _tray.Balloon("يعمل في الخلفية — انقر الأيقونة لفتح النافذة");

            _svc.StartMonitoring();                                  // يبدأ تلقائيًا إن كان الترخيص صالحًا
            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _tick.Tick += async (_, _) => { await _svc.TickAsync(); _tray?.SetTooltip(_win.TooltipText()); };
            _tick.Start();
        }
        catch (Exception ex)
        {
            FileLog.Write("FATAL", ex.ToString());
            MessageBox.Show("تعذّر تشغيل التطبيق:\n" + ex.Message, "مراقب المياه", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ShowMain()
    {
        if (_win == null) return;
        _win.Show();
        if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
        _win.Activate();
    }

    private void ExitApp()
    {
        if (MessageBox.Show("الخروج يوقف المراقبة نهائيًا حتى تفتح البرنامج من جديد. هل تريد الخروج؟", "مراقب المياه",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _tick?.Stop();
        if (_win != null) { _win.ReallyClosing = true; _win.Close(); }
        _tray?.Dispose();
        _svc?.Dispose();
        _mutex?.ReleaseMutex();
        Shutdown();
    }
}

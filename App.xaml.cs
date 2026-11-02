using System.Windows;
namespace NeonDesk;
public partial class App : Application
{
    private Mutex? _instance;
    private bool _owned;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, "Local\\NeonDesk-" + Environment.UserName, out _owned);
        if (!_owned) { MessageBox.Show("清理台已经打开，请切换到已有窗口。"); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { if (_owned) _instance?.ReleaseMutex(); _instance?.Dispose(); base.OnExit(e); }
}

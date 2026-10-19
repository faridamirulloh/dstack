using MiniCodex.Presentation;
using System.Windows;
using MiniCodex.Services;

namespace MiniCodex;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    public WindowManager WindowManager { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        NotificationService.RegisterActivation();
        _instanceMutex = new Mutex(false, @"Local\MiniCodex.LocalUi");
        try { _ownsMutex = _instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            AppDialog.Show("Mini Codex is already running. Use its existing window.", "Mini Codex", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        try
        {
            var controller = new AppController(new StateStore());
            AppZoom.Current.Initialize(controller);
            WindowManager = new WindowManager(controller);
            _ = WindowManager.Notifications;
            await WindowManager.RestoreAccountLayoutAsync();
        }
        catch (Exception error)
        {
            AppDialog.Show(error.Message, "Mini Codex could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

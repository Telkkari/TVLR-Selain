using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TVLR_Selain_3;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            try { SetCurrentProcessExplicitAppUserModelID("TVLRSelain"); } catch { }
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            SovellusAsetukset.Instance.Lataa();

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);
}

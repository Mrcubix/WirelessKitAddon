using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WirelessKitAddon.UX.ViewModels;

namespace WirelessKitAddon.UX;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Hide dock icon after the run loop starts — Avalonia resets the
        // activation policy during startup, so this must be dispatched.
        if (OperatingSystem.IsMacOS())
            Dispatcher.UIThread.Post(HideDockIcon, DispatcherPriority.Background);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;

            var vm = new WirelessKitViewModel(desktop.Args);

            vm.CloseRequested += delegate
            {
                Dispatcher.UIThread.Invoke(() => desktop.Shutdown());
            };

            DataContext = vm;
        }

        base.OnFrameworkInitializationCompleted();
    }

    #region macOS Dock Hiding

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_nint(IntPtr receiver, IntPtr selector, nint arg);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    private static void HideDockIcon()
    {
        var nsApp = objc_getClass("NSApplication");
        var sharedApp = objc_msgSend(nsApp, sel_registerName("sharedApplication"));
        // NSApplicationActivationPolicyAccessory = 1 (no dock icon, no menu bar)
        objc_msgSend_nint(sharedApp, sel_registerName("setActivationPolicy:"), 1);
    }

    #endregion
}

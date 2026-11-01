using System;
using System.Threading.Tasks;
using Avalonia;
using SleepGuard.Services;
using TKCode.Arc;
using TKCode.Arc.Fonts;

namespace SleepGuard;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppOptions.Parse(args);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorLog.Write("未处理的异常", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write("未观察的任务异常", e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .WithArcFonts()
            .UseArc(arc => arc.UseArcControls())
            .LogToTrace();
}

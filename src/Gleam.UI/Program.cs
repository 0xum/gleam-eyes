using Avalonia;
using System;

namespace Gleam.Ui;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
            {
                Gleam.Engine.Processing.PluginLogger.Log($"[Fatal] UnhandledException: {ex}\n{ex.StackTrace}");
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Gleam.Engine.Processing.PluginLogger.Log($"[Fatal] UnobservedTaskException: {eventArgs.Exception}\n{eventArgs.Exception.StackTrace}");
            eventArgs.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
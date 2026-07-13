using Serilog;
using System.IO;
using System;

namespace StayOnTarget;

public static class LogConfig
{
    public static void Initialize()
    {
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StayOnTarget", "Logs");
        try
        {
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            var logFilePath = Path.Combine(logDirectory, "log-.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();

            Log.Information("Application starting up...");
        }
        catch (Exception ex)
        {
            // Fallback if logging fails to initialize
            System.Diagnostics.Debug.WriteLine($"Failed to initialize logging: {ex}");
        }
    }

    public static void Shutdown()
    {
        Log.Information("Application shutting down...");
        Log.CloseAndFlush();
    }
}

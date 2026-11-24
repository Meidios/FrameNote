using System.IO;
using Serilog;
using Serilog.Events;

namespace FrameNote;

internal static class RuntimeLog
{
    internal static readonly string DataDirectory = Environment.GetEnvironmentVariable("FRAMENOTE_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameNote");
    internal static string DirectoryPath => Path.Combine(DataDirectory, "Logs");

    internal static void Start(bool player)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            Log.Logger = new LoggerConfiguration()
                .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                .Enrich.WithProperty("Host", player ? "Player" : "FrameNote")
                .WriteTo.File(Path.Combine(DirectoryPath, "FrameNote-.log"),
                    rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 2 * 1024 * 1024,
                    retainedFileCountLimit: 10, shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Host}/{ProcessId}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Logging is optional when the profile cannot be written.
            return;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error(e.Exception, "Unobserved task failure");
        Log.Information("Started version {Version} on {OS}, .NET {Runtime}", Updates.Running, Environment.OSVersion, Environment.Version);
    }

    internal static void WebMessage(string? message, bool error)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Log.Write(error ? LogEventLevel.Error : LogEventLevel.Information, "Review: {Message}", message[..Math.Min(message.Length, 4096)]);
    }
}

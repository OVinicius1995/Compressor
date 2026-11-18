using System.Text.Json;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string? Option(string key)
        {
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
        }
        string bundledConfig = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        bool installed = File.Exists(Path.Combine(AppContext.BaseDirectory, "installed.marker"));
        string config = Path.GetFullPath(Option("--config") ??
            (installed ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JiraVideoCompressor", "appsettings.json") :
            File.Exists(bundledConfig) ? bundledConfig : Path.Combine(ProjectDirectory(), "appsettings.json")));
        bool console = args.Any(a => a is "--console" or "--once" or "--manual" or "--prepare-tools");
        if (!console)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(config));
            return 0;
        }
        try
        {
            string? manual = Option("--manual");
            if (args.Contains("--manual") && manual is null)
            {
                Console.WriteLine("Cole o caminho completo do vídeo:");
                manual = (Console.ReadLine() ?? "").Trim().Trim('"');
            }
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            var settings = Settings.Load(config);
            ToolBootstrapper.PrepareAsync(settings, config, null, cancellation.Token).GetAwaiter().GetResult();
            if (args.Contains("--prepare-tools")) return 0;
            return new MonitorService(settings, config).Run(args.Contains("--once") || manual is not null, manual, cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    internal static string ProjectDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "JiraVideoCompressor.csproj"))) return directory.FullName;
        return AppContext.BaseDirectory;
    }
}

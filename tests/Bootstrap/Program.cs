using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class BootstrapChecks
{
    static async Task Main(string[] args)
    {
        string root = Path.Combine(Path.GetFullPath(args[0]), ".test-artifacts", "bootstrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        CleanupChecks.Run(root);
        byte[] archive;
        using (var bytes = new MemoryStream())
        {
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                foreach (string file in new[] { "bin/ffmpeg.exe", "bin/ffprobe.exe", "LICENSE.txt" })
                {
                    using var writer = new StreamWriter(zip.CreateEntry("vendor/" + file).Open());
                    writer.Write("fixture-" + file);
                }
            archive = bytes.ToArray();
        }
        var progress = new ImmediateProgress();
        var settings = Configuration(root);
        string legacyConfig = Path.Combine(root, "old-settings.json");
        File.WriteAllText(legacyConfig, JsonSerializer.Serialize(new { ToolsDirectory = "old-custom-tools" }));
        var migratedSettings = Settings.Load(legacyConfig);
        Check(migratedSettings.ToolsDirectory == Path.Combine(root, "tools", "ffmpeg", "bin"), "Configuração antiga alterou o destino fixo.");
        migratedSettings.Save(legacyConfig);
        Check(!File.ReadAllText(legacyConfig).Contains("ToolsDirectory"), "Configuração continua expondo caminhos dos recursos.");
        Check(ToolBootstrapper.UserGuidance(new HttpRequestException()).Contains("internet") &&
            ToolBootstrapper.UserGuidance(new UnauthorizedAccessException()).Contains("Documentos"), "Orientação de preparação não é acionável.");
        var handler = new VendorHandler(archive);
        using var client = new HttpClient(handler);
        await ToolBootstrapper.EnsureAsync(settings, Path.Combine(root, "appsettings.json"), progress, CancellationToken.None, client);
        Check(File.Exists(settings.FFmpeg) && File.Exists(settings.FFprobe), "Instalação incompleta.");
        Check(File.Exists(Path.Combine(root,"tools","ffmpeg","LICENSE.txt")), "Licença do fornecedor ausente.");
        Check(File.Exists(Path.Combine(root,"tools","ffmpeg","ORIGIN.json")), "Origem/hash não registrados.");
        Check(progress.Maximum == 100, "Progresso não chegou a 100%.");
        await ToolBootstrapper.EnsureAsync(settings, Path.Combine(root, "appsettings.json"), null, CancellationToken.None, client);
        Check(handler.Requests == 2, "Instalação existente provocou novo download.");
        foreach (string failure in new[] { "hash", "incomplete", "url", "missing" })
        {
            string test = Path.Combine(root, failure);
            Directory.CreateDirectory(test);
            using var failedClient = new HttpClient(new VendorHandler(archive, failure));
            bool rejected = false;
            try { await ToolBootstrapper.EnsureAsync(Configuration(test), Path.Combine(test, "appsettings.json"), null, CancellationToken.None, failedClient); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && !File.Exists(Configuration(test).FFmpeg), "Pacote inválido instalado: " + failure);
        }
        string canceledRoot = Path.Combine(root, "canceled");
        Directory.CreateDirectory(canceledRoot);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool interrupted = false;
        try { await ToolBootstrapper.EnsureAsync(Configuration(canceledRoot), Path.Combine(canceledRoot,"appsettings.json"), null, canceled.Token, client); }
        catch (OperationCanceledException) { interrupted = true; }
        Check(interrupted && !File.Exists(Configuration(canceledRoot).FFmpeg), "Cancelamento instalou arquivo parcial.");
        if (args.Length > 1)
        {
            string migrationRoot = Path.Combine(root, "legacy-installation");
            Directory.CreateDirectory(migrationRoot);
            string path = Path.Combine(migrationRoot, "appsettings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { ToolsDirectory = Path.GetFullPath(args[1]) }));
            var migration = Settings.Load(path);
            await ToolBootstrapper.PrepareAsync(migration, path, null, CancellationToken.None);
            await ToolBootstrapper.ValidateReadyAsync(migration, CancellationToken.None);
            Check(File.Exists(migration.FFmpeg) && File.Exists(migration.FFprobe), "Migração da instalação existente falhou.");
            File.Delete(migration.FFprobe);
            await ToolBootstrapper.PrepareAsync(migration, path, null, CancellationToken.None);
            Check(File.Exists(migration.FFprobe), "Componente ausente não foi recuperado da instalação antiga.");
        }
        Console.WriteLine("PASSOU: preparação automática, hash, tamanho, origem, licença, progresso, reutilização e cancelamento.");
    }
    static Settings Configuration(string root)
    {
        var settings = new Settings();
        settings.Resolve(root);
        return settings;
    }
    static void Check(bool success, string message) { if (!success) throw new Exception(message); }
    sealed class ImmediateProgress : IProgress<double>
    {
        public double Maximum { get; private set; }
        public void Report(double value) => Maximum = Math.Max(Maximum,value);
    }
    sealed class VendorHandler(byte[] archive, string failure = "") : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            if (request.RequestUri!.AbsoluteUri == ToolBootstrapper.ReleaseApi)
            {
                string digest = failure == "hash" ? new string('0',64) : Convert.ToHexString(SHA256.HashData(archive));
                string payload = JsonSerializer.Serialize(new { assets = failure == "missing" ? Array.Empty<object>() : new object[] { new
                {
                    name = ToolBootstrapper.AssetName, digest = "sha256:" + digest, size = archive.Length,
                    browser_download_url = failure == "url" ? "https://example.com/arquivo.zip" : "https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/test/ffmpeg.zip"
                } } });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload,Encoding.UTF8,"application/json") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(failure == "incomplete" ? archive[..20] : archive) });
        }
    }
}

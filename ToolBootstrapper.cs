using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

sealed class ToolBootstrapper
{
    internal const string ReleaseApi = "https://api.github.com/repos/FFmpeg-Builds/FFmpeg-Builds/releases/latest";
    internal const string AssetName = "ffmpeg-master-latest-win64-gpl.zip";
    public static async Task EnsureAsync(Settings settings, string configPath, IProgress<double>? progress,
        CancellationToken token, HttpClient? suppliedClient = null)
    {
        if (File.Exists(settings.FFmpeg) && File.Exists(settings.FFprobe)) return;
        string root = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        string managedRoot = Path.Combine(root, "tools", "ffmpeg");
        string managedBin = Path.Combine(managedRoot, "bin");
        if (!settings.ToolsDirectory.Equals(managedBin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FFmpeg/FFprobe não encontrado na pasta escolhida. Selecione a pasta correta ou use tools/ffmpeg/bin para preparação automática.");
        Directory.CreateDirectory(Path.GetDirectoryName(managedRoot)!);
        using var installationLock = new FileStream(managedRoot + ".setup.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string work = Path.Combine(root, ".work", "ffmpeg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        using var ownClient = suppliedClient is null ? new HttpClient { Timeout = TimeSpan.FromMinutes(15) } : null;
        HttpClient client = suppliedClient ?? ownClient!;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
            request.Headers.UserAgent.ParseAdd("JiraVideoCompressor/0.1.0");
            using var metadataResponse = await client.SendAsync(request, token);
            metadataResponse.EnsureSuccessStatusCode();
            using var metadata = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync(token));
            var asset = metadata.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString() == AssetName);
            if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("A distribuição do FFmpeg não está disponível no fornecedor.");
            string digest = asset.GetProperty("digest").GetString() ?? "";
            if (!digest.StartsWith("sha256:") || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
                throw new InvalidDataException("O fornecedor não informou um SHA-256 válido; download não instalado.");
            var downloadUrl = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (downloadUrl.Scheme != "https" || downloadUrl.Host != "github.com" ||
                !downloadUrl.AbsolutePath.StartsWith("/FFmpeg-Builds/FFmpeg-Builds/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("Endereço inesperado para a distribuição do FFmpeg.");
            long expectedLength = asset.GetProperty("size").GetInt64();
            if (expectedLength <= 0 || expectedLength > 500_000_000) throw new InvalidDataException("Tamanho inesperado para o pacote do FFmpeg.");
            string archive = Path.Combine(work, "ffmpeg.zip");
            using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var destination = File.Create(archive);
                byte[] buffer = new byte[1024 * 1024];
                int count;
                long transferred = 0;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    transferred += count;
                    if (transferred > expectedLength) throw new InvalidDataException("Download excedeu o tamanho informado.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), token);
                    progress?.Report(transferred * 90.0 / expectedLength);
                }
                if (transferred != expectedLength) throw new InvalidDataException("Download incompleto; tente iniciar novamente.");
            }
            using (var stream = File.OpenRead(archive))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 do FFmpeg não confere; o pacote não foi instalado. Tente novamente.");
            progress?.Report(95);
            using (var zip = ZipFile.OpenRead(archive))
            {
                var executable = zip.Entries.Single(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.Ordinal));
                string prefix = executable.FullName[..^"bin/ffmpeg.exe".Length];
                foreach (string relative in new[] { "bin/ffmpeg.exe", "bin/ffprobe.exe", "LICENSE.txt" })
                {
                    token.ThrowIfCancellationRequested();
                    var entry = zip.GetEntry(prefix + relative) ?? throw new InvalidDataException("Distribuição do FFmpeg incompleta.");
                    if (entry.Length <= 0 || entry.Length > 300_000_000) throw new InvalidDataException("Arquivo inesperado no pacote do FFmpeg.");
                    string extracted = Path.Combine(work, Path.GetFileName(relative));
                    entry.ExtractToFile(extracted);
                }
            }
            string ready = Path.Combine(work, "ready");
            foreach (string relative in new[] { "bin/ffmpeg.exe", "bin/ffprobe.exe", "LICENSE.txt" })
            {
                string staged = Path.Combine(ready, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                File.Move(Path.Combine(work, Path.GetFileName(relative)), staged);
            }
            await File.WriteAllTextAsync(Path.Combine(ready, "ORIGIN.json"),
                JsonSerializer.Serialize(new { releaseApi = ReleaseApi, url = downloadUrl.ToString(), sha256 = digest[7..],
                    license = "GPL-3.0-or-later (build do fornecedor)", installedAt = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }), token);
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(managedRoot))
            {
                Directory.Move(ready, managedRoot); // Primeira instalação completa aparece de uma vez.
                progress?.Report(100);
                return;
            }
            // Recupera uma pasta já criada, sem sobrescrever ferramentas de outro conteúdo.
            foreach (string relative in new[] { "bin/ffmpeg.exe", "bin/ffprobe.exe", "LICENSE.txt", "ORIGIN.json" })
            {
                string target = Path.Combine(managedRoot, relative);
                string extracted = Path.Combine(ready, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    if (relative == "ORIGIN.json") continue;
                    using var existing = File.OpenRead(target);
                    using var incoming = File.OpenRead(extracted);
                    if (!(await SHA256.HashDataAsync(existing, token)).SequenceEqual(await SHA256.HashDataAsync(incoming, token)))
                        throw new IOException("Existe um arquivo diferente na preparação do FFmpeg. Escolha outra pasta ou revise tools/ffmpeg antes de tentar novamente.");
                }
                else File.Move(extracted, target);
            }
            progress?.Report(100);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

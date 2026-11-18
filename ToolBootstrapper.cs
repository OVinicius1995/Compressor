using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;

sealed class ToolBootstrapper
{
    internal const string ReleaseApi = "https://api.github.com/repos/FFmpeg-Builds/FFmpeg-Builds/releases/latest";
    internal const string AssetName = "ffmpeg-master-latest-win64-gpl.zip";
    public static async Task PrepareAsync(Settings settings, string configPath, IProgress<double>? progress, CancellationToken token)
    {
        try
        {
            if (File.Exists(settings.FFmpeg) && File.Exists(settings.FFprobe))
            {
                try { await ValidateReadyAsync(settings, token); return; }
                catch (Exception ex) when (ex is InvalidDataException or Win32Exception or IOException)
                {
                    QuarantineManagedResources(settings, configPath);
                }
            }
            else if (File.Exists(settings.FFmpeg) || File.Exists(settings.FFprobe))
                QuarantineManagedResources(settings, configPath);
            if (!string.IsNullOrEmpty(settings.LegacyToolsDirectory) &&
                !settings.LegacyToolsDirectory.Equals(settings.ToolsDirectory, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(settings.LegacyToolsDirectory, "ffmpeg.exe")) && File.Exists(Path.Combine(settings.LegacyToolsDirectory, "ffprobe.exe")))
            {
                bool usable;
                try { await ValidateDirectoryAsync(settings.LegacyToolsDirectory, token); usable = true; }
                catch (Exception ex) when (ex is InvalidDataException or Win32Exception or IOException) { usable = false; }
                if (usable)
                {
                    Directory.CreateDirectory(settings.ToolsDirectory);
                    foreach (string name in new[] { "ffmpeg.exe", "ffprobe.exe" })
                    {
                        token.ThrowIfCancellationRequested();
                        if (!File.Exists(Path.Combine(settings.ToolsDirectory, name)))
                            await Task.Run(() => File.Copy(Path.Combine(settings.LegacyToolsDirectory, name), Path.Combine(settings.ToolsDirectory, name)), token);
                    }
                    string oldLicense = Path.Combine(Path.GetDirectoryName(settings.LegacyToolsDirectory)!, "LICENSE.txt");
                    string newLicense = Path.Combine(Path.GetDirectoryName(settings.ToolsDirectory)!, "LICENSE.txt");
                    if (File.Exists(oldLicense) && !File.Exists(newLicense)) File.Copy(oldLicense, newLicense);
                }
            }
            await EnsureAsync(settings, configPath, progress, token);
            await ValidateReadyAsync(settings, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(configPath)!, "startup.log"), $"{DateTimeOffset.UtcNow:O} {ex}{Environment.NewLine}"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw new ComponentPreparationException(UserGuidance(ex), ex);
        }
    }

    public static Task ValidateReadyAsync(Settings settings, CancellationToken token) => ValidateDirectoryAsync(settings.ToolsDirectory, token);

    private static void QuarantineManagedResources(Settings settings, string configPath)
    {
        // O diretório de destino é fixo; apenas estes quatro arquivos gerenciados são movidos.
        string backup = Path.Combine(Path.GetDirectoryName(configPath)!, ".work", "invalid-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        foreach (string relative in new[] { "bin/ffmpeg.exe", "bin/ffprobe.exe", "LICENSE.txt", "ORIGIN.json" })
        {
            string current = Path.Combine(Path.GetDirectoryName(settings.ToolsDirectory)!, relative);
            if (File.Exists(current)) File.Move(current, Path.Combine(backup, Path.GetFileName(current)));
        }
    }

    private static async Task ValidateDirectoryAsync(string directory, CancellationToken token)
    {
        foreach (string name in new[] { "ffmpeg", "ffprobe" })
        {
            string executable = Path.Combine(directory, name + ".exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Recurso de vídeo ausente.", executable);
            string version = await CheckAsync(executable, "-version", token);
            if (!version.Contains(name + " version", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O recurso de vídeo não respondeu corretamente.");
        }
        string encoders = await CheckAsync(Path.Combine(directory, "ffmpeg.exe"), "-encoders", token);
        if (!System.Text.RegularExpressions.Regex.IsMatch(encoders, @"(?m)^\s*\S+\s+libx264(?:\s|$)")) throw new InvalidDataException("Recurso de compressão não disponível.");
    }

    private static async Task<string> CheckAsync(string executable, string argument, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-hide_banner"); start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidDataException("Não foi possível verificar os recursos de vídeo.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            token.ThrowIfCancellationRequested();
            throw new InvalidDataException("Os recursos de vídeo não responderam à verificação.");
        }
        await Task.WhenAll(output, error);
        if (process.ExitCode != 0) throw new InvalidDataException("Não foi possível utilizar os recursos de vídeo.");
        return await output;
    }

    internal static string UserGuidance(Exception error)
    {
        string action = error switch
        {
            UnauthorizedAccessException => "Extraia todo o ZIP para uma pasta sua, como Documentos. Abra o programa nessa pasta e tente novamente.",
            FileNotFoundException or DirectoryNotFoundException => "Alguns arquivos necessários não foram encontrados. Clique em Tentar preparar novamente para recuperá-los automaticamente. Se continuar, extraia todo o ZIP em uma pasta nova e abra Iniciar.cmd.",
            HttpRequestException or TaskCanceledException => "Verifique a conexão com a internet e clique em Tentar preparar novamente. A primeira preparação precisa de internet.",
            IOException when error is not InvalidDataException => "Confira se há espaço livre e se a pasta permite salvar arquivos. Feche outras janelas do Compressor e clique em Tentar preparar novamente.",
            _ => "Clique em Tentar preparar novamente. Se continuar, baixe o ZIP da versão mais recente, extraia todo o conteúdo em uma pasta nova e abra Iniciar.cmd."
        };
        return "Não foi possível preparar os recursos necessários para processar vídeos.\n\n" + action +
            "\n\nO monitoramento está bloqueado e nenhum vídeo será processado até a preparação terminar.";
    }
    public static async Task EnsureAsync(Settings settings, string configPath, IProgress<double>? progress,
        CancellationToken token, HttpClient? suppliedClient = null)
    {
        if (File.Exists(settings.FFmpeg) && File.Exists(settings.FFprobe)) return;
        string root = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        string managedRoot = Path.Combine(root, "tools", "ffmpeg");
        string managedBin = Path.Combine(managedRoot, "bin");
        if (!settings.ToolsDirectory.Equals(managedBin, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A localização gerenciada dos recursos está inválida.");
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

sealed class ComponentPreparationException(string message, Exception inner) : Exception(message, inner);

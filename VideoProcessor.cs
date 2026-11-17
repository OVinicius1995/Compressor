using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

sealed class Settings
{
    public static Settings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Configuração vazia.");
        settings.Resolve(Path.GetDirectoryName(path)!);
        return settings;
    }
    public void Save(string path)
    {
        // Propriedades calculadas não são gravadas; o arquivo continua editável manualmente.
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
    public string ToolsDirectory { get; set; } = "";
    public string InputDirectory { get; set; } = "Entrada";
    public string OutputDirectory { get; set; } = "Prontos para Jira";
    public string StateDirectory { get; set; } = ".state";
    public long TargetBytes { get; set; } = 9_500_000;
    public long LimitBytes { get; set; } = 10_000_000;
    public double PollSeconds { get; set; } = 2;
    public double StableSeconds { get; set; } = 10;
    public string[] Extensions { get; set; } = [".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".m4v"];
    [System.Text.Json.Serialization.JsonIgnore]
    public string FFmpeg => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    [System.Text.Json.Serialization.JsonIgnore]
    public string FFprobe => Path.Combine(ToolsDirectory, "ffprobe.exe");
    public void Resolve(string root)
    {
        ToolsDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ToolsDirectory, root));
        InputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(InputDirectory, root));
        OutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(OutputDirectory, root));
        StateDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(StateDirectory, root));
    }
    public void Validate()
    {
        if (!File.Exists(FFmpeg) || !File.Exists(FFprobe)) throw new InvalidDataException("FFmpeg/FFprobe não encontrado. Ajuste ToolsDirectory.");
        if (TargetBytes <= 0 || TargetBytes >= LimitBytes || LimitBytes > 10_000_000) throw new InvalidDataException("Use 0 < TargetBytes < LimitBytes <= 10000000.");
        if (!double.IsFinite(PollSeconds) || !double.IsFinite(StableSeconds) || PollSeconds <= 0 || StableSeconds <= 0) throw new InvalidDataException("Os intervalos devem ser positivos e finitos.");
        if (new[] { InputDirectory, OutputDirectory, StateDirectory }.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3) throw new InvalidDataException("Entrada, saída e estado devem ser pastas diferentes.");
        if (Extensions.Length == 0 || Extensions.Any(e => !e.StartsWith('.') || e.Contains('/') || e.Contains('\\'))) throw new InvalidDataException("Extensões inválidas.");
    }
}

sealed record Receipt(string OutputName, string OutputHash, string? PreviousOutputName = null);

sealed class VideoProcessor(Settings settings, StatusStore status)
{
    public async Task Process(string input, FileStream source, CancellationToken token)
    {
        source.Position = 0;
        status.Update(input, "hashing", 0, "Verificando conteúdo e duplicatas.");
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(source, token)).ToLowerInvariant();
        string destinationKey = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(settings.OutputDirectory.ToUpperInvariant())))[..16];
        string receiptPath = Path.Combine(settings.StateDirectory, hash + "-" + destinationKey + ".json");
        string legacyReceipt = Path.Combine(settings.StateDirectory, hash + ".json");
        string readReceipt = File.Exists(receiptPath) ? receiptPath : legacyReceipt;
        bool copy = source.Length <= settings.LimitBytes;
        string name = "";
        if (File.Exists(readReceipt))
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(readReceipt, token)) ?? throw new InvalidDataException("Registro inválido.");
            ValidateOutputName(receipt.OutputName);
            // Retoma uma renomeação interrompida depois de salvar o registro ou mover o arquivo.
            if (receipt.PreviousOutputName is not null)
            {
                ValidateOutputName(receipt.PreviousOutputName);
                string pendingOutput = Path.Combine(settings.OutputDirectory, receipt.OutputName);
                string oldOutput = Path.Combine(settings.OutputDirectory, receipt.PreviousOutputName);
                if (!File.Exists(pendingOutput) && File.Exists(oldOutput))
                {
                    await ValidateExisting(oldOutput, receipt.OutputHash, token);
                    File.Move(oldOutput, pendingOutput);
                }
                if (File.Exists(pendingOutput)) await ValidateExisting(pendingOutput, receipt.OutputHash, token);
                receipt = receipt with { PreviousOutputName = null };
                await SaveReceipt(receiptPath, receipt, token);
            }
            string previous = Path.Combine(settings.OutputDirectory, receipt.OutputName);
            if (File.Exists(previous))
            {
                await ValidateExisting(previous, receipt.OutputHash, token);
                if (IsLegacyName(receipt.OutputName, hash))
                {
                    string friendlyName = AvailableName(input, copy);
                    var migration = new Receipt(friendlyName, receipt.OutputHash, receipt.OutputName);
                    // Primeiro registra os dois nomes. Uma queda entre estas etapas é recuperável.
                    await SaveReceipt(receiptPath, migration, token);
                    token.ThrowIfCancellationRequested();
                    string renamed = Path.Combine(settings.OutputDirectory, friendlyName);
                    File.Move(previous, renamed); // Não sobrescreve em caso de colisão.
                    await SaveReceipt(receiptPath, migration with { PreviousOutputName = null }, token);
                    previous = renamed;
                    receipt = migration with { PreviousOutputName = null };
                }
                Console.WriteLine($"Já pronto: {Path.GetFileName(input)} → {receipt.OutputName}");
                status.Update(input, "skipped", 100, "Conteúdo já processado; nome do resultado conferido.", previous);
                return;
            }
            // Registro antigo sem resultado: gera novamente usando o nome amigável.
            if (!IsLegacyName(receipt.OutputName, hash)) name = receipt.OutputName;
        }
        if (name.Length == 0) name = AvailableName(input, copy);
        string output = Path.Combine(settings.OutputDirectory, name);
        if (File.Exists(output)) throw new IOException("Resultado sem registro confiável; revise antes de removê-lo.");
        status.Update(input, "analyzing", 0, "Analisando o vídeo.");
        double duration = await Probe(input, token);
        string work = Path.Combine(settings.OutputDirectory, ".work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string temporary = Path.Combine(work, "result" + Path.GetExtension(output));
        try
        {
            Console.WriteLine($"Processando: {Path.GetFileName(input)} ({source.Length} bytes)");
            if (copy)
            {
                status.Update(input, "copying", 0, "Copiando sem recodificar.");
                source.Position = 0;
                await using var destination = File.Create(temporary);
                byte[] buffer = new byte[1024 * 1024];
                int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, count), token);
                    status.Update(input, "copying", source.Position * 99.0 / source.Length, "Copiando sem recodificar.");
                }
            }
            else
            {
                double bitrate = Math.Floor(settings.TargetBytes * 8.0 / duration * .98);
                bool success = false;
                for (int attempt = 1; attempt <= 3 && bitrate >= 50_000; attempt++, bitrate = Math.Floor(bitrate * .90))
                {
                    status.Update(input, "compressing", 0, $"Tentativa {attempt}/3 · análise (passagem 1/2).");
                    string[] common = ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-i", input,
                        "-map", "0:v:0", "-an", "-c:v", "libx264", "-preset", "medium", "-b:v", bitrate.ToString("0", CultureInfo.InvariantCulture),
                        "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", "-pix_fmt", "yuv420p", "-passlogfile", Path.Combine(work, "pass")];
                    await Encode(token, input, duration, attempt, 1, [.. common, "-stats_period", "0.2", "-progress", "pipe:1", "-pass", "1", "-f", "null", "NUL"]);
                    await Encode(token, input, duration, attempt, 2, [.. common, "-stats_period", "0.2", "-progress", "pipe:1", "-pass", "2", "-movflags", "+faststart", temporary]);
                    if (new FileInfo(temporary).Length is > 0 && new FileInfo(temporary).Length <= settings.LimitBytes) { success = true; break; }
                }
                if (!success) throw new InvalidDataException("Não foi possível atingir o limite; divida o vídeo em partes.");
            }
            long size = new FileInfo(temporary).Length;
            status.Update(input, "validating", 99, "Validando limite e integridade do resultado.");
            if (size <= 0 || size > settings.LimitBytes) throw new InvalidDataException("Resultado excede o limite ou está vazio.");
            await Probe(temporary, token);
            string outputHash;
            using (var result = File.OpenRead(temporary)) outputHash = Convert.ToHexString(await SHA256.HashDataAsync(result, token));
            // Persiste antes da publicação para recuperar uma interrupção entre os movimentos.
            await SaveReceipt(receiptPath, new Receipt(name, outputHash), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, output);
            status.Update(input, "completed", 100, copy ? $"Copiado: {size} bytes." : $"Comprimido: {size} bytes.", output);
            Console.WriteLine($"Pronto: {Path.GetFileName(input)} → {name} ({size} bytes)");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsLegacyName(string name, string sourceHash) =>
        Path.GetFileNameWithoutExtension(name).Equals(sourceHash, StringComparison.OrdinalIgnoreCase);

    private static void ValidateOutputName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or "..")
            throw new InvalidDataException("Nome inválido no registro.");
    }

    private async Task ValidateExisting(string path, string expectedHash, CancellationToken token)
    {
        using var existing = File.OpenRead(path);
        if (existing.Length <= 0 || existing.Length > settings.LimitBytes ||
            !Convert.ToHexString(await SHA256.HashDataAsync(existing, token)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Resultado existente foi alterado. Revise e remova-o para reprocessar.");
    }

    private static async Task SaveReceipt(string path, Receipt receipt, CancellationToken token)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(receipt), token);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string AvailableName(string input, bool copy)
    {
        string stem = Path.GetFileNameWithoutExtension(input);
        if (stem.Length > 120) stem = stem[..120];
        string extension = copy ? Path.GetExtension(input).ToLowerInvariant() : ".mp4";
        for (int i = 1; ; i++)
        {
            string name = stem + "_compressed" + (i == 1 ? "" : "_" + i) + extension;
            if (!File.Exists(Path.Combine(settings.OutputDirectory, name))) return name;
        }
    }

    private Task<string> Encode(CancellationToken token, string input, double duration, int attempt, int pass, string[] arguments)
    {
        status.Update(input, "compressing", pass == 1 ? 0 : 49.5, $"Tentativa {attempt}/3 · passagem {pass}/2.");
        return RunProgress(settings.FFmpeg, token, line =>
        {
            if (line.StartsWith("out_time_us=") && double.TryParse(line.AsSpan(12), CultureInfo.InvariantCulture, out double microseconds))
            {
                double fraction = Math.Clamp(microseconds / 1_000_000 / duration, 0, 1);
                status.Update(input, "compressing", (pass - 1 + fraction) * 49.5, $"Tentativa {attempt}/3 · passagem {pass}/2.");
            }
        }, arguments);
    }

    private async Task<double> Probe(string path, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await Run(settings.FFprobe, token, "-v", "error", "-show_entries", "format=duration:stream=codec_type", "-of", "json", path));
        var root = doc.RootElement;
        if (!root.GetProperty("streams").EnumerateArray().Any(s => s.GetProperty("codec_type").GetString() == "video")) throw new InvalidDataException("Arquivo sem vídeo.");
        double duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("Duração inválida.");
        return duration;
    }

    private static Task<string> Run(string executable, CancellationToken token, params string[] arguments) => RunProgress(executable, token, null, arguments);

    private static async Task<string> RunProgress(string executable, CancellationToken token, Action<string>? progress, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("Não foi possível iniciar FFmpeg/FFprobe.");
        async Task<string> ReadOutput()
        {
            var text = new System.Text.StringBuilder();
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync()) is not null)
            {
                if (progress is null) text.AppendLine(line);
                else progress(line);
            }
            return text.ToString();
        }
        var stdout = ReadOutput();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            // Falha ao persistir progresso também deve encerrar o subprocesso, sem deadlock no pipe.
            Task finished = process.WaitForExitAsync(token);
            if (await Task.WhenAny(stdout, finished) == stdout && stdout.IsFaulted) await stdout;
            await finished;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0) throw new IOException($"{Path.GetFileName(executable)} falhou: {await stderr}");
        return await stdout;
    }
}

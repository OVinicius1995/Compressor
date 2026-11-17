sealed class MonitorService(Settings settings, string configPath)
{
    public StatusStore Status { get; } = new(configPath);
    public async Task<int> Run(bool once, string? manual, CancellationToken token)
    {
        Console.WriteLine($"Iniciando monitoramento: {settings.InputDirectory}");
        settings.Validate();
        Directory.CreateDirectory(settings.InputDirectory);
        Directory.CreateDirectory(settings.OutputDirectory);
        Directory.CreateDirectory(settings.StateDirectory);
        // Também protege status/histórico caso duas configurações usem estados diferentes.
        using var configLock = new FileStream(configPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var instance = new FileStream(Path.Combine(settings.StateDirectory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var processor = new VideoProcessor(settings, Status);
        var observations = new Dictionary<string, ((long, DateTime) Stamp, DateTime Since)>(StringComparer.OrdinalIgnoreCase);
        var completed = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int exitCode = 0;
        Status.SetRunning(true);
        Console.WriteLine("Monitor iniciado.");
        using var discoveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        // A descoberta continua durante o FFmpeg para o widget enxergar novas chegadas na fila.
        async Task Discover()
        {
            if (manual is not null) return;
            while (true)
            {
                foreach (string path in Directory.GetFiles(settings.InputDirectory).Where(p => settings.Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))) Status.Discover(path);
                Status.Heartbeat();
                await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), discoveryCancellation.Token);
            }
        }
        Task discovery = Discover();
        try
        {
            do
            {
                token.ThrowIfCancellationRequested();
                if (discovery.IsFaulted) await discovery;
                string[] paths = manual is not null ? [Path.GetFullPath(manual)] : Directory.GetFiles(settings.InputDirectory)
                    .Where(p => settings.Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).OrderBy(p => p).ToArray();
                foreach (string path in known.Except(paths, StringComparer.OrdinalIgnoreCase).ToArray())
                {
                    Status.Update(path, "removed", 0, "Arquivo removido da entrada.");
                    known.Remove(path); completed.Remove(path); observations.Remove(path);
                }
                // Registra a fila inteira antes de iniciar o primeiro trabalho.
                foreach (string path in paths)
                    if (known.Add(path)) Status.Discover(path);
                bool pending = false;
                foreach (string path in paths)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(path);
                        var stamp = (info.Length, info.LastWriteTimeUtc);
                        if (completed.TryGetValue(path, out var done) && done == stamp) continue;
                        if (!observations.TryGetValue(path, out var observation) || observation.Stamp != stamp)
                        {
                            observations[path] = (stamp, DateTime.UtcNow);
                            Status.Update(path, "waiting", 0, "Aguardando a gravação/cópia terminar.");
                            pending = true; continue;
                        }
                        if ((DateTime.UtcNow - observation.Since).TotalSeconds < settings.StableSeconds) { pending = true; continue; }
                        FileStream source;
                        try { source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
                        catch (IOException) { pending = true; continue; }
                        using (source)
                        {
                            info.Refresh();
                            if ((info.Length, info.LastWriteTimeUtc) != stamp) { observations.Remove(path); pending = true; continue; }
                            await processor.Process(path, source, token);
                        }
                        completed[path] = stamp;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Status.Update(path, "failed", 0, ex.Message);
                        Console.Error.WriteLine($"Erro em {Path.GetFileName(path)}: {ex.Message}");
                        // Sem repetição infinita: alterar o arquivo ou reiniciar permite tentar de novo.
                        var failed = new FileInfo(path);
                        if (failed.Exists) completed[path] = (failed.Length, failed.LastWriteTimeUtc);
                        exitCode = 1;
                    }
                }
                if (once && !pending) break;
                await Task.Delay(TimeSpan.FromSeconds(settings.PollSeconds), token);
            } while (true);
            return exitCode;
        }
        finally
        {
            discoveryCancellation.Cancel();
            try { await discovery; }
            catch (OperationCanceledException) { }
            finally { Status.SetRunning(false); }
        }
    }
}

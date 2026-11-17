using System.Text.Json;

// Contrato público simples para leitores externos; nomes e estados estáveis em inglês.
sealed record JobStatus(string SourcePath, string FileName, string State, double Percent,
    string Message, string? OutputPath, DateTimeOffset UpdatedAt);
sealed record StatusSnapshot(int SchemaVersion, DateTimeOffset UpdatedAt, bool Running, int ProcessId,
    int Waiting, int Compressing, int Processing, int Completed, int Failed, IReadOnlyList<JobStatus> Jobs);

sealed class StatusStore
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly Dictionary<string, JobStatus> jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string snapshotPath;
    private readonly string historyPath;
    private readonly object gate = new();
    private DateTimeOffset lastWrite;
    private bool running;
    private readonly System.Collections.Concurrent.ConcurrentQueue<JobStatus> completions = new();
    public bool TryTakeCompletion(out JobStatus? job) => completions.TryDequeue(out job);
    public StatusStore(string configPath)
    {
        string root = Path.GetDirectoryName(configPath)!;
        snapshotPath = Path.Combine(root, "status.json");
        historyPath = Path.Combine(root, "history.jsonl");
    }
    public static bool IsProcessing(string state) => state is "hashing" or "analyzing" or "compressing" or "copying" or "validating";
    public void Discover(string source)
    {
        lock (gate)
            if (!jobs.ContainsKey(source)) Update(source, "waiting", 0, "Aguardando estabilidade e disponibilidade.");
    }
    public void Heartbeat()
    {
        lock (gate)
            if (DateTimeOffset.UtcNow - lastWrite >= TimeSpan.FromSeconds(1)) Write();
    }
    public void SetRunning(bool value)
    {
        lock (gate)
        {
            running = value;
            if (!value)
                foreach (var job in jobs.Values.Where(j => IsProcessing(j.State) || j.State == "waiting").ToArray())
                    Update(job.SourcePath, "canceled", job.Percent, "Monitoramento interrompido; original preservado.");
            Write();
        }
    }
    public void Update(string source, string state, double percent, string message, string? output = null)
    {
        lock (gate)
        {
            jobs.TryGetValue(source, out var previous);
            bool transition = previous?.State != state || previous.OutputPath != output || previous.Message != message;
            var job = new JobStatus(source, Path.GetFileName(source), state, Math.Round(Math.Clamp(percent, 0, 100), 1), message, output, DateTimeOffset.UtcNow);
            jobs[source] = job;
            if (transition)
                File.AppendAllText(historyPath, JsonSerializer.Serialize(job, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + Environment.NewLine);
            if (transition || DateTimeOffset.UtcNow - lastWrite >= TimeSpan.FromMilliseconds(500)) Write();
            if (state == "completed" && previous?.State != "completed") completions.Enqueue(job);
        }
    }
    public StatusSnapshot Snapshot()
    {
        lock (gate)
        {
            var list = jobs.Values.OrderByDescending(j => j.UpdatedAt).ToArray();
            return new(1, DateTimeOffset.UtcNow, running, Environment.ProcessId,
                list.Count(j => j.State == "waiting"), list.Count(j => j.State == "compressing"),
                list.Count(j => IsProcessing(j.State)), list.Count(j => j.State is "completed" or "skipped"),
                list.Count(j => j.State == "failed"), list);
        }
    }
    private void Write()
    {
        string temporary = snapshotPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Snapshot(), Json));
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(temporary, snapshotPath, overwrite: true); break; }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException) { Thread.Sleep(20); }
        }
        lastWrite = DateTimeOffset.UtcNow;
    }
}

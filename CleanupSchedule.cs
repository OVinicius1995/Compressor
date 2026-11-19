using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

internal sealed record CleanupPlan(bool Enabled, TimeOnly Time, bool Originals, bool Results, bool Permanent,
    string Input, string Output, string State, DateOnly? LastRun = null, string? Outcome = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? OnDate = null)
{
    internal string When => OnDate is { } date ? $"em {date:dd/MM/yyyy} às {Time:HH:mm}" : $"todos os dias às {Time:HH:mm}";
}

internal static class CleanupSchedule
{
    private static string PlanPath(string config) => config + ".cleanup.json";
    internal static string DecisionPath(string config) => Path.Combine(Path.GetDirectoryName(config)!, "decisoes-de-exclusao.md");
    internal static CleanupPlan? Load(string config) => File.Exists(PlanPath(config))
        ? JsonSerializer.Deserialize<CleanupPlan>(File.ReadAllText(PlanPath(config))) ?? throw new InvalidDataException("Agendamento inválido.") : null;

    private static void Write(string config, CleanupPlan plan)
    {
        string path = PlanPath(config);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }

    internal static async Task<FileStream> LockAsync(string state, CancellationToken token)
    {
        string path = Path.Combine(state, "processing.lock");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33) { await Task.Delay(200, token).ConfigureAwait(false); }
        }
    }

    internal static void Save(string config, CleanupPlan plan)
    {
        using var edit = new FileStream(PlanPath(config) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var previous = Load(config);
        if (plan.Enabled && !plan.Originals && !plan.Results) throw new InvalidDataException("Selecione pelo menos um tipo de vídeo.");
        if (plan.Enabled && plan.OnDate is { } date && date.ToDateTime(plan.Time) <= DateTime.Now)
            throw new InvalidDataException("Escolha uma data e um horário futuros para a exclusão.");
        if (!plan.Enabled && previous?.Enabled != true) return;
        // A tarefa lê somente o plano confirmado. Durante a edição, o bloqueio também protege a execução.
        RegisterTask(config, plan);
        try
        {
            if (plan.Enabled && plan.Permanent) RecordDecision(config, plan);
            Write(config, plan with { LastRun = plan.OnDate is null ? previous?.LastRun : null, Outcome = previous?.Outcome });
        }
        catch
        {
            RegisterTask(config, previous ?? plan with { Enabled = false });
            throw;
        }
    }

    private static string DecisionId(CleanupPlan plan) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(plan with { LastRun = null, Outcome = null })));

    internal static void RecordDecision(string config, CleanupPlan plan)
    {
        string decision = $"\n## Confirmação em {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n\n" +
            $"Usuário: {WindowsIdentity.GetCurrent().Name}\n\nExclusão permanente {plan.When}. " +
            "O usuário confirmou que os arquivos não passarão pela Lixeira e a exclusão não poderá ser desfeita.\n\n" +
            $"- Originais: {(plan.Originals ? plan.Input : "desmarcado")}\n- Resultados: {(plan.Results ? plan.Output : "desmarcado")}\n\n" +
            $"Registro: {DecisionId(plan)}\n";
        File.AppendAllText(DecisionPath(config), decision);
    }

    private static void RegisterTask(string config, CleanupPlan plan)
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
        scheduler.Connect();
        dynamic folder = scheduler.GetFolder("\\");
        string name = "JiraVideoCompressor-Limpeza-" + VideoProcessor.DestinationKey(config) + "-" + WindowsIdentity.GetCurrent().User!.Value;
        if (!plan.Enabled)
        {
            try { folder.DeleteTask(name, 0); }
            catch (System.Runtime.InteropServices.COMException ex) when ((ex.HResult & 0xffff) == 2) { }
            return;
        }
        dynamic task = CreateTask(scheduler, config, plan);
        folder.RegisterTaskDefinition(name, task, 6, task.Principal.UserId, null, 3);
    }

    internal static dynamic CreateTask(dynamic scheduler, string config, CleanupPlan plan)
    {
        dynamic task = scheduler.NewTask(0);
        task.RegistrationInfo.Description = $"Limpeza de vídeos {plan.When}.";
        task.Principal.UserId = WindowsIdentity.GetCurrent().Name;
        task.Principal.LogonType = 3; // InteractiveToken: Lixeira e permissões do próprio usuário.
        task.Settings.StartWhenAvailable = true;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        dynamic trigger = task.Triggers.Create(plan.OnDate.HasValue ? 1 : 2); // TIME ou DAILY
        DateTime next = (plan.OnDate ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(plan.Time);
        if (plan.OnDate is null)
        {
            trigger.DaysInterval = 1;
            if (next <= DateTime.Now) next = next.AddDays(1);
        }
        else
        {
            trigger.EndBoundary = next.Date.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            task.Settings.DeleteExpiredTaskAfter = "PT0S";
        }
        trigger.StartBoundary = next.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        dynamic action = task.Actions.Create(0);
        action.Path = Path.Combine(AppContext.BaseDirectory, "JiraVideoCompressor.exe");
        action.Arguments = $"--scheduled-cleanup --config \"{config}\"";
        return task;
    }

    internal static int Run(string config)
    {
        using var edit = new FileStream(PlanPath(config) + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var plan = Load(config);
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        if (plan is not { Enabled: true } || plan.LastRun == today) return 0;
        if (plan.OnDate is { } date && (date != today || now < date.ToDateTime(plan.Time))) return 0;
        try
        {
            if (plan.Permanent && (!File.Exists(DecisionPath(config)) || !File.ReadLines(DecisionPath(config)).Contains("Registro: " + DecisionId(plan))))
                throw new InvalidDataException("Confirmação da exclusão permanente ausente. Abra o agendador e confirme as opções novamente.");
            int count = 0;
            if (Directory.Exists(plan.State))
            {
                using var processing = LockAsync(plan.State, CancellationToken.None).GetAwaiter().GetResult();
                if (plan.OnDate is { } scheduled && scheduled != DateOnly.FromDateTime(DateTime.Now)) return 0;
                count = Clean(config, plan);
            }
            Write(config, plan with { Enabled = plan.OnDate is null, LastRun = today, Outcome = $"{today:dd/MM/yyyy}: {count} arquivo(s) excluído(s)." });
            return 0;
        }
        catch (Exception ex)
        {
            Write(config, plan with { Outcome = $"{today:dd/MM/yyyy}: {ex.Message}" });
            throw;
        }
    }

    private static int Clean(string config, CleanupPlan plan)
    {
        string history = Path.Combine(Path.GetDirectoryName(config)!, "history.jsonl");
        if (!File.Exists(history)) return 0;
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        using (var reader = new StreamReader(new FileStream(history, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
        {
            while (reader.ReadLine() is { } line)
            {
                var job = JsonSerializer.Deserialize<JobStatus>(line, json);
                if (job?.State is not ("completed" or "skipped") || job.OutputPath is null || !InFolder(job.OutputPath, plan.Output)) continue;
                if (!sources.TryGetValue(job.OutputPath, out var paths)) sources[job.OutputPath] = paths = new(StringComparer.OrdinalIgnoreCase);
                if (InFolder(job.SourcePath, plan.Input)) paths.Add(job.SourcePath);
            }
        }
        int count = 0;
        string suffix = "-" + VideoProcessor.DestinationKey(plan.Output);
        foreach (string path in Directory.EnumerateFiles(plan.State, "*.json"))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) stem = stem[..^suffix.Length];
            else if (File.Exists(Path.Combine(plan.State, stem + suffix + ".json"))) continue;
            if (stem.Length != 64 || !stem.All(Uri.IsHexDigit)) continue;
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path));
            if (receipt is null || receipt.PreviousOutputName != null || receipt.OutputName != Path.GetFileName(receipt.OutputName)) continue;
            string output = Path.Combine(plan.Output, receipt.OutputName);
            if (!sources.TryGetValue(output, out var originals)) continue;
            using var result = Verified(output, receipt.OutputHash);
            if (result is null && !(receipt.Cleaned && plan.Results)) continue;
            if (plan.Originals)
                foreach (string original in originals)
                {
                    using var source = Verified(original, stem);
                    if (source is null) continue;
                    Remove(original, plan.Permanent);
                    count++;
                }
            if (plan.Results && result is not null)
            {
                // Grava no registro específico deste destino, inclusive para recibos antigos.
                string scoped = Path.Combine(plan.State, stem + suffix + ".json");
                VideoProcessor.SaveReceipt(scoped, receipt with { Cleaned = true }, CancellationToken.None).GetAwaiter().GetResult();
                try { Remove(output, plan.Permanent); }
                catch
                {
                    VideoProcessor.SaveReceipt(scoped, receipt, CancellationToken.None).GetAwaiter().GetResult();
                    throw;
                }
                count++;
            }
        }
        return count;
    }

    private static bool InFolder(string path, string directory) => Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.OrdinalIgnoreCase);

    private static FileStream? Verified(string path, string hash)
    {
        if (!File.Exists(path)) return null;
        var file = new FileInfo(path);
        if (((file.Attributes | file.Directory!.Attributes) & FileAttributes.ReparsePoint) != 0) return null;
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (FileNotFoundException) { return null; }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33) { return null; }
        try
        {
            if (Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return stream;
        }
        catch { stream.Dispose(); throw; }
        stream.Dispose();
        return null;
    }

    private static void Remove(string path, bool permanent)
    {
        if (permanent) File.Delete(path);
        else RecycleBin.Move(path);
    }
}

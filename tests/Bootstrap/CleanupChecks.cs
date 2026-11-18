using System.Security.Cryptography;
using System.Text.Json;

internal static class CleanupChecks
{
    internal static void Run(string testRoot)
    {
        foreach (var selection in new[] { (true, false), (false, true), (true, true) })
        {
            string root = Path.Combine(testRoot, $"cleanup-{selection.Item1}-{selection.Item2}");
            string config = Path.Combine(root, "appsettings.json");
            var settings = new Settings();
            settings.Resolve(root);
            Directory.CreateDirectory(settings.InputDirectory);
            Directory.CreateDirectory(settings.OutputDirectory);
            Directory.CreateDirectory(settings.StateDirectory);
            string source = Path.Combine(settings.InputDirectory, "original.mp4");
            string output = Path.Combine(settings.OutputDirectory, "original_compressed.mp4");
            string changed = Path.Combine(settings.InputDirectory, "alterado.mp4");
            string unrelated = Path.Combine(settings.OutputDirectory, "outro.mp4");
            File.WriteAllText(source, "original");
            File.WriteAllText(output, "compressed");
            File.WriteAllText(changed, "replacement");
            File.WriteAllText(unrelated, "unrelated");
            string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            string receipt = Path.Combine(settings.StateDirectory, Hash(source).ToLowerInvariant() + "-" + VideoProcessor.DestinationKey(settings.OutputDirectory) + ".json");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new Receipt(Path.GetFileName(output), Hash(output))));
            var history = new StatusStore(config);
            history.Update(source, "completed", 100, "Pronto", output);
            history.Update(changed, "skipped", 100, "Duplicata anterior", output);
            var plan = new CleanupPlan(true, new TimeOnly(2, 30), selection.Item1, selection.Item2, true, settings.InputDirectory, settings.OutputDirectory, settings.StateDirectory);
            if (plan.Originals && plan.Results)
            {
                dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
                scheduler.Connect();
                dynamic task = CleanupSchedule.CreateTask(scheduler, config, plan);
                if (task.Triggers.Item(1).DaysInterval != 1 || !((string)task.Triggers.Item(1).StartBoundary).EndsWith("T02:30:00") ||
                    (string)task.Actions.Item(1).Arguments != $"--scheduled-cleanup --config \"{config}\"")
                    throw new Exception("Tarefa diária ou comando agendado inválido.");
                var recycle = new RecycleProgress();
                if (recycle.PreDeleteItem(0, 0) >= 0 || recycle.PreDeleteItem(0x80, 0) < 0)
                    throw new Exception("Modo Lixeira permitiu exclusão definitiva.");
            }
            File.WriteAllText(config + ".cleanup.json", JsonSerializer.Serialize(plan with { Enabled = false }));
            CleanupSchedule.Run(config);
            if (!File.Exists(source) || !File.Exists(output)) throw new Exception("Limpeza desativada alterou arquivos.");
            File.WriteAllText(config + ".cleanup.json", JsonSerializer.Serialize(plan));
            try { CleanupSchedule.Run(config); throw new Exception("Exclusão permanente sem confirmação."); }
            catch (InvalidDataException) { }
            CleanupSchedule.RecordDecision(config, plan);
            if (!File.ReadAllText(CleanupSchedule.DecisionPath(config)).Contains("não poderá ser desfeita"))
                throw new Exception("Registro da decisão permanente incompleto.");
            CleanupSchedule.Run(config);
            if (File.Exists(source) == plan.Originals || File.Exists(output) == plan.Results || !File.Exists(changed) || !File.Exists(unrelated))
                throw new Exception("A limpeza não respeitou as categorias ou o conteúdo registrado.");
            if (plan.Results && !JsonSerializer.Deserialize<Receipt>(File.ReadAllText(receipt))!.Cleaned)
                throw new Exception("Exclusão do resultado não registrada.");
            if (plan.Results && !plan.Originals)
            {
                using var stream = File.OpenRead(source);
                new VideoProcessor(settings, history).Process(source, stream, CancellationToken.None).GetAwaiter().GetResult();
                if (history.Snapshot().Jobs.Single(job => job.SourcePath == source).State != "cleaned" || File.Exists(output))
                    throw new Exception("O monitor recriou um resultado excluído pelo usuário.");
            }
            File.WriteAllText(source, "original");
            File.WriteAllText(output, "compressed");
            CleanupSchedule.Run(config);
            if (!File.Exists(source) || !File.Exists(output)) throw new Exception("A limpeza repetiu no mesmo dia.");
        }
        Console.WriteLine("PASSOU: exclusão agendada de originais, resultados ou ambos, proteção de arquivos alterados e controle diário.");
    }
}

using System.Diagnostics;
using System.Text.Json;

sealed class MainForm : Form
{
    private readonly string configPath;
    private readonly TextBox input = new() { Dock = DockStyle.Fill };
    private readonly TextBox output = new() { Dock = DockStyle.Fill };
    private readonly TextBox tools = new() { Dock = DockStyle.Fill };
    private readonly Button start = new() { Text = "Salvar e iniciar", AutoSize = true };
    private readonly Button stop = new() { Text = "Parar", Enabled = false, AutoSize = true };
    private readonly Label summary = new() { AutoSize = true, Padding = new Padding(0, 12, 0, 8) };
    private readonly Label activity = new() { AutoSize = true, Text = "Parado. Escolha as pastas e clique em Salvar e iniciar." };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly NotifyIcon tray;
    private readonly Icon applicationIcon;
    private readonly Queue<JobStatus> notifications = new();
    private DateTimeOffset nextNotificationAt;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private readonly List<Control> configurationControls = [];
    private readonly Mutex desktopInstance;
    private bool ownsMutex;
    private bool exiting;
    private bool stopping;
    private bool preparingTools;
    private Task? worker;
    private CancellationTokenSource? cancellation;
    private MonitorService? service;
    private Settings settings = new();

    public MainForm(string configPath)
    {
        this.configPath = configPath;
        string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(configPath.ToUpperInvariant())));
        desktopInstance = new Mutex(false, "Local\\JiraVideoCompressor-" + id);
        try { ownsMutex = desktopInstance.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        Text = "Jira Video Compressor";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("JiraVideoCompressor.roller.ico")
            ?? throw new InvalidOperationException("Ícone da aplicação não encontrado."))
        using (var embeddedIcon = new Icon(iconStream)) applicationIcon = (Icon)embeddedIcon.Clone();
        Icon = applicationIcon;
        MinimumSize = new Size(820, 540);
        Size = new Size(1020, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 9 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++) layout.RowStyles.Add(new RowStyle(i == 7 ? SizeType.Percent : SizeType.AutoSize, i == 7 ? 100 : 0));
        layout.Controls.Add(new Label { Text = "Vídeos prontos para anexar no Jira", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 0, 0, 10) }, 0, 0);
        layout.Controls.Add(FolderRow("Pasta de entrada", input), 0, 1);
        layout.Controls.Add(FolderRow("Prontos para Jira", output), 0, 2);
        layout.Controls.Add(FolderRow("FFmpeg / FFprobe", tools), 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        var openOutput = new Button { Text = "Abrir resultados", AutoSize = true };
        var hide = new Button { Text = "Ocultar na bandeja", AutoSize = true };
        actions.Controls.AddRange([start, stop, openOutput, hide]);
        layout.Controls.Add(actions, 0, 4);
        layout.Controls.Add(summary, 0, 5);
        var current = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 0, 10) };
        current.Controls.Add(activity);
        current.Controls.Add(progress);
        progress.Height = 18;
        layout.Controls.Add(current, 0, 6);
        grid.Columns.Add("file", "Vídeo");
        grid.Columns.Add("state", "Estado");
        grid.Columns.Add("percent", "%");
        grid.Columns.Add("message", "Detalhes");
        grid.Columns[0].FillWeight = 28;
        grid.Columns[1].FillWeight = 17;
        grid.Columns[2].FillWeight = 8;
        grid.Columns[3].FillWeight = 47;
        layout.Controls.Add(grid, 0, 7);
        layout.Controls.Add(new Label { Text = "Fechar a janela mantém o monitor na bandeja. Para encerrar, use Sair no ícone.", AutoSize = true, Padding = new Padding(0, 10, 0, 0) }, 0, 8);
        Controls.Add(layout);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir painel", null, (_, _) => ShowPanel());
        menu.Items.Add("Iniciar monitoramento", null, async (_, _) => await StartMonitoring());
        menu.Items.Add("Parar monitoramento", null, async (_, _) => await StopMonitoring());
        menu.Items.Add("Abrir resultados", null, (_, _) => OpenResults());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, async (_, _) => await ExitApplication());
        tray = new NotifyIcon { Icon = applicationIcon, Text = "Jira Video Compressor · parado", ContextMenuStrip = menu, Visible = ownsMutex };
        tray.DoubleClick += (_, _) => ShowPanel();
        tray.BalloonTipClicked += (_, _) => ShowPanel();
        start.Click += async (_, _) => await StartMonitoring();
        stop.Click += async (_, _) => await StopMonitoring();
        openOutput.Click += (_, _) => OpenResults();
        hide.Click += (_, _) => Hide();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        timer.Tick += (_, _) => RefreshStatus();
        Shown += (_, _) =>
        {
            if (!ownsMutex)
            {
                MessageBox.Show(this, "O painel já está aberto. Acesse o ícone do Jira Video Compressor na bandeja do Windows.", Text);
                exiting = true; Close(); return;
            }
            try
            {
                if (File.Exists(configPath)) settings = Settings.Load(configPath);
                else
                {
                    string example = Path.Combine(Path.GetDirectoryName(configPath)!, "appsettings.example.json");
                    if (File.Exists(example)) settings = Settings.Load(example);
                    else settings.Resolve(Path.GetDirectoryName(configPath)!);
                }
                input.Text = settings.InputDirectory;
                output.Text = settings.OutputDirectory;
                tools.Text = settings.ToolsDirectory;
                RefreshStatus(); timer.Start();
            }
            catch (Exception ex) { ShowError(ex); }
        };
    }

    private Control FolderRow(string label, TextBox box)
    {
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 4, 0, 4) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var choose = new Button { Text = "Escolher…", AutoSize = true };
        choose.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = label, UseDescriptionForTitle = true };
            if (Directory.Exists(box.Text)) picker.SelectedPath = box.Text;
            if (picker.ShowDialog(this) == DialogResult.OK) box.Text = picker.SelectedPath;
        };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        row.Controls.Add(box, 1, 0);
        row.Controls.Add(choose, 2, 0);
        configurationControls.AddRange([box, choose]);
        return row;
    }

    private async Task StartMonitoring()
    {
        if (worker is { IsCompleted: false } || stopping || exiting || preparingTools) return;
        try
        {
            settings.InputDirectory = input.Text.Trim();
            settings.OutputDirectory = output.Text.Trim();
            settings.ToolsDirectory = tools.Text.Trim();
            if (new[] { input.Text, output.Text, tools.Text }.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Escolha as três pastas.");
            settings.Resolve(Path.GetDirectoryName(configPath)!);
            if (!File.Exists(settings.FFmpeg) || !File.Exists(settings.FFprobe))
            {
                preparingTools = true;
                cancellation?.Dispose();
                cancellation = new CancellationTokenSource();
                SetControls(true);
                var downloadProgress = new Progress<double>(percent =>
                {
                    if (IsDisposed || exiting) return;
                    activity.Text = $"Preparando FFmpeg automaticamente · {percent:F0}% · internet necessária na primeira vez";
                    progress.Value = (int)(Math.Clamp(percent, 0, 100) * 10);
                });
                activity.Text = "Consultando o fornecedor do FFmpeg para preparação automática…";
                worker = ToolBootstrapper.EnsureAsync(settings, configPath, downloadProgress, cancellation.Token);
                try { await worker; }
                finally { preparingTools = false; }
                if (cancellation.IsCancellationRequested || exiting) return;
            }
            settings.Validate();
            // Não muda a configuração enquanto outra execução CLI estiver utilizando-a.
            using (var configLock = new FileStream(configPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) settings.Save(configPath);
            CollectNotifications();
            service = new MonitorService(settings, configPath);
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            SetControls(true);
            activity.Text = "Iniciando monitoramento…";
            worker = Task.Run(() => service.Run(false, null, cancellation.Token));
            await ObserveWorker(worker);
        }
        catch (OperationCanceledException) { activity.Text = "Preparação interrompida. Clique em Salvar e iniciar para tentar novamente."; }
        catch (Exception ex) { ShowPanel(); ShowError(ex); SetControls(false); }
        finally { if (worker is not { IsCompleted: false }) SetControls(false); }
    }
    private async Task ObserveWorker(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowPanel(); ShowError(ex); }
        finally { SetControls(false); RefreshStatus(); }
    }
    private async Task StopMonitoring()
    {
        if (stopping) return;
        stopping = true;
        stop.Enabled = false;
        cancellation?.Cancel();
        try { if (worker is not null) await worker; }
        catch (OperationCanceledException) { }
        catch (Exception) { /* ObserveWorker apresenta a falha. */ }
        finally { stopping = false; SetControls(false); }
    }
    private async Task ExitApplication()
    {
        if (exiting) return;
        exiting = true;
        await StopMonitoring();
        Close();
    }
    private void SetControls(bool running)
    {
        if (IsDisposed) return;
        start.Enabled = !running && !exiting;
        stop.Enabled = running && !stopping;
        foreach (var control in configurationControls) control.Enabled = !running && !exiting;
    }
    private void RefreshStatus()
    {
        if (preparingTools) return;
        try
        {
            CollectNotifications();
            StatusSnapshot? snapshot = service?.Status.Snapshot();
            if (snapshot is null)
            {
                string path = Path.Combine(Path.GetDirectoryName(configPath)!, "status.json");
                if (File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    snapshot = JsonSerializer.Deserialize<StatusSnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            if (snapshot is null) { summary.Text = "Aguardando início do monitoramento."; return; }
            bool running = worker is { IsCompleted: false };
            summary.Text = $"Aguardando: {snapshot.Waiting}    Comprimindo: {snapshot.Compressing}    Em processamento: {snapshot.Processing}    Prontos: {snapshot.Completed}    Erros: {snapshot.Failed}";
            var active = snapshot.Jobs.FirstOrDefault(j => StatusStore.IsProcessing(j.State));
            activity.Text = running ? active is null ? "Monitorando a pasta de entrada…" : $"{active.FileName} · {active.Percent:F1}% · {active.Message}" : "Monitoramento parado. Histórico da última execução abaixo.";
            progress.Value = running && active is not null ? (int)(active.Percent * 10) : 0;
            tray.Text = running ? $"Jira · {snapshot.Waiting} aguardando · {(active is null ? "monitorando" : active.Percent.ToString("F0") + "%")}" : "Jira Video Compressor · parado";
            int scroll = grid.FirstDisplayedScrollingRowIndex;
            grid.Rows.Clear();
            foreach (var job in snapshot.Jobs)
                grid.Rows.Add(job.FileName, StateLabel(job.State), $"{job.Percent:F1}", job.Message);
            if (scroll >= 0 && scroll < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scroll;
        }
        catch (IOException) { /* Leitor tolera substituição atômica do snapshot. */ }
        catch (JsonException) { activity.Text = "Não foi possível ler o último status. Inicie o monitor para atualizá-lo."; }
    }
    private void CollectNotifications()
    {
        if (exiting || !ownsMutex) return;
        if (service is not null)
            while (service.Status.TryTakeCompletion(out var job)) notifications.Enqueue(job!);
        if (notifications.Count == 0 || DateTimeOffset.UtcNow < nextNotificationAt) return;
        var completed = notifications.Dequeue();
        string action = completed.Message.StartsWith("Copiado:", StringComparison.Ordinal) ? "Cópia concluída" : "Compressão concluída";
        tray.BalloonTipTitle = "Vídeo pronto para o Jira";
        tray.BalloonTipText = $"{action}: {completed.FileName}\nResultado: {Path.GetFileName(completed.OutputPath)}";
        tray.BalloonTipIcon = ToolTipIcon.None;
        tray.ShowBalloonTip(6000);
        nextNotificationAt = DateTimeOffset.UtcNow.AddSeconds(7);
    }
    private static string StateLabel(string state) => state switch
    {
        "waiting" => "Aguardando", "hashing" => "Verificando", "analyzing" => "Analisando",
        "compressing" => "Comprimindo", "copying" => "Copiando", "validating" => "Validando",
        "completed" => "Pronto", "skipped" => "Já pronto", "failed" => "Erro", "canceled" => "Interrompido", "removed" => "Removido", _ => state
    };
    private void ShowPanel() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OpenResults()
    {
        try
        {
            string path = Path.GetFullPath(output.Text, Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ShowError(Exception ex)
    {
        Console.Error.WriteLine(ex);
        MessageBox.Show(this, ex.Message, "Jira Video Compressor", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop(); timer.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
            applicationIcon.Dispose();
            cancellation?.Dispose();
            if (ownsMutex) { desktopInstance.ReleaseMutex(); ownsMutex = false; }
            desktopInstance.Dispose();
        }
        base.Dispose(disposing);
    }
}

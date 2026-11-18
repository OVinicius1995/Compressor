using System.Diagnostics;
using System.Text.Json;

sealed class MainForm : Form
{
    private readonly string configPath;
    private readonly TextBox input = new() { Dock = DockStyle.Fill };
    private readonly TextBox output = new() { Dock = DockStyle.Fill };
    private readonly Label componentStatus = new() { Text = "Preparando recursos de vídeo…", AutoSize = true, Padding = new Padding(0, 8, 0, 4) };
    private readonly Button retryPreparation = new ThemeButton { Text = "Tentar preparar novamente", Visible = false };
    private readonly CheckBox ignoreMkv = new ThemeCheckBox { Text = "Movavi: ignorar MKV provisório no monitor", Checked = true };
    private readonly ComboBox compressionMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 235 };
    private readonly ToolTip optionHelp = new();
    private readonly Button start = new ThemeButton { Text = "Salvar e iniciar", IsPrimary = true };
    private readonly Button stop = new ThemeButton { Text = "Parar", Enabled = false };
    private readonly Label summary = new() { AutoSize = true, Padding = new Padding(0, 12, 0, 8) };
    private readonly Label activity = new() { AutoSize = true, Text = "Parado. Escolha as pastas e clique em Salvar e iniciar." };
    private readonly ProgressBar progress = new ThemeProgress { Dock = DockStyle.Fill, Maximum = 1000 };
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
    private bool componentsReady;
    private bool startingMonitor;
    private Task? preparationTask;
    private CancellationTokenSource? preparationCancellation;
    private readonly ToolStripMenuItem trayStart;
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
        VisualTheme.Window(this);
        MinimumSize = new Size(860, 740);
        Size = new Size(1040, 820);
        StartPosition = FormStartPosition.CenterScreen;
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Padding = new Padding(24), ColumnCount = 1, RowCount = 12, MinimumSize = new Size(0, 760) };
        viewport.Layout += (_, _) =>
        {
            int height = Math.Max(viewport.ClientSize.Height, layout.MinimumSize.Height);
            if (layout.Height != height) layout.Height = height;
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 12; i++) layout.RowStyles.Add(new RowStyle(i == 9 ? SizeType.Percent : SizeType.AutoSize, i == 9 ? 100 : 0));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var brand = new PictureBox { Image = applicationIcon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(40, 40), Margin = new Padding(0, 0, 16, 0), AccessibleName = "Rolo compactador" };
        brand.Disposed += (_, _) => brand.Image?.Dispose();
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(new Label { Text = "Jira Video Compressor", AutoSize = true, Font = VisualTheme.Font(23, FontStyle.Bold), Margin = Padding.Empty }, 1, 0);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(new Label { Text = "Da gravação ao Jira, com os originais preservados.", ForeColor = VisualTheme.Support, AutoSize = true, Margin = new Padding(0, 0, 0, 24) }, 0, 1);
        layout.Controls.Add(FolderRow("Pasta de entrada", input), 0, 2);
        layout.Controls.Add(FolderRow("Prontos para Jira", output), 0, 3);
        componentStatus.ForeColor = VisualTheme.Support;
        layout.Controls.Add(componentStatus, 0, 4);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 8), Margin = Padding.Empty, WrapContents = true };
        compressionMode.Items.AddRange(["Rápido (1 passagem)", "Preciso (2 passagens)"]);
        compressionMode.SelectedIndex = 0;
        optionHelp.SetToolTip(ignoreMkv, "Ignora todos os arquivos .mkv no monitor. Se seu vídeo final for MKV, desmarque ou use o modo manual.");
        optionHelp.SetToolTip(compressionMode, "Rápido: uma passagem/veryfast. Preciso: duas passagens/medium. Só repete se ultrapassar o limite; a qualidade pode variar entre os perfis.");
        ignoreMkv.Margin = new Padding(0, 8, 24, 8);
        ignoreMkv.AccessibleDescription = "Ignora MKV provisório. Desmarque para processar vídeos finais nesse formato.";
        compressionMode.AccessibleName = "Perfil de compressão";
        compressionMode.BackColor = SystemInformation.HighContrast ? SystemColors.Window : VisualTheme.Surface;
        compressionMode.ForeColor = VisualTheme.Foreground;
        compressionMode.FlatStyle = FlatStyle.Flat;
        compressionMode.DrawMode = DrawMode.OwnerDrawFixed;
        compressionMode.ItemHeight = 28;
        compressionMode.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var brush = new SolidBrush(SystemInformation.HighContrast ? selected ? SystemColors.Highlight : SystemColors.Window : selected ? VisualTheme.Hover : VisualTheme.Surface);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, compressionMode.Items[e.Index]?.ToString() ?? "", e.Font ?? compressionMode.Font, e.Bounds,
                SystemInformation.HighContrast && selected ? SystemColors.HighlightText : VisualTheme.Foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        };
        options.Controls.AddRange([ignoreMkv, new Label { Text = "Compressão", AutoSize = true, Padding = new Padding(0, 8, 8, 0), ForeColor = VisualTheme.Support }, compressionMode]);
        configurationControls.AddRange([ignoreMkv, compressionMode]);
        layout.Controls.Add(options, 0, 5);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 16), Margin = Padding.Empty };
        var openOutput = new ThemeButton { Text = "Abrir resultados" };
        var hide = new ThemeButton { Text = "Ocultar na bandeja" };
        actions.Controls.AddRange([start, stop, openOutput, hide]);
        layout.Controls.Add(actions, 0, 6);
        var recovery = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        recovery.Controls.Add(retryPreparation);
        layout.Controls.Add(recovery, 0, 7);
        summary.Font = VisualTheme.Font(14, FontStyle.Bold);
        summary.AccessibleName = "Resumo da fila";
        layout.Controls.Add(summary, 0, 8);
        var current = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 0, 16), Margin = Padding.Empty };
        current.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        activity.ForeColor = VisualTheme.Support;
        activity.Dock = DockStyle.Top;
        activity.AutoEllipsis = true;
        activity.AutoSize = false;
        activity.Height = 24;
        activity.AccessibleName = "Atividade atual";
        current.Controls.Add(activity);
        current.Controls.Add(progress);
        progress.Height = 6;
        progress.AccessibleName = "Progresso do vídeo atual";
        // A tabela e o progresso compartilham a área flexível sem comprimir as configurações.
        var monitoring = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, MinimumSize = new Size(0, 150), Margin = Padding.Empty };
        monitoring.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        monitoring.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        monitoring.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        monitoring.Controls.Add(current, 0, 0);
        grid.Columns.Add("file", "Vídeo");
        grid.Columns.Add("state", "Estado");
        grid.Columns.Add("percent", "%");
        grid.Columns.Add("message", "Detalhes");
        grid.Columns[0].FillWeight = 28;
        grid.Columns[1].FillWeight = 17;
        grid.Columns[2].FillWeight = 8;
        grid.Columns[3].FillWeight = 47;
        VisualTheme.Grid(grid);
        grid.AccessibleName = "Fila e histórico dos vídeos";
        monitoring.Controls.Add(grid, 0, 1);
        layout.Controls.Add(monitoring, 0, 9);
        layout.Controls.Add(new Label { Text = "Vídeos sem áudio · Até 10 MB por arquivo · Um vídeo por vez", ForeColor = VisualTheme.Support, AutoSize = true, Margin = new Padding(0, 16, 0, 0) }, 0, 10);
        layout.Controls.Add(new Label { Text = "Fechar mantém o aplicativo na bandeja. Para encerrar, use Sair no ícone.", ForeColor = VisualTheme.Support, AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, 11);
        viewport.Controls.Add(layout);
        Controls.Add(viewport);

        var menu = new ContextMenuStrip();
        VisualTheme.Menu(menu);
        menu.Items.Add("Abrir painel", null, (_, _) => ShowPanel());
        trayStart = new ToolStripMenuItem("Iniciar monitoramento", null, async (_, _) => await StartMonitoring()) { Enabled = false };
        menu.Items.Add(trayStart);
        menu.Items.Add("Parar monitoramento", null, async (_, _) => await StopMonitoring());
        menu.Items.Add("Abrir resultados", null, (_, _) => OpenResults());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, async (_, _) => await ExitApplication());
        tray = new NotifyIcon { Icon = applicationIcon, Text = "Jira Video Compressor · parado", ContextMenuStrip = menu, Visible = ownsMutex };
        tray.DoubleClick += (_, _) => ShowPanel();
        tray.BalloonTipClicked += (_, _) => ShowPanel();
        start.Click += async (_, _) => await StartMonitoring();
        stop.Click += async (_, _) => await StopMonitoring();
        retryPreparation.Click += async (_, _) => await PrepareComponentsAsync();
        openOutput.Click += (_, _) => OpenResults();
        hide.Click += (_, _) => Hide();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, e) => { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        timer.Tick += (_, _) => RefreshStatus();
        start.Enabled = false;
        Shown += async (_, _) =>
        {
            if (!ownsMutex)
            {
                VisualTheme.Alert(this, "O painel já está aberto", "Acesse o ícone do Jira Video Compressor na bandeja do Windows.", applicationIcon);
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
                ignoreMkv.Checked = settings.IgnoreMkvInMonitor;
                compressionMode.SelectedIndex = settings.CompressionMode.Equals("precise", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                RefreshStatus(); timer.Start();
                await PrepareComponentsAsync();
            }
            catch (Exception ex)
            {
                componentStatus.Text = "O programa precisa ser preparado antes de iniciar.";
                ShowError(ex);
            }
        };
    }

    private Control FolderRow(string label, TextBox box)
    {
        var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 16) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var choose = new ThemeButton { Text = "Escolher…", AccessibleName = "Escolher " + label.ToLowerInvariant(), Margin = Padding.Empty };
        choose.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { Description = label, UseDescriptionForTitle = true };
            if (Directory.Exists(box.Text)) picker.SelectedPath = box.Text;
            if (picker.ShowDialog(this) == DialogResult.OK) box.Text = picker.SelectedPath;
        };
        var caption = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        row.Controls.Add(caption, 0, 0);
        row.SetColumnSpan(caption, 2);
        box.AccessibleName = label;
        row.Controls.Add(new ThemeField(box), 0, 1);
        row.Controls.Add(choose, 1, 1);
        configurationControls.AddRange([box, choose]);
        return row;
    }

    private async Task StartMonitoring()
    {
        if (worker is { IsCompleted: false } || stopping || exiting || preparingTools || startingMonitor) return;
        if (!componentsReady) { ShowPanel(); return; }
        startingMonitor = true;
        SetControls(true);
        try
        {
            settings.InputDirectory = input.Text.Trim();
            settings.OutputDirectory = output.Text.Trim();
            settings.IgnoreMkvInMonitor = ignoreMkv.Checked;
            settings.CompressionMode = compressionMode.SelectedIndex == 1 ? "precise" : "fast";
            if (new[] { input.Text, output.Text }.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Escolha a pasta de entrada e a pasta de resultados.");
            settings.Resolve(Path.GetDirectoryName(configPath)!);
            try { await ToolBootstrapper.ValidateReadyAsync(settings, CancellationToken.None); }
            catch (Exception ex)
            {
                MarkComponentsUnavailable();
                ShowError(new ComponentPreparationException(ToolBootstrapper.UserGuidance(ex), ex));
                return;
            }
            settings.Validate();
            // Não muda a configuração enquanto outra execução CLI estiver utilizando-a.
            using (var configLock = new FileStream(configPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) settings.Save(configPath);
            CollectNotifications();
            service = new MonitorService(settings, configPath);
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            startingMonitor = false;
            SetControls(true);
            activity.Text = "Iniciando monitoramento…";
            worker = Task.Run(() => service.Run(false, null, cancellation.Token));
            await ObserveWorker(worker);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowPanel(); ShowError(ex); SetControls(false); }
        finally { startingMonitor = false; if (worker is not { IsCompleted: false }) SetControls(false); }
    }
    private async Task PrepareComponentsAsync()
    {
        if (preparingTools || exiting || worker is { IsCompleted: false }) return;
        preparingTools = true;
        componentsReady = false;
        retryPreparation.Visible = false;
        componentStatus.Text = "Preparando recursos de vídeo. Aguarde…";
        componentStatus.ForeColor = VisualTheme.Support;
        preparationCancellation?.Dispose();
        preparationCancellation = new CancellationTokenSource();
        SetControls(false);
        var downloadProgress = new Progress<double>(percent =>
        {
            if (IsDisposed || exiting) return;
            componentStatus.Text = $"Preparando recursos de vídeo · {percent:F0}% · internet necessária na primeira vez";
            progress.Value = (int)(Math.Clamp(percent, 0, 100) * 10);
        });
        try
        {
            settings.Resolve(Path.GetDirectoryName(configPath)!);
            preparationTask = ToolBootstrapper.PrepareAsync(settings, configPath, downloadProgress, preparationCancellation.Token);
            await preparationTask;
            if (exiting) return;
            componentsReady = true;
            componentStatus.Text = "Recursos de vídeo prontos.";
            componentStatus.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : VisualTheme.Success;
            activity.Text = "Escolha as pastas e clique em Salvar e iniciar.";
        }
        catch (OperationCanceledException) when (preparationCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!exiting)
            {
                MarkComponentsUnavailable();
                ShowPanel();
                ShowError(ex);
            }
        }
        finally
        {
            preparingTools = false;
            if (!IsDisposed && !exiting) { progress.Value = 0; SetControls(false); }
        }
    }
    private void MarkComponentsUnavailable()
    {
        componentsReady = false;
        componentStatus.Text = "Preparação incompleta. O monitoramento está bloqueado.";
        componentStatus.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : VisualTheme.Warning;
        retryPreparation.Visible = true;
        SetControls(false);
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
        preparationCancellation?.Cancel();
        try { if (preparationTask is not null) await preparationTask; }
        catch (Exception) { /* A preparação apresenta a orientação quando a janela permanece aberta. */ }
        await StopMonitoring();
        Close();
    }
    private void SetControls(bool running)
    {
        if (IsDisposed) return;
        start.Enabled = !running && !exiting && componentsReady && !preparingTools && !startingMonitor;
        trayStart.Enabled = start.Enabled;
        retryPreparation.Enabled = !running && !exiting && !preparingTools;
        stop.Enabled = running && !stopping && !startingMonitor;
        foreach (var control in configurationControls) control.Enabled = !running && !exiting;
    }
    private void RefreshStatus()
    {
        if (preparingTools || startingMonitor) return;
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
            string? selectedFile = grid.CurrentRow?.Cells[0].Value?.ToString();
            int selectedColumn = grid.CurrentCell?.ColumnIndex ?? 0;
            grid.Rows.Clear();
            foreach (var job in snapshot.Jobs)
                grid.Rows.Add(job.FileName, StateLabel(job.State), $"{job.Percent:F1}", job.Message);
            if (selectedFile is not null)
                foreach (DataGridViewRow row in grid.Rows)
                    if (row.Cells[0].Value?.ToString() == selectedFile) { grid.CurrentCell = row.Cells[selectedColumn]; break; }
            if (scroll >= 0 && scroll < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scroll;
            optionHelp.SetToolTip(activity, activity.Text);
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
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Action<string>? ErrorPresenter { get; set; }
    private void ShowError(Exception ex)
    {
        Console.Error.WriteLine(ex);
        if (ErrorPresenter is { } present) { present(ex.Message); return; }
        VisualTheme.Alert(this, "Não foi possível continuar", ex.Message, applicationIcon);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop(); timer.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
            applicationIcon.Dispose();
            optionHelp.Dispose();
            cancellation?.Dispose();
            preparationCancellation?.Dispose();
            if (ownsMutex) { desktopInstance.ReleaseMutex(); ownsMutex = false; }
            desktopInstance.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class CleanupDialog : Form
{
    internal CleanupDialog(string config, Settings settings, Icon icon)
    {
        var saved = CleanupSchedule.Load(config);
        Text = "Agendar exclusão de vídeos";
        Icon = icon;
        VisualTheme.Window(this);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Padding = new Padding(20) };
        Controls.Add(panel);
        var enabled = new ThemeCheckBox { Text = "Ativar exclusão agendada", Checked = saved?.Enabled == true };
        var frequency = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 165, AccessibleName = "Frequência" };
        frequency.Items.AddRange(["Todos os dias", "Em uma data específica"]);
        frequency.SelectedIndex = saved?.OnDate is null ? 0 : 1;
        var date = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 120,
            AccessibleName = "Data da exclusão", Value = saved?.OnDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today };
        var time = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
            Width = 85, AccessibleName = "Horário local", Value = DateTime.Today.Add((saved?.Time ?? new TimeOnly(20, 0)).ToTimeSpan()) };
        var originals = new ThemeCheckBox { Text = "Excluir vídeos originais", Checked = saved?.Originals == true };
        var results = new ThemeCheckBox { Text = "Excluir resultados gerados pelo Compressor", Checked = saved?.Results == true };
        var permanent = new ThemeCheckBox { Text = "Excluir permanentemente (ignorar a Lixeira)", Checked = saved?.Permanent == true };
        var save = new ThemeButton { Text = "Salvar", IsPrimary = true };
        var cancel = new ThemeButton { Text = "Cancelar", DialogResult = DialogResult.Cancel };
        Label Note(string text) => new() { Text = text, AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = VisualTheme.Support, Margin = new Padding(3, 4, 3, 9) };
        panel.Controls.Add(enabled);
        panel.Controls.Add(Note("Escolha a frequência e o horário local. Sua conta deve estar conectada ao Windows."));
        var schedule = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        schedule.Controls.AddRange([frequency, date, time]);
        panel.Controls.Add(schedule);
        panel.Controls.Add(originals);
        panel.Controls.Add(Note(settings.InputDirectory));
        panel.Controls.Add(results);
        panel.Controls.Add(Note(settings.OutputDirectory));
        panel.Controls.Add(permanent);
        panel.Controls.Add(Note("Por padrão, os vídeos processados vão para a Lixeira. A exclusão permanente será registrada em " + CleanupSchedule.DecisionPath(config) + "."));
        if (saved?.Outcome is { } outcome) panel.Controls.Add(Note("Última execução: " + outcome));
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([save, cancel]);
        panel.Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
        void RefreshOptions()
        {
            frequency.Enabled = date.Enabled = time.Enabled = originals.Enabled = results.Enabled = permanent.Enabled = enabled.Checked;
            date.Visible = frequency.SelectedIndex == 1;
            save.Enabled = !enabled.Checked || originals.Checked || results.Checked;
        }
        frequency.SelectedIndexChanged += (_, _) => RefreshOptions();
        enabled.CheckedChanged += (_, _) => RefreshOptions();
        originals.CheckedChanged += (_, _) => RefreshOptions();
        results.CheckedChanged += (_, _) => RefreshOptions();
        RefreshOptions();
        save.Click += (_, _) =>
        {
            var plan = new CleanupPlan(enabled.Checked, new TimeOnly(time.Value.Hour, time.Value.Minute), originals.Checked, results.Checked,
                permanent.Checked, settings.InputDirectory, settings.OutputDirectory, settings.StateDirectory,
                OnDate: frequency.SelectedIndex == 1 ? DateOnly.FromDateTime(date.Value) : null);
            if (plan.Enabled && plan.OnDate is { } day && day.ToDateTime(plan.Time) <= DateTime.Now)
            {
                VisualTheme.Alert(this, "Confira a data da exclusão", "Escolha uma data e um horário futuros.", icon);
                return;
            }
            if (plan.Enabled && plan.Permanent &&
                MessageBox.Show(this, $"Os vídeos selecionados serão excluídos permanentemente {plan.When}. Eles não passarão pela Lixeira. Essa decisão não poderá ser desfeita para arquivos já excluídos. Confirmar?",
                    "Confirmar exclusão permanente", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try { CleanupSchedule.Save(config, plan); DialogResult = DialogResult.OK; }
            catch (Exception ex) { VisualTheme.Alert(this, "Não foi possível salvar o agendamento", ex.Message, icon); }
        };
    }
}

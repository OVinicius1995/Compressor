using System.Reflection;
using System.Text.Json;

internal static class UiSmoke
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var type = assembly.GetType("MainForm", throwOnError: true)!;
        string config = Path.GetFullPath(args[1]);
        string root = Path.GetDirectoryName(config)!;
        int result = 0;
        using (var form = (Form)Activator.CreateInstance(type, config)!)
        {
            object Field(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            object? Call(string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            form.Shown += async (_, _) =>
            {
                Task? start = null;
                try
                {
                    string input = Path.Combine(root, "ui-input-" + Environment.ProcessId);
                    string output = Path.Combine(root, "ui-output-" + Environment.ProcessId);
                    Directory.CreateDirectory(input);
                    File.Copy(Path.Combine(root, "Entrada", "renomeado.mp4"), Path.Combine(input, "tela.mp4"), true);
                    ((TextBox)Field("input")).Text = input;
                    ((TextBox)Field("output")).Text = output;
                    Assert(((NotifyIcon)Field("tray")).Visible, "Ícone da bandeja não foi ativado.");
                    Assert(ReferenceEquals(form.Icon, ((NotifyIcon)Field("tray")).Icon), "Janela e bandeja devem usar o mesmo ícone.");
                    using (var executableIcon = Icon.ExtractAssociatedIcon(Path.ChangeExtension(args[0], ".exe")))
                    using (var bitmap = executableIcon!.ToBitmap())
                    {
                        bool yellow = false;
                        for (int x = 0; x < bitmap.Width; x++)
                            for (int y = 0; y < bitmap.Height; y++)
                            {
                                var pixel = bitmap.GetPixel(x, y);
                                yellow |= pixel.R > 200 && pixel.G > 100 && pixel.G < 230 && pixel.B < 150;
                            }
                        Assert(yellow, "Executável sem o ícone amarelo do rolo compactador.");
                    }
                    start = (Task)Call("StartMonitoring")!;
                    var deadline = DateTime.UtcNow.AddSeconds(20);
                    while (!File.Exists(Path.Combine(output, "tela_compressed.mp4")))
                    {
                        if (DateTime.UtcNow > deadline) throw new Exception("Timeout ao iniciar pela janela.");
                        await Task.Delay(100);
                    }
                    Assert(!((TextBox)Field("input")).Enabled, "Configuração deveria ficar bloqueada durante o processamento.");
                    using (var saved = JsonDocument.Parse(File.ReadAllText(config)))
                        Assert(saved.RootElement.GetProperty("InputDirectory").GetString() == input && saved.RootElement.GetProperty("OutputDirectory").GetString() == output, "Seleção de pastas não foi salva.");
                    form.Hide();
                    Assert(!form.Visible && ((NotifyIcon)Field("tray")).Visible, "Ocultar removeu o acesso pela bandeja.");
                    Call("ShowPanel");
                    Assert(form.Visible, "Painel não reabriu.");
                    Call("RefreshStatus");
                    Assert(((NotifyIcon)Field("tray")).BalloonTipText.Contains("Cópia concluída: tela.mp4"), "Notificação não identificou a cópia concluída.");
                    Assert(((DataGridView)Field("grid")).Rows.Count > 0, "Janela sem status dos vídeos.");
                    using (var screenshot = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
                        screenshot.Save(Path.Combine(root, "ui-preview.png"));
                    }
                    await (Task)Call("StopMonitoring")!;
                    await start;
                    Assert(((TextBox)Field("input")).Enabled, "Parar não liberou as configurações.");
                    using (var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "status.json"))))
                        Assert(!snapshot.RootElement.GetProperty("running").GetBoolean(), "Parar não persistiu o status.");
                    File.Copy(Path.Combine(root, "Entrada", "grande.avi"), Path.Combine(input, "comprimir.avi"), true);
                    start = (Task)Call("StartMonitoring")!;
                    deadline = DateTime.UtcNow.AddSeconds(30);
                    while (!((NotifyIcon)Field("tray")).BalloonTipText.Contains("Compressão concluída: comprimir.avi"))
                    {
                        if (DateTime.UtcNow > deadline) throw new Exception("Notificação da compressão não foi emitida.");
                        await Task.Delay(100);
                    }
                    Assert(File.Exists(Path.Combine(output, "comprimir_compressed.mp4")), "Notificação emitida sem resultado liberado.");
                    await (Task)Call("StopMonitoring")!;
                    await start;
                    File.Copy(Path.Combine(root, "Entrada", "grande.avi"), Path.Combine(input, "cancelar.avi"), true);
                    using (var changed = File.OpenWrite(Path.Combine(input, "cancelar.avi"))) changed.SetLength(changed.Length + 1);
                    start = (Task)Call("StartMonitoring")!;
                    deadline = DateTime.UtcNow.AddSeconds(20);
                    bool compressing = false;
                    while (!compressing)
                    {
                        if (DateTime.UtcNow > deadline) throw new Exception("Timeout ao aguardar compressão para cancelar.");
                        using (var stream = new FileStream(Path.Combine(root, "status.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var state = JsonDocument.Parse(stream))
                            compressing = state.RootElement.GetProperty("compressing").GetInt32() == 1;
                        await Task.Delay(20);
                    }
                    await (Task)Call("StopMonitoring")!;
                    await start;
                    Assert(!File.Exists(Path.Combine(output, "cancelar_compressed.mp4")), "Cancelamento publicou resultado parcial.");
                    Call("RefreshStatus");
                    Assert(((NotifyIcon)Field("tray")).BalloonTipText.Contains("Compressão concluída: comprimir.avi"), "Duplicata/cancelamento gerou alerta indevido.");
                    Assert(File.Exists(Path.Combine(input, "cancelar.avi")), "Cancelamento removeu original.");
                    using (var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "status.json"))))
                        Assert(state.RootElement.GetProperty("jobs").EnumerateArray().Any(j => j.GetProperty("state").GetString() == "canceled"), "Cancelamento não foi persistido.");
                    Console.WriteLine("PASSOU: janela, início/parada, pastas salvas, bandeja, ocultar/reabrir, ícone do executável, alertas de cópia/compressão e cancelamento sem alerta indevido.");
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                finally { await (Task)Call("ExitApplication")!; }
            };
            Application.Run(form);
        }
        // Nova instância carrega a seleção feita na janela anterior.
        if (result == 0)
        {
            using var reopened = (Form)Activator.CreateInstance(type, config)!;
            reopened.Shown += async (_, _) =>
            {
                var box = (TextBox)type.GetField("input", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
                if (box.Text != Path.Combine(root, "ui-input-" + Environment.ProcessId)) { result = 1; Console.Error.WriteLine("Pastas não foram restauradas ao reabrir."); }
                await (Task)type.GetMethod("ExitApplication", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reopened, null)!;
            };
            Application.Run(reopened);
        }
        return result;
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}

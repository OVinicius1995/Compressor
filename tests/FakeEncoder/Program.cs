using System.Text.Json;

if (args.Contains("-version")) { Console.WriteLine("ffmpeg version controlled-test"); return; }
if (args.Contains("-encoders")) { Console.WriteLine(" V..... libx264 test encoder"); return; }

// Encoder controlado só para testar o limite e a recuperação; o FFprobe usado é real.
string log = Environment.GetEnvironmentVariable("COMPRESSOR_TEST_ENCODER_LOG") ?? throw new Exception("Log de teste não configurado.");
string fixture = Environment.GetEnvironmentVariable("COMPRESSOR_TEST_ENCODER_VIDEO") ?? throw new Exception("Vídeo de teste não configurado.");
File.AppendAllText(log, JsonSerializer.Serialize(args) + Environment.NewLine);
string output = args[^1];
if (output != "NUL")
{
    string counterFile = log + ".count";
    int count = File.Exists(counterFile) ? int.Parse(File.ReadAllText(counterFile)) : 0;
    File.WriteAllText(counterFile, (++count).ToString());
    File.Copy(fixture, output, overwrite: true);
    using var result = File.OpenWrite(output);
    bool alwaysOversize = Environment.GetEnvironmentVariable("COMPRESSOR_TEST_ALWAYS_OVERSIZE") == "1";
    result.SetLength(count == 1 || alwaysOversize ? 50_001 : 45_000);
}
Console.WriteLine("out_time_us=500000");
Console.WriteLine("progress=end");

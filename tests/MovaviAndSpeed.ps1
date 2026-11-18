param([string]$Configuration='Debug')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$personal=Get-Content (Join-Path $projectRoot 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$tools=if ([string]::IsNullOrWhiteSpace($personal.ToolsDirectory)) { Join-Path $projectRoot 'tools/ffmpeg/bin' } elseif ([IO.Path]::IsPathRooted($personal.ToolsDirectory)) { $personal.ToolsDirectory } else { Join-Path $projectRoot $personal.ToolsDirectory }
$ffmpeg=Join-Path $tools 'ffmpeg.exe'
$dotnet=Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
$dll=Join-Path $projectRoot "bin/$Configuration/net9.0-windows/JiraVideoCompressor.dll"
$root=Join-Path $projectRoot ('.test-artifacts/movavi-speed-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
function Assert($condition,[string]$message) { if (!$condition) { throw $message } }
function Config([string]$name,[string]$mode='fast',[bool]$ignore=$true,[string]$toolPath=$tools,[int]$attempts=3,[long]$target=9500000,[long]$limit=10000000) {
    $folder=Join-Path $root $name
    [IO.Directory]::CreateDirectory((Join-Path $folder 'Entrada')) | Out-Null
    $value=@{ ToolsDirectory=$toolPath; InputDirectory='Entrada'; OutputDirectory='Saida'; StateDirectory='state'; PollSeconds=0.1; StableSeconds=0.3; IgnoreMkvInMonitor=$ignore; CompressionMode=$mode; MaxCompressionAttempts=$attempts; TargetBytes=$target; LimitBytes=$limit }
    $path=Join-Path $folder 'appsettings.json'
    [IO.File]::WriteAllText($path,($value | ConvertTo-Json))
    return $path
}
function Start-App([string]$config,[string]$options='--once') {
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$dotnet
    $start.Arguments='"'+$dll+'" --config "'+$config+'" '+$options
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    return [Diagnostics.Process]::Start($start)
}
function Finish($process,[int]$expected=0) {
    $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Timeout.' }
    $code=$process.ExitCode
    Write-Host ($stdout.Result+$stderr.Result)
    $process.Dispose()
    Assert ($code -eq $expected) "ExitCode $code, esperado $expected."
}
function Wait-File([string]$path) {
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    while (![IO.File]::Exists($path)) { if ([DateTime]::UtcNow -gt $deadline) { throw ('Timeout: '+$path) }; Start-Sleep -Milliseconds 100 }
}
function Read-Snapshot([string]$path) {
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader=New-Object IO.StreamReader($stream)
    try { return ($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose() }
}
$small=Join-Path $root 'fixture.mp4'
& $ffmpeg -hide_banner -loglevel error -nostdin -y -f lavfi -i 'testsrc2=size=320x240:rate=15' -t 1 -an -c:v libx264 $small
Assert ($LASTEXITCODE -eq 0) 'Falha ao gerar fixture.'

# MKV estável é ignorado mesmo antes de o MP4 exportado existir ou ter o mesmo nome.
$config=Config 'movavi'
$folder=Split-Path $config -Parent
$mkv=Join-Path $folder 'Entrada/ScreenRecorderProject.MKV'
& $ffmpeg -hide_banner -loglevel error -nostdin -y -i $small -c copy $mkv
Assert ($LASTEXITCODE -eq 0) 'Falha ao gerar MKV.'
$mkvHash=(Get-FileHash $mkv).Hash
$monitor=Start-App $config '--console'
try {
    Wait-File (Join-Path $folder 'status.json')
    Start-Sleep -Milliseconds 1200
    $snapshot=Read-Snapshot (Join-Path $folder 'status.json')
    Assert ($snapshot.waiting -eq 0 -and $snapshot.processing -eq 0 -and @($snapshot.jobs).Count -eq 0) 'MKV provisório entrou na fila.'
    [IO.File]::Copy($small,(Join-Path $folder 'Entrada/evidencia-final.mp4'))
    Wait-File (Join-Path $folder 'Saida/evidencia-final_compressed.mp4')
    Assert (@(Get-ChildItem (Join-Path $folder 'Saida') -File).Count -eq 1) 'Pré-vídeo e exportação foram processados.'
} finally { if (!$monitor.HasExited) { $monitor.Kill(); $monitor.WaitForExit() }; $monitor.Dispose() }
Finish (Start-App $config)
Assert ((Get-FileHash $mkv).Hash -eq $mkvHash) 'MKV original foi alterado.'
Finish (Start-App $config ('--manual "'+$mkv+'"'))
Assert ([IO.File]::Exists((Join-Path $folder 'Saida/ScreenRecorderProject_compressed.mkv'))) 'Modo manual não aceitou MKV.'
$normalConfig=Config 'mkv-normal' 'fast' $false
[IO.File]::Copy($mkv,(Join-Path (Split-Path $normalConfig -Parent) 'Entrada/legitimo.mkv'))
Finish (Start-App $normalConfig)
Assert ([IO.File]::Exists((Join-Path (Split-Path $normalConfig -Parent) 'Saida/legitimo_compressed.mkv'))) 'Opção para monitorar MKV não funcionou.'

# Mesmo vídeo, estados e saídas separados: comparação real dos dois perfis.
$large=Join-Path $root 'grande.avi'
& $ffmpeg -hide_banner -loglevel error -nostdin -y -f lavfi -i 'testsrc2=size=640x360:rate=30' -t 8 -an -c:v rawvideo -pix_fmt yuv420p $large
Assert ($LASTEXITCODE -eq 0) 'Falha ao gerar vídeo grande.'
$largeHash=(Get-FileHash $large).Hash
$timings=@{}
$encodingTimings=@{}
foreach ($mode in @('fast','precise')) {
    $case=Config $mode $mode
    $caseRoot=Split-Path $case -Parent
    [IO.File]::Copy($large,(Join-Path $caseRoot 'Entrada/grande.avi'))
    $clock=[Diagnostics.Stopwatch]::StartNew()
    Finish (Start-App $case)
    $clock.Stop(); $timings[$mode]=$clock.Elapsed.TotalSeconds
    $output=Join-Path $caseRoot 'Saida/grande_compressed.mp4'
    Assert ((Get-Item $output).Length -gt 0 -and (Get-Item $output).Length -le 10000000) 'Limite inválido.'
    $events=@(Get-Content (Join-Path $caseRoot 'history.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    $encodingStart=$events | Where-Object state -eq compressing | Select-Object -First 1
    $encodingEnd=$events | Where-Object state -eq validating | Select-Object -First 1
    $encodingTimings[$mode]=([DateTimeOffset]::Parse($encodingEnd.updatedAt)-[DateTimeOffset]::Parse($encodingStart.updatedAt)).TotalSeconds
    if ($mode -eq 'fast') {
        Assert (@($events | Where-Object { $_.message -like '*1 passagem*' }).Count -gt 0) 'Perfil rápido ausente.'
        Assert (@($events | Where-Object { $_.message -like '*Tentativa 2*' -or $_.message -like '*precisa*' }).Count -eq 0) 'Vídeo que coube foi repetido.'
    } else { Assert (@($events | Where-Object { $_.message -like '*passagem 2/2*' }).Count -gt 0) 'Perfil preciso não fez duas passagens.' }
}
Assert ((Get-FileHash $large).Hash -eq $largeHash) 'Original do benchmark foi alterado.'

# Recuperação determinística usando encoder controlado + FFprobe e vídeo reais.
$shim=Join-Path $root 'shim'
[IO.Directory]::CreateDirectory($shim) | Out-Null
$shimBuild=Join-Path $projectRoot "tests/FakeEncoder/bin/$Configuration/net9.0"
foreach ($file in [IO.Directory]::GetFiles($shimBuild)) { [IO.File]::Copy($file,(Join-Path $shim ([IO.Path]::GetFileName($file)))) }
[IO.File]::Copy((Join-Path $shim 'FakeEncoder.exe'),(Join-Path $shim 'ffmpeg.exe'))
[IO.File]::Copy((Join-Path $tools 'ffprobe.exe'),(Join-Path $shim 'ffprobe.exe'))
$env:COMPRESSOR_TEST_ENCODER_VIDEO=$small
try {
    foreach ($kind in @('recovery','max-one','never-fits')) {
        $log=Join-Path $root ($kind+'.jsonl')
        $env:COMPRESSOR_TEST_ENCODER_LOG=$log
        $env:COMPRESSOR_TEST_ALWAYS_OVERSIZE=if ($kind -eq 'never-fits') { '1' } else { '0' }
        $attempts=if ($kind -eq 'max-one') { 1 } else { 2 }
        $case=Config $kind 'fast' $true $shim $attempts 45000 50000
        $caseRoot=Split-Path $case -Parent
        # O encoder de teste depende de DLL/runtimeconfig; a build real do fornecedor é estática.
        $managed=Join-Path $caseRoot 'tools/ffmpeg/bin'
        [IO.Directory]::CreateDirectory($managed) | Out-Null
        foreach ($file in [IO.Directory]::GetFiles($shim)) { [IO.File]::Copy($file,(Join-Path $managed ([IO.Path]::GetFileName($file)))) }
        $videoInput=Join-Path $caseRoot 'Entrada/video.mp4'
        [IO.File]::Copy($small,$videoInput)
        $padded=[IO.File]::OpenWrite($videoInput)
        try { $padded.SetLength(200000) } finally { $padded.Dispose() }
        $expected=if ($kind -eq 'recovery') { 0 } else { 1 }
        Finish (Start-App $case) $expected
        $calls=@(Get-Content $log | ForEach-Object { ,($_ | ConvertFrom-Json) })
        Assert ('-pass' -notin $calls[0]) 'Primeira execução rápida fez análise extra.'
        if ($kind -eq 'max-one') { Assert ($calls.Count -eq 1) 'Máximo configurado foi excedido.' }
        else {
            Assert ($calls.Count -eq 3) 'Recuperação deve fazer uma execução rápida e duas passagens precisas.'
            $firstBitrate=[int]$calls[0][([Array]::IndexOf([object[]]$calls[0],'-b:v')+1)]
            $nextBitrate=[int]$calls[1][([Array]::IndexOf([object[]]$calls[1],'-b:v')+1)]
            Assert ($nextBitrate -lt $firstBitrate) 'Recuperação não reduziu o bitrate.'
        }
        $output=Join-Path $caseRoot 'Saida/video_compressed.mp4'
        if ($expected -eq 0) { Assert ((Get-Item $output).Length -eq 45000) 'Recuperação não publicou o resultado válido.' }
        else { Assert (![IO.File]::Exists($output)) 'Resultado acima do limite foi publicado.' }
    }
} finally {
    [Environment]::SetEnvironmentVariable('COMPRESSOR_TEST_ENCODER_VIDEO',$null)
    [Environment]::SetEnvironmentVariable('COMPRESSOR_TEST_ENCODER_LOG',$null)
    [Environment]::SetEnvironmentVariable('COMPRESSOR_TEST_ALWAYS_OVERSIZE',$null)
}
[IO.File]::WriteAllText((Join-Path $root 'timings.json'),(@{ processSeconds=$timings; encodingSeconds=$encodingTimings } | ConvertTo-Json -Depth 3))
Write-Host ('PASSOU: Movavi/MKV, exportação final, manual, perfil rápido/preciso, parada após sucesso, recuperação e bloqueio do limite. Compressao: rapido {0:F2}s; preciso {1:F2}s. Artefatos: {2}' -f $encodingTimings.fast,$encodingTimings.precise,$root)

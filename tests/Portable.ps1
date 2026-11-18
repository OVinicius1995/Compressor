param([Parameter(Mandatory=$true)][string]$PackagePath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $projectRoot ('.test-artifacts/portable-test-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($PackagePath),$testRoot)
$app=Join-Path $testRoot 'JiraVideoCompressor-windows-x64'
function Assert($value,[string]$message) { if (!$value) { throw $message } }
$manifest=Get-Content (Join-Path $app 'package-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($manifest.selfContained -and !$manifest.sdkRequired) 'Manifesto não indica pacote autocontido.'
Assert ($manifest.applicationLicense -eq 'Apache-2.0') 'Licença do aplicativo ausente.'
foreach ($entry in $manifest.files) {
    $file=Join-Path $app $entry.path
    Assert ([IO.File]::Exists($file)) ('Arquivo ausente: '+$entry.path)
    Assert ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -eq $entry.sha256) ('Hash incorreto: '+$entry.path)
}
Assert (![IO.Directory]::Exists((Join-Path $app '.state'))) 'Pacote contém estado pessoal.'
Assert (![IO.File]::Exists((Join-Path $app 'status.json'))) 'Pacote contém status pessoal.'
$configPath=Join-Path $app 'appsettings.json'
$settings=Get-Content $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
Assert ($settings.InputDirectory -eq 'Entrada' -and $settings.OutputDirectory -eq 'Prontos para Jira') 'Configuração contém caminhos pessoais.'
Assert (!$settings.PSObject.Properties['ToolsDirectory']) 'Configuração ainda expõe o caminho dos componentes.'
$settings.PollSeconds=0.1; $settings.StableSeconds=0.3
[IO.File]::WriteAllText($configPath,($settings | ConvertTo-Json))
function Run-Portable([string]$Arguments='--once', [int]$TimeoutMs=60000) {
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=Join-Path $app 'JiraVideoCompressor.exe'
    # Sem --config e fora da pasta do app: deve usar o arquivo ao lado do executável.
    $start.Arguments=$Arguments
    $start.WorkingDirectory=$projectRoot
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.EnvironmentVariables['DOTNET_ROOT']=Join-Path $testRoot 'runtime-inexistente'
    $start.EnvironmentVariables['DOTNET_ROOT_X64']=Join-Path $testRoot 'runtime-inexistente'
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP']='0'
    $process=[Diagnostics.Process]::Start($start)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($TimeoutMs)) { $process.Kill(); throw 'Timeout do executável portátil.' }
        Write-Host ($stdout.Result+$stderr.Result)
        Assert ($process.ExitCode -eq 0) 'Executável portátil falhou.'
    } finally { $process.Dispose() }
}
if ($manifest.ffmpegDelivery -eq 'download-on-first-run') {
    Assert (![IO.File]::Exists((Join-Path $app 'tools/ffmpeg/bin/ffmpeg.exe'))) 'Pacote público contém FFmpeg incorporado.'
    # Em ambiente sem rede, uma instalação de teste pode ser fornecida explicitamente.
    if ($env:COMPRESSOR_TEST_FFMPEG) {
        [IO.Directory]::CreateDirectory((Join-Path $app 'tools/ffmpeg/bin')) | Out-Null
        foreach ($tool in @('ffmpeg.exe','ffprobe.exe')) { [IO.File]::Copy((Join-Path $env:COMPRESSOR_TEST_FFMPEG $tool),(Join-Path $app ('tools/ffmpeg/bin/'+$tool))) }
    } else {
        Run-Portable -Arguments '--prepare-tools' -TimeoutMs 600000
    }
}
$inputDirectory=Join-Path $app 'Entrada'
$outputDirectory=Join-Path $app 'Prontos para Jira'
[IO.Directory]::CreateDirectory($inputDirectory) | Out-Null
$small=Join-Path $inputDirectory 'copia.mp4'
& (Join-Path $app 'tools/ffmpeg/bin/ffmpeg.exe') -hide_banner -loglevel error -nostdin -y -f lavfi -i 'testsrc2=size=320x240:rate=15' -t 1 -an -c:v libx264 $small
Assert ($LASTEXITCODE -eq 0) 'FFmpeg incluído não gerou o vídeo de teste.'
$large=Join-Path $inputDirectory 'maior.mp4'
[IO.File]::Copy($small,$large)
$file=[IO.File]::OpenWrite($large)
try { $file.SetLength(10000001) } finally { $file.Dispose() }
$smallHash=(Get-FileHash $small).Hash
$largeHash=(Get-FileHash $large).Hash

Run-Portable
$copied=Join-Path $outputDirectory 'copia_compressed.mp4'
$compressed=Join-Path $outputDirectory 'maior_compressed.mp4'
Assert ((Get-FileHash $copied).Hash -eq $smallHash) 'Cópia não é idêntica.'
Assert ((Get-Item $compressed).Length -gt 0 -and (Get-Item $compressed).Length -le 10000000) 'Compressão fora do limite.'
Assert ((Get-FileHash $small).Hash -eq $smallHash -and (Get-FileHash $large).Hash -eq $largeHash) 'Original foi alterado.'
Run-Portable
Assert (@(Get-ChildItem $outputDirectory -File).Count -eq 2) 'Reinício do pacote gerou duplicatas.'
$snapshot=Get-Content (Join-Path $app 'status.json') -Raw | ConvertFrom-Json
Assert (!$snapshot.running -and $snapshot.completed -eq 2) 'Status não foi salvo na pasta do pacote.'
Write-Output ('PASSOU: hashes do pacote, executável sem SDK/runtime externo, configuração local, FFmpeg incluído, cópia, compressão, limite, originais e reinício. Artefatos: '+$testRoot)

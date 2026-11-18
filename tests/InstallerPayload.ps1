param(
    [Parameter(Mandatory=$true)][string]$InstallerPath,
    [Parameter(Mandatory=$true)][string]$UnpackerPath,
    [Parameter(Mandatory=$true)][string]$FFmpegDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$root = Join-Path $projectRoot ('.test-artifacts/installer-payload-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($root) | Out-Null
function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
& $UnpackerPath -x -a -q -y ("-d" + $root) $InstallerPath | Out-Null
Assert ($LASTEXITCODE -eq 0) 'Falha ao extrair o instalador.'
$app = Join-Path $root '{app}'
$manifest = Get-Content (Join-Path $app 'package-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($entry in $manifest.files) {
    if ($entry.path -in @('appsettings.json','Iniciar.cmd')) { continue }
    $file = Join-Path $app $entry.path
    Assert ([IO.File]::Exists($file)) ('Arquivo ausente no instalador: ' + $entry.path)
    Assert ((Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() -eq $entry.sha256) ('Hash incorreto: ' + $entry.path)
}
Assert ([IO.File]::Exists((Join-Path $app 'installed.marker'))) 'Marcador de instalacao ausente.'
Assert (![IO.File]::Exists((Join-Path $app 'appsettings.json'))) 'Instalador inclui preferencias pessoais.'
Assert ([IO.File]::Exists((Join-Path $app 'coreclr.dll'))) 'Runtime ausente.'
Assert (![IO.File]::Exists((Join-Path $app 'tools/ffmpeg/bin/ffmpeg.exe'))) 'Binario do fornecedor redistribuido indevidamente.'
$data = Join-Path $root 'dados'
[IO.Directory]::CreateDirectory((Join-Path $data 'Entrada')) | Out-Null
$config = Join-Path $data 'appsettings.json'
$settings = Get-Content (Join-Path $app 'appsettings.example.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$settings | Add-Member -NotePropertyName ToolsDirectory -NotePropertyValue $FFmpegDirectory
$settings.StableSeconds = 0.1
$settings.PollSeconds = 0.1
[IO.File]::WriteAllText($config,($settings | ConvertTo-Json -Depth 4))
$video = Join-Path $data 'Entrada/exemplo.mp4'
& (Join-Path $FFmpegDirectory 'ffmpeg.exe') -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=320x240:rate=15' -t 1 -an -c:v libx264 $video
Assert ($LASTEXITCODE -eq 0) 'Falha ao gerar video sintetico.'
$hash = (Get-FileHash $video).Hash
$exe = Join-Path $app 'JiraVideoCompressor.exe'
function Run-App([string[]]$arguments) {
    $p = New-Object Diagnostics.ProcessStartInfo
    $p.FileName = $exe
    $p.Arguments = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $p.UseShellExecute = $false
    $p.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($p)
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw 'Timeout do aplicativo instalado.' }
    Assert ($process.ExitCode -eq 0) 'Aplicativo extraido do instalador falhou.'
    $process.Dispose()
}
Run-App @('--prepare-tools','--config',$config)
Assert ([IO.File]::Exists((Join-Path $data 'tools/ffmpeg/bin/ffmpeg.exe'))) 'Preparacao nao instalou os recursos na pasta dos dados.'
Run-App @('--once','--config',$config)
Run-App @('--once','--config',$config)
$results = @([IO.Directory]::GetFiles((Join-Path $data 'Prontos para Jira')))
Assert ($results.Count -eq 1) 'Reinicio duplicou resultados.'
Assert ([IO.Path]::GetFileName($results[0]) -eq 'exemplo_compressed.mp4') 'Nome incorreto.'
Assert ((Get-FileHash $video).Hash -eq $hash) 'Original alterado.'
Assert ((Get-Item $results[0]).Length -le 10000000) 'Resultado acima do limite.'
Write-Output "PASSOU: integridade do instalador, runtime, ferramentas, copia real, originais e reinicio. Artefatos: $root"

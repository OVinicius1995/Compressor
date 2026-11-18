param([Parameter(Mandatory=$true)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
# Registra/desregistra o aplicativo: execute somente no runner descartavel do GitHub.
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Este teste exige o runner descartavel do GitHub Actions.' }
$root = Join-Path $env:GITHUB_WORKSPACE ('.test-artifacts/installer-windows-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'app'
$data = Join-Path $env:LOCALAPPDATA 'JiraVideoCompressor'
if ([IO.Directory]::Exists($data)) { throw 'O runner ja possui dados do aplicativo; teste interrompido para preserva-los.' }
[IO.Directory]::CreateDirectory($root) | Out-Null
function Assert($value,[string]$message) { if (!$value) { throw $message } }
function Run([string]$file,[string[]]$arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $file
    $info.Arguments = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $process = [Diagnostics.Process]::Start($info)
    if (!$process.WaitForExit(900000)) { $process.Kill(); throw 'Timeout da instalacao.' }
    Assert ($process.ExitCode -eq 0) ("Processo falhou: $file ($($process.ExitCode))")
    $process.Dispose()
}
$argsSetup = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=',('/DIR=' + $app),'/GROUP=Compressor CI',('/LOG=' + (Join-Path $root 'setup.log')))
Run $InstallerPath $argsSetup
$config = Join-Path $data 'appsettings.json'
Assert ([IO.File]::Exists($config)) 'Preferencias iniciais ausentes.'
$settings = Get-Content $config -Raw -Encoding UTF8 | ConvertFrom-Json
Assert (![string]::IsNullOrWhiteSpace($settings.InputDirectory)) 'Pasta inicial ausente.'
Assert ([IO.Path]::IsPathRooted($settings.InputDirectory)) 'Pasta inicial nao e absoluta.'
Assert ($settings.PSObject.Properties.Name -notcontains 'ToolsDirectory') 'Caminho de ferramenta exposto nas preferencias.'
$ffmpeg = Join-Path $data 'tools/ffmpeg/bin/ffmpeg.exe'
Assert ([IO.File]::Exists($ffmpeg)) 'Instalacao nao preparou FFmpeg.'
Assert ([IO.File]::Exists((Join-Path $data 'tools/ffmpeg/bin/ffprobe.exe'))) 'Instalacao nao preparou FFprobe.'
Assert ([IO.File]::Exists((Join-Path $app 'installed.marker'))) 'Marcador de instalacao ausente.'
$settings.InputDirectory = Join-Path $root 'Entrada'
$settings.OutputDirectory = Join-Path $root 'Saida'
$settings.StableSeconds = 0.1
$settings.PollSeconds = 0.1
[IO.Directory]::CreateDirectory($settings.InputDirectory) | Out-Null
[IO.File]::WriteAllText($config,($settings | ConvertTo-Json -Depth 4))
$original = Join-Path $settings.InputDirectory 'exemplo.mp4'
Run $ffmpeg @('-hide_banner','-loglevel','error','-f','lavfi','-i','testsrc2=size=320x240:rate=15','-t','1','-an','-c:v','libx264',$original)
$hash = (Get-FileHash $original).Hash
$exe = Join-Path $app 'JiraVideoCompressor.exe'
Run $exe @('--once')
Run $exe @('--once')
Assert (@([IO.Directory]::GetFiles($settings.OutputDirectory)).Count -eq 1) 'Reinicio gerou duplicata.'
$savedConfig = [IO.File]::ReadAllText($config)
$receipt = @([IO.Directory]::GetFiles((Join-Path $data '.state'),'*.json'))
Assert ($receipt.Count -gt 0) 'Registro de duplicatas ausente.'
Run $InstallerPath $argsSetup
Assert ([IO.File]::ReadAllText($config) -eq $savedConfig) 'Atualizacao alterou preferencias.'
Assert ([IO.File]::Exists($receipt[0])) 'Atualizacao removeu registros.'
Run (Join-Path $app 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG=' + (Join-Path $root 'uninstall.log')))
Assert (![IO.File]::Exists($exe)) 'Desinstalacao manteve o executavel.'
Assert ([IO.File]::ReadAllText($config) -eq $savedConfig) 'Desinstalacao alterou preferencias.'
Assert ([IO.File]::Exists($receipt[0])) 'Desinstalacao removeu registros.'
Assert ((Get-FileHash $original).Hash -eq $hash) 'Original alterado.'
Assert ([IO.File]::Exists((Join-Path $settings.OutputDirectory 'exemplo_compressed.mp4'))) 'Resultado removido.'
Write-Output 'PASSOU: instalacao completa, download, pastas iniciais, execucao pelo padrao instalado, atualizacao e desinstalacao preservando dados.'

param(
    [switch]$IncludeFFmpeg,
    [string]$FFmpegDirectory,
    [string]$PublishedAppDirectory,
    [string]$RuntimePackagesDirectory,
    [string]$RuntimeVersion = '9.0.23'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($IncludeFFmpeg -and !$FFmpegDirectory) {
    $FFmpegDirectory = Join-Path $projectRoot 'tools/ffmpeg/bin'
}
if ($IncludeFFmpeg) {
    $FFmpegDirectory = [IO.Path]::GetFullPath($FFmpegDirectory)
    foreach ($executable in @('ffmpeg.exe','ffprobe.exe')) {
        if (![IO.File]::Exists((Join-Path $FFmpegDirectory $executable))) { throw "Arquivo ausente: $executable em $FFmpegDirectory" }
    }
    $ffmpegLicense = Join-Path (Split-Path $FFmpegDirectory -Parent) 'LICENSE.txt'
    if (![IO.File]::Exists($ffmpegLicense)) { throw 'Mantenha LICENSE.txt da distribuição do FFmpeg na pasta acima de bin.' }
}

$buildId = Get-Date -Format 'yyyyMMdd-HHmmss'
$buildRoot = Join-Path $projectRoot ('artifacts/portable-' + $buildId + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6))
$packageName = 'JiraVideoCompressor-windows-x64'
$destination = Join-Path $buildRoot $packageName
[IO.Directory]::CreateDirectory($destination) | Out-Null
if (!$PublishedAppDirectory) {
    & dotnet publish (Join-Path $projectRoot 'JiraVideoCompressor.csproj') -c Release -r win-x64 --self-contained true "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:NuGetAudit=false --source https://api.nuget.org/v3/index.json -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar a publicação autocontida.' }
} else {
    $PublishedAppDirectory = [IO.Path]::GetFullPath($PublishedAppDirectory)
    foreach ($file in [IO.Directory]::GetFiles($PublishedAppDirectory, '*', [IO.SearchOption]::AllDirectories)) {
        $relative = $file.Substring($PublishedAppDirectory.TrimEnd('\','/').Length + 1)
        $target = Join-Path $destination $relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.File]::Copy($file,$target)
    }
}
$runtimeConfig = Get-Content (Join-Path $destination 'JiraVideoCompressor.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.framework -or $runtimeConfig.runtimeOptions.frameworks -or
    ![IO.File]::Exists((Join-Path $destination 'coreclr.dll')) -or
    ![IO.File]::Exists((Join-Path $destination 'System.Windows.Forms.dll'))) {
    throw 'A publicação precisa ser autocontida, com runtime e Windows Forms incluídos.'
}

# Somente ferramentas e configuração nova: nunca copia o estado pessoal do projeto.
$tools = Join-Path $destination 'tools/ffmpeg/bin'
$licenses = Join-Path $destination 'Licenses'
[IO.Directory]::CreateDirectory($licenses) | Out-Null
if ($IncludeFFmpeg) {
    [IO.Directory]::CreateDirectory($tools) | Out-Null
    foreach ($executable in @('ffmpeg.exe','ffprobe.exe')) {
        [IO.File]::Copy((Join-Path $FFmpegDirectory $executable),(Join-Path $tools $executable))
    }
    [IO.File]::Copy($ffmpegLicense,(Join-Path $licenses 'FFmpeg-LICENSE.txt'))
}
[IO.File]::Copy((Join-Path $projectRoot 'LICENSE'),(Join-Path $destination 'LICENSE'))
[IO.File]::Copy((Join-Path $projectRoot 'NOTICE'),(Join-Path $destination 'NOTICE'))
if (!$RuntimePackagesDirectory) {
    if ($env:NUGET_PACKAGES) { $RuntimePackagesDirectory = $env:NUGET_PACKAGES }
    else {
        $cache = @(& dotnet nuget locals global-packages --list --force-english-output)
        if ($LASTEXITCODE -ne 0) { throw 'Informe RuntimePackagesDirectory para localizar os avisos do runtime.' }
        $RuntimePackagesDirectory = ($cache | Where-Object { $_ -like 'global-packages:*' }) -replace '^global-packages:\s*',''
    }
}
$actualRuntimeVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
$licenseFiles = @(
    @('microsoft.netcore.app.runtime.win-x64','LICENSE.TXT','DotNet-LICENSE.txt'),
    @('microsoft.netcore.app.runtime.win-x64','THIRD-PARTY-NOTICES.TXT','DotNet-THIRD-PARTY-NOTICES.txt'),
    @('microsoft.windowsdesktop.app.runtime.win-x64','LICENSE','WindowsDesktop-LICENSE.txt')
)
foreach ($entry in $licenseFiles) {
    $source = Join-Path $RuntimePackagesDirectory ($entry[0] + '/' + $actualRuntimeVersion + '/' + $entry[1])
    if (![IO.File]::Exists($source)) { throw "Aviso de terceiro ausente: $source" }
    [IO.File]::Copy($source,(Join-Path $licenses $entry[2]))
}
$freshConfig = Get-Content (Join-Path $projectRoot 'appsettings.example.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$freshConfig.InputDirectory = 'Entrada'
$freshConfig.OutputDirectory = 'Prontos para Jira'
$freshConfig.StateDirectory = '.state'
$json = $freshConfig | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $destination 'appsettings.json'),$json)
[IO.File]::WriteAllText((Join-Path $destination 'appsettings.example.json'),$json)
$launcher = @'
@echo off
start "" "%~dp0JiraVideoCompressor.exe" --config "%~dp0appsettings.json"
'@
[IO.File]::WriteAllText((Join-Path $destination 'Iniciar.cmd'),$launcher.Replace("`n","`r`n"))
$instructions = @'
JIRA VIDEO COMPRESSOR - WINDOWS X64

1. Extraia TODO o ZIP para uma pasta com permissao de escrita.
2. Abra Iniciar.cmd ou JiraVideoCompressor.exe.
3. Escolha entrada e saida. O FFmpeg ja esta configurado.
4. Aguarde a preparacao automatica dos recursos de video ao abrir.
   Na primeira vez, essa preparacao precisa de internet.
   Quando o programa indicar que esta pronto, clique em Salvar e iniciar.
5. Coloque os videos na entrada.
6. Anexe somente os resultados validados da pasta de saida.

Nao precisa instalar .NET, SDK, FFmpeg ou executar comandos de compilacao.
Mantenha os arquivos e subpastas do pacote juntos.
As escolhas ficam no appsettings.json desta pasta.
Fechar a janela mantem o programa na bandeja. Para encerrar, use Sair no icone.
Originais sao preservados. Mantenha .state para evitar duplicatas apos reiniciar.

Aplicativo gratuito e de codigo aberto, sob Apache License 2.0.
Os componentes de terceiros mantem suas proprias licencas.
Consulte Licenses/THIRD-PARTY-NOTICES.md.
'@
[IO.File]::WriteAllText((Join-Path $destination 'LEIA-ME.txt'),$instructions)
$versionLines = @('Download automático do fornecedor na primeira execução')
if ($IncludeFFmpeg) {
    $versionLines = @(& (Join-Path $tools 'ffmpeg.exe') -version 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) { throw 'FFmpeg incluído não executa nesta máquina.' }
    [IO.File]::WriteAllLines((Join-Path $licenses 'FFmpeg-build.txt'),[string[]]$versionLines)
}
$notices = @'
# Componentes de terceiros

## FFmpeg / FFprobe

Os dois executáveis são ferramentas externas invocadas em processos separados.
O pacote público não contém seus executáveis: a aplicação baixa a distribuição
diretamente do fornecedor, por HTTPS, na primeira execução e confere o SHA-256
informado na API do GitHub antes de instalar. A licença é extraída junto das
ferramentas, em tools/ffmpeg/LICENSE.txt; a origem e o hash ficam em ORIGIN.json.

Projeto: https://ffmpeg.org/
Builds de Windows: https://github.com/FFmpeg-Builds/FFmpeg-Builds
Licenças e distribuição: https://ffmpeg.org/legal.html

A build do fornecedor contém componentes GPL, incluindo libx264. Ela continua
sob sua própria licença; a Apache 2.0 do aplicativo não muda essas condições.
Para um pacote offline com executáveis incorporados, também é necessário tratar
a redistribuição dos fontes correspondentes e instruções dessa build.

## .NET

Este pacote inclui o runtime .NET e Windows Desktop obtidos de pacotes oficiais
da Microsoft. Os avisos e licenças incluídos pela publicação permanecem no pacote.
Projeto: https://github.com/dotnet/runtime

## Aplicativo

Jira Video Compressor é distribuído sob Apache License 2.0, disponível em LICENSE.
Os avisos do projeto estão em NOTICE.
'@
[IO.File]::WriteAllText((Join-Path $licenses 'THIRD-PARTY-NOTICES.md'),$notices)
$manifest = [ordered]@{
    platform = 'windows-x64'; selfContained = $true; sdkRequired = $false
    runtimeVersion = ($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
    ffmpegVersion = $versionLines[0]
    ffmpegDelivery = $(if ($IncludeFFmpeg) { 'bundled' } else { 'download-on-first-run' })
    applicationLicense = 'Apache-2.0'
    includesPersonalSettings = $false
    publicReleaseReview = $(if ($IncludeFFmpeg) { @('Disponibilizar fontes correspondentes e instruções dos componentes GPL incluídos') } else { @() })
    files = @()
}
foreach ($file in [IO.Directory]::GetFiles($destination,'*',[IO.SearchOption]::AllDirectories)) {
    if ($file.EndsWith('.pdb',[StringComparison]::OrdinalIgnoreCase)) { [IO.File]::Delete($file); continue }
    $relative = $file.Substring($destination.Length + 1).Replace('\','/')
    if ($relative -match '(^|/)(startup\.log|status\.json|history\.jsonl|instance\.lock|appsettings\.json\.lock)$' -or $relative -match '(^|/)(\.state|\.work|\.test-artifacts)/' -or $relative -match '\.(mp4|avi|mov|mkv|webm)$') { throw "Arquivo pessoal/gerado no pacote: $relative" }
    $manifest.files += [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[IO.File]::WriteAllText((Join-Path $destination 'package-manifest.json'),($manifest | ConvertTo-Json -Depth 6))
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $buildRoot ($packageName + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($destination,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
$checksum = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($zip)
[IO.File]::WriteAllText(($zip + '.sha256'),$checksum + "`n")
Write-Output "Pacote: $zip"
Write-Output "SHA-256: $zip.sha256"

param(
    [string]$Version = '0.4.0',
    [string]$PackageDirectory,
    [string]$CompilerPath
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use uma versao no formato 0.2.0.' }
if (!$CompilerPath) {
    $candidate = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($candidate) { $CompilerPath = $candidate.Source }
    else { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
}
if (![IO.File]::Exists($CompilerPath)) { throw 'Instale Inno Setup 6 ou informe -CompilerPath com o caminho do ISCC.exe.' }
if (!$PackageDirectory) {
    & (Join-Path $PSScriptRoot 'Build-Portable.ps1') -Version $Version
    if ($LASTEXITCODE) { throw 'Falha ao preparar o pacote portatil.' }
    $zip = Get-ChildItem (Join-Path $projectRoot 'artifacts') -Recurse -Filter 'JiraVideoCompressor-windows-x64.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $PackageDirectory = Join-Path $zip.DirectoryName 'JiraVideoCompressor-windows-x64'
}
$PackageDirectory = [IO.Path]::GetFullPath($PackageDirectory)
$appAssembly = Join-Path $PackageDirectory 'JiraVideoCompressor.dll'
$appVersion = [Reflection.AssemblyName]::GetAssemblyName($appAssembly).Version.ToString(3)
if ($appVersion -ne $Version) { throw "Versao do aplicativo ($appVersion) diferente da versao do instalador ($Version)." }
$manifest = Get-Content (Join-Path $PackageDirectory 'package-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (!$manifest.selfContained -or $manifest.ffmpegDelivery -ne 'download-on-first-run') {
    throw 'Use o pacote publico autocontido, com preparacao de FFmpeg no destino.'
}
foreach ($entry in $manifest.files) {
    $file = [IO.Path]::GetFullPath((Join-Path $PackageDirectory $entry.path))
    if (!$file.StartsWith($PackageDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Caminho invalido no manifesto.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) { throw "Hash incorreto: $($entry.path)" }
}
$allowed = @($manifest.files | ForEach-Object { $_.path.Replace('/', '\') }) + 'package-manifest.json'
foreach ($file in [IO.Directory]::GetFiles($PackageDirectory, '*', [IO.SearchOption]::AllDirectories)) {
    $relative = $file.Substring($PackageDirectory.TrimEnd('\').Length + 1)
    if ($relative -notin $allowed) { throw "Arquivo fora do manifesto: $relative" }
}
$output = Join-Path $projectRoot ('artifacts/installer-' + $Version + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[IO.Directory]::CreateDirectory($output) | Out-Null
& $CompilerPath ("/DPackageDir=" + $PackageDirectory) ("/DBuildVersion=" + $Version) ("/DOutputDir=" + $output) (Join-Path $projectRoot 'installer/Compressor.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o instalador.' }
$installer = Join-Path $output ("Compressor-$Version-Setup-x64.exe")
if (![IO.File]::Exists($installer)) { throw 'O compilador nao gerou o instalador esperado.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($installer + '.sha256'),$hash + '  ' + [IO.Path]::GetFileName($installer) + "`n")
Write-Output "Instalador: $installer"

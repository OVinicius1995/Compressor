param([string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$settings = Get-Content (Join-Path $projectRoot 'appsettings.json') -Raw | ConvertFrom-Json
$tools = if ([IO.Path]::IsPathRooted($settings.ToolsDirectory)) { $settings.ToolsDirectory } else { [IO.Path]::GetFullPath((Join-Path $projectRoot $settings.ToolsDirectory)) }
$ffmpeg = Join-Path $tools 'ffmpeg.exe'
$ffprobe = Join-Path $tools 'ffprobe.exe'
$dotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'
$dll = Join-Path $projectRoot "bin/$Configuration/net9.0-windows/JiraVideoCompressor.dll"
$testRoot = Join-Path $projectRoot ('.test-artifacts/' + [Guid]::NewGuid().ToString('N'))
$inputDir = Join-Path $testRoot 'Entrada'
$outputDir = Join-Path $testRoot 'Prontos para Jira'
$stateDir = Join-Path $testRoot 'state'
[IO.Directory]::CreateDirectory($inputDir) | Out-Null
$config = Join-Path $testRoot 'appsettings.json'
@{ ToolsDirectory = $tools; InputDirectory = $inputDir; OutputDirectory = $outputDir; StateDirectory = $stateDir; StableSeconds = 0.5; PollSeconds = 0.1; TargetBytes = 9500000; LimitBytes = 10000000 } | ConvertTo-Json | Set-Content $config -Encoding UTF8

function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
function Start-App([bool]$once = $true) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $dotnet
    $start.Arguments = '"' + $dll + '" --config "' + $config + '"' + $(if ($once) { ' --once' } else { ' --console' })
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    return $process
}
function Finish-App($process, [int]$expected = 0) {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (!$process.WaitForExit(50)) {
        if ([DateTime]::UtcNow -gt $deadline) { $process.Kill(); throw 'Timeout do aplicativo.' }
        $statusPath = Join-Path $testRoot 'status.json'
        if (Test-Path $statusPath) {
            $stream = [IO.File]::Open($statusPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader = New-Object IO.StreamReader($stream)
            try { $live = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            Assert ($live.processing -le 1) 'Mais de um vídeo em processamento.'
            if (@($live.jobs | Where-Object { $_.state -eq 'compressing' -and $_.percent -gt 0 -and $_.percent -lt 99 }).Count -gt 0) { $script:SawCompressionProgress = $true }
        }
    }
    $log = $stdout.Result + $stderr.Result
    Write-Host $log
    Assert ($process.ExitCode -eq $expected) "ExitCode esperado: $expected; recebido: $($process.ExitCode)"
    $process.Dispose()
}
function Results { return @(Get-ChildItem $outputDir -File) }
function Pad-Video([string]$path, [long]$length) {
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Write)
    try { $stream.SetLength($length) } finally { $stream.Dispose() }
}

$small = Join-Path $inputDir 'pequeno.mp4'
& $ffmpeg -hide_banner -loglevel error -nostdin -y -f lavfi -i 'testsrc2=size=320x240:rate=15' -t 1 -an -c:v libx264 $small
Assert ($LASTEXITCODE -eq 0) 'Falha na geração do vídeo pequeno.'
$boundary = Join-Path $inputDir 'limite.mp4'
Copy-Item $small $boundary
Pad-Video $boundary 10000000
$large = Join-Path $inputDir 'grande.avi'
& $ffmpeg -hide_banner -loglevel error -nostdin -y -f lavfi -i 'testsrc2=size=640x360:rate=30' -t 8 -an -c:v rawvideo -pix_fmt yuv420p $large
Assert ($LASTEXITCODE -eq 0) 'Falha na geração do vídeo grande.'
$originals = @{}
Get-ChildItem $inputDir -File | ForEach-Object { $originals[$_.FullName] = (Get-FileHash $_.FullName).Hash }
Finish-App (Start-App)
Assert $script:SawCompressionProgress 'Percentual real de compressão não foi observado.'
Assert ((Results).Count -eq 3) 'Devem existir três resultados.'
foreach ($file in (Results)) {
    Assert ($file.Length -gt 0 -and $file.Length -le 10000000) 'Resultado fora do limite.'
    $probe = (& $ffprobe -v error -show_entries stream=codec_type -of json $file.FullName | Out-String) | ConvertFrom-Json
    Assert ($LASTEXITCODE -eq 0) 'FFprobe rejeitou um resultado.'
    Assert (@($probe.streams | Where-Object codec_type -eq audio).Count -eq 0) 'Resultado contém áudio.'
    & $ffmpeg -v error -nostdin -i $file.FullName -f null NUL
    Assert ($LASTEXITCODE -eq 0) 'Falha na decodificação do resultado.'
}
foreach ($path in $originals.Keys) { Assert ((Get-FileHash $path).Hash -eq $originals[$path]) 'Original alterado.' }
$smallOutput = Join-Path $outputDir 'pequeno_compressed.mp4'
Assert ((Get-FileHash $smallOutput).Hash -eq $originals[$small]) 'Cópia deve ser idêntica.'
Assert ((Get-Item (Join-Path $outputDir 'limite_compressed.mp4')).Length -eq 10000000) 'O limite exato deve ser copiado.'
Assert (Test-Path (Join-Path $outputDir 'grande_compressed.mp4')) 'Nome amigável não foi aplicado.'
Copy-Item $small (Join-Path $inputDir 'renomeado.mp4')
Finish-App (Start-App)
Assert ((Results).Count -eq 3) 'Reinício/renomeação gerou duplicata.'

# Simula uma cópia pausada: outro vídeo deve progredir enquanto este está bloqueado.
$lockedPath = Join-Path $inputDir 'copia-em-andamento.mp4'
$writer = [IO.File]::Open($lockedPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
$app = $null
try {
    $bytes = [IO.File]::ReadAllBytes($small)
    $writer.Write($bytes, 0, [int]($bytes.Length / 2)); $writer.Flush()
    $app = Start-App $false
    Start-Sleep -Milliseconds 1200
    Assert ((Results).Count -eq 3) 'Arquivo bloqueado foi liberado prematuramente.'
    $snapshot = Get-Content (Join-Path $testRoot 'status.json') -Raw | ConvertFrom-Json
    Assert ($snapshot.waiting -eq 1 -and $snapshot.running) 'Status da fila bloqueada incorreto.'
    Finish-App (Start-App) 1
    $next = Join-Path $inputDir 'outro.mp4'
    Copy-Item $small $next
    Pad-Video $next 200000
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Results).Count -lt 4 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Assert ((Results).Count -eq 4) 'Arquivo bloqueado impediu o processamento de outro vídeo.'
    $writer.Write($bytes, [int]($bytes.Length / 2), $bytes.Length - [int]($bytes.Length / 2)); $writer.Flush()
    $writer.SetLength(300000); $writer.Dispose(); $writer = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Results).Count -lt 5 -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    Assert ((Results).Count -eq 5) 'Cópia concluída não foi processada.'
} finally {
    if ($writer) { $writer.Dispose() }
    if ($app) { if (!$app.HasExited) { $app.Kill(); $app.WaitForExit() }; $app.Dispose() }
}

[IO.File]::WriteAllText((Join-Path $inputDir 'invalido.mp4'), 'não é vídeo')
Finish-App (Start-App) 1
Assert ((Results).Count -eq 5) 'Arquivo inválido foi publicado.'
[IO.File]::Delete((Join-Path $inputDir 'invalido.mp4'))
# Resultado ausente é reconstruído; resultado alterado é rejeitado sem sobrescrever.
[IO.File]::Delete($smallOutput)
Finish-App (Start-App)
Assert ((Results).Count -eq 5) 'Resultado ausente não foi recuperado.'
[IO.File]::WriteAllText($smallOutput, 'alterado')
Finish-App (Start-App) 1
Assert ([IO.File]::ReadAllText($smallOutput) -eq 'alterado') 'Resultado alterado foi sobrescrito.'
[IO.File]::Delete($smallOutput)
$overBoundary = Join-Path $inputDir 'um-byte-acima.mp4'
Copy-Item $small $overBoundary
Pad-Video $overBoundary 10000001
Finish-App (Start-App)
$overOutput = Join-Path $outputDir 'um-byte-acima_compressed.mp4'
Assert ((Results).Count -eq 6) 'O arquivo um byte acima não foi tratado.'
Assert ((Get-Item $overOutput).Length -lt 10000000) 'Arquivo acima do limite foi copiado sem comprimir.'
Assert ((Get-Item $overBoundary).Length -eq 10000001) 'Original acima do limite foi alterado.'
# Nome ocupado por conteúdo diferente recebe sufixo numérico, sem sobrescrever.
Pad-Video $small 250000
Finish-App (Start-App)
Assert (Test-Path (Join-Path $outputDir 'pequeno_compressed_2.mp4')) 'Colisão de nomes não foi resolvida.'
Assert ((Get-Item $smallOutput).Length -eq 29002) 'Resultado anterior foi sobrescrito.'
$snapshot = Get-Content (Join-Path $testRoot 'status.json') -Raw | ConvertFrom-Json
Assert (!$snapshot.running -and $snapshot.processing -eq 0 -and $snapshot.waiting -eq 0) 'Snapshot final inconsistente.'
$events = @(Get-Content (Join-Path $testRoot 'history.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
Assert (@($events | Where-Object state -eq compressing).Count -gt 0) 'Histórico não registrou compressão.'
Assert (@($events | Where-Object { $_.state -eq 'validating' -and $_.percent -eq 99 }).Count -gt 0) 'Progresso de validação ausente.'
Assert (@($events | Where-Object state -eq completed).Count -gt 0) 'Histórico não registrou conclusão.'
$configData = Get-Content $config -Raw | ConvertFrom-Json
$configData.OutputDirectory = Join-Path $testRoot 'Segunda saida'
$configData | ConvertTo-Json | Set-Content $config -Encoding UTF8
Finish-App (Start-App)
Assert (@(Get-ChildItem $configData.OutputDirectory -File).Count -eq 7) 'Trocar a saída não preparou os vídeos na nova pasta.'
$configData.OutputDirectory = $outputDir
$configData | ConvertTo-Json | Set-Content $config -Encoding UTF8
Finish-App (Start-App)
Assert ((Results).Count -eq 7) 'Voltar à saída anterior gerou duplicatas.'
# Compatibilidade com registros antigos e resultados nomeados pelo hash.
$legacy = Join-Path $inputDir 'legado.mp4'
Copy-Item $small $legacy
Pad-Video $legacy 350000
$legacyHash = (Get-FileHash $legacy).Hash.ToLowerInvariant()
Copy-Item $legacy (Join-Path $outputDir ($legacyHash + '.mp4'))
@{ OutputName = $legacyHash + '.mp4'; OutputHash = $legacyHash.ToUpperInvariant() } | ConvertTo-Json | Set-Content (Join-Path $stateDir ($legacyHash + '.json')) -Encoding UTF8
Finish-App (Start-App)
Assert ((Results).Count -eq 8 -and (Test-Path (Join-Path $outputDir 'legado_compressed.mp4'))) 'Resultado legado não recebeu nome amigável.'
Assert (!(Test-Path (Join-Path $outputDir ($legacyHash + '.mp4')))) 'Renomeação deixou uma duplicata com nome em hash.'
Assert ((Get-FileHash (Join-Path $outputDir 'legado_compressed.mp4')).Hash.ToLowerInvariant() -eq $legacyHash) 'Renomeação alterou o vídeo.'
Finish-App (Start-App)
Assert ((Results).Count -eq 8) 'Reinício após migração gerou duplicatas.'

# Registro legado sem arquivo: não pode recriar o nome em hash.
$missingLegacy = Join-Path $inputDir 'legado-ausente.mp4'
Copy-Item $small $missingLegacy
Pad-Video $missingLegacy 450000
$missingHash = (Get-FileHash $missingLegacy).Hash.ToLowerInvariant()
@{ OutputName = $missingHash + '.mp4'; OutputHash = $missingHash } | ConvertTo-Json | Set-Content (Join-Path $stateDir ($missingHash + '.json')) -Encoding UTF8
Finish-App (Start-App)
Assert (Test-Path (Join-Path $outputDir 'legado-ausente_compressed.mp4')) 'Reconstrução reutilizou nome legado.'
Assert (!(Test-Path (Join-Path $outputDir ($missingHash + '.mp4')))) 'Reconstrução produziu nome em hash.'

# Recupera migração interrompida antes ou depois do movimento.
foreach ($alreadyMoved in @($false, $true)) {
    $stem = if ($alreadyMoved) { 'movido' } else { 'pendente' }
    $original = Join-Path $inputDir ($stem + '.mp4')
    Copy-Item $small $original
    Pad-Video $original $(if ($alreadyMoved) { 550000 } else { 500000 })
    $hash = (Get-FileHash $original).Hash.ToLowerInvariant()
    $newName = $stem + '_compressed.mp4'
    $oldName = $hash + '.mp4'
    Copy-Item $original (Join-Path $outputDir $(if ($alreadyMoved) { $newName } else { $oldName }))
    @{ OutputName = $newName; OutputHash = $hash; PreviousOutputName = $oldName } | ConvertTo-Json | Set-Content (Join-Path $stateDir ($hash + '.json')) -Encoding UTF8
    Finish-App (Start-App)
    Assert (Test-Path (Join-Path $outputDir $newName)) 'Migração interrompida não foi recuperada.'
    Assert (!(Test-Path (Join-Path $outputDir $oldName))) 'Migração recuperada deixou nome antigo.'
}

# A migração também respeita nomes ocupados, sem sobrescrever outro arquivo.
$collision = Join-Path $inputDir 'colisao-legada.mp4'
Copy-Item $small $collision
Pad-Video $collision 600000
$collisionHash = (Get-FileHash $collision).Hash.ToLowerInvariant()
Copy-Item $collision (Join-Path $outputDir ($collisionHash + '.mp4'))
$occupied = Join-Path $outputDir 'colisao-legada_compressed.mp4'
[IO.File]::WriteAllText($occupied, 'arquivo existente')
@{ OutputName = $collisionHash + '.mp4'; OutputHash = $collisionHash } | ConvertTo-Json | Set-Content (Join-Path $stateDir ($collisionHash + '.json')) -Encoding UTF8
Finish-App (Start-App)
Assert ([IO.File]::ReadAllText($occupied) -eq 'arquivo existente') 'Migração sobrescreveu outro arquivo.'
Assert (Test-Path (Join-Path $outputDir 'colisao-legada_compressed_2.mp4')) 'Migração não resolveu a colisão.'
# Regressão do caso real: vídeo comprimido, com hash do resultado diferente do original.
$largeHash = (Get-FileHash $large).Hash.ToLowerInvariant()
$largeOutput = Join-Path $outputDir 'grande_compressed.mp4'
$compressedHash = (Get-FileHash $largeOutput).Hash
$sha = [Security.Cryptography.SHA256]::Create()
try { $destinationKey = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($outputDir.ToUpperInvariant())))).Replace('-', '').Substring(0,16) } finally { $sha.Dispose() }
$largeReceipt = Join-Path $stateDir ($largeHash + '-' + $destinationKey + '.json')
[IO.File]::Move($largeOutput, (Join-Path $outputDir ($largeHash + '.mp4')))
@{ OutputName = $largeHash + '.mp4'; OutputHash = $compressedHash } | ConvertTo-Json | Set-Content $largeReceipt -Encoding UTF8
Finish-App (Start-App)
Assert ((Get-FileHash $largeOutput).Hash -eq $compressedHash) 'Migração do vídeo comprimido alterou o conteúdo.'
Assert (!(Test-Path (Join-Path $outputDir ($largeHash + '.mp4')))) 'Vídeo comprimido manteve nome em hash.'
[IO.File]::Delete($largeOutput)
@{ OutputName = $largeHash + '.mp4'; OutputHash = $compressedHash } | ConvertTo-Json | Set-Content $largeReceipt -Encoding UTF8
Finish-App (Start-App)
Assert (Test-Path $largeOutput) 'Recompressão com registro legado não usou o nome original.'
Assert (!(Test-Path (Join-Path $outputDir ($largeHash + '.mp4')))) 'Recompressão ressuscitou o nome em hash.'
Write-Host "PASSOU: cópia, limite exato, compressão real, validação, originais, reinício, duplicatas, bloqueio/cópia lenta, fila, instância única, arquivo inválido e recuperação. Artefatos: $testRoot"

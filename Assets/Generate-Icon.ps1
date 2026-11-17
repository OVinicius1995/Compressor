# Gera um ICO multirresolução sem ferramentas/pacotes externos.
# O desenho vetorial de referência está em roller.svg.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bitmap = New-Object Drawing.Bitmap($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.ScaleTransform(($size / 256.0), ($size / 256.0))
    $outline = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#233044'), 10)
    $outline.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $outline.StartCap = $outline.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $dark = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#233044'))
    $yellow = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#ffd052'))
    $orange = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#f2a91d'))
    $blue = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#66c9e8'))
    $steel = New-Object Drawing.SolidBrush([Drawing.ColorTranslator]::FromHtml('#8c9baa'))
    $highlight = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#c8d6df'), 6)
    $highlight.StartCap = $highlight.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $glassBorder = New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml('#233044'), 6)
    try {
        $graphics.FillRectangle($yellow, 92, 36, 72, 90); $graphics.DrawRectangle($outline, 92, 36, 72, 90)
        $graphics.FillRectangle($blue, 105, 49, 46, 57); $graphics.DrawRectangle($glassBorder, 105, 49, 46, 57)
        $body = [Drawing.Point[]]@((New-Object Drawing.Point(46,107)), (New-Object Drawing.Point(95,107)), (New-Object Drawing.Point(95,132)), (New-Object Drawing.Point(206,132)), (New-Object Drawing.Point(206,171)), (New-Object Drawing.Point(46,171)))
        $graphics.FillPolygon($orange, $body); $graphics.DrawPolygon($outline, $body)
        $graphics.FillRectangle($yellow, 42, 103, 54, 43); $graphics.DrawRectangle($outline, 42, 103, 54, 43)
        $graphics.DrawLine($outline, 82, 31, 174, 31)
        $graphics.DrawLine($outline, 59, 103, 59, 80)
        $graphics.FillEllipse($dark, 157, 152, 62, 62)
        $graphics.FillEllipse($yellow, 176, 171, 24, 24)
        $graphics.DrawLine($outline, 49, 142, 49, 166); $graphics.DrawLine($outline, 112, 142, 112, 166)
        $drum = New-Object Drawing.Drawing2D.GraphicsPath
        try {
            $drum.AddArc(25,162,34,34,180,90); $drum.AddArc(99,162,34,34,270,90)
            $drum.AddArc(99,182,34,34,0,90); $drum.AddArc(25,182,34,34,90,90); $drum.CloseFigure()
            $graphics.FillPath($steel, $drum); $graphics.DrawPath($outline, $drum)
        } finally { $drum.Dispose() }
        $graphics.DrawLine($highlight, 47, 173, 47, 205); $graphics.DrawLine($highlight, 113, 173, 113, 205)
        $memory = New-Object IO.MemoryStream
        try {
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $images += ,($memory.ToArray())
            if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'roller-preview.png'), [Drawing.Imaging.ImageFormat]::Png) }
        } finally { $memory.Dispose() }
    } finally {
        $graphics.Dispose(); $bitmap.Dispose(); $outline.Dispose(); $glassBorder.Dispose(); $highlight.Dispose()
        $dark.Dispose(); $yellow.Dispose(); $orange.Dispose(); $blue.Dispose(); $steel.Dispose()
    }
}
$file = [IO.File]::Create((Join-Path $PSScriptRoot 'roller.ico'))
$writer = New-Object IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose() }

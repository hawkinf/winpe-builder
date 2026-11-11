<#
.SYNOPSIS
    Gera o ícone do WinPE Builder (src/WinPEBuilder.App/app.ico) com vários tamanhos (16..256).
.DESCRIPTION
    Desenha com System.Drawing (nada de arquivos binários de terceiros) e grava um .ico com quadros PNG.
    Rode de novo se quiser mudar cores/texto; depois recompile o app.
#>
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\WinPEBuilder.App\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconFrame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Quadrado arredondado com degradê azul.
    $m = [Math]::Max(1, [int]($size * 0.04))
    $radius = [int]($size * 0.22)
    $rect = New-Object System.Drawing.Rectangle $m, $m, ($size - 2 * $m), ($size - 2 * $m)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 36, 130, 235)),
        ([System.Drawing.Color]::FromArgb(255, 14, 52, 120)), 60.0
    $g.FillPath($brush, $path)

    # Disco (ISO) em linha fina, só quando há espaço.
    if ($size -ge 48) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([single]([Math]::Max(1, $size * 0.03)))
        $c = [int]($size * 0.12)
        $g.DrawEllipse($pen, $c, $c, $size - 2 * $c, $size - 2 * $c)
        $pen.Dispose()
    }

    # Texto "PE".
    $fontSize = [single]($size * 0.46)
    $font = New-Object System.Drawing.Font 'Segoe UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $fmt.LineAlignment = 'Center'
    $textRect = New-Object System.Drawing.RectangleF 0, ([single]($size * 0.02)), $size, $size
    $g.DrawString('PE', $font, [System.Drawing.Brushes]::White, $textRect, $fmt)

    $g.Dispose(); $font.Dispose(); $brush.Dispose(); $path.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) { , (New-IconFrame $s) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)   # ICONDIR
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }                               # 0 = 256 no formato ICO
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()

$target = [System.IO.Path]::GetFullPath($Output)
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
"Ícone gerado: $target ($([Math]::Round((Get-Item $target).Length / 1KB, 1)) KB, tamanhos: $($sizes -join ', '))"

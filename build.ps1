# Gera RegistradorHoras.exe (um único arquivo) usando o compilador C# que já vem no Windows.
# Não precisa instalar nada. Uso:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$aqui = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$saida = Join-Path $aqui 'RegistradorHoras.exe'
$ico   = Join-Path $aqui 'obj\app.ico'

New-Item -ItemType Directory -Force (Join-Path $aqui 'obj') | Out-Null

# --- ícone do .exe (relógio verde), desenhado na hora ---
Add-Type -AssemblyName System.Drawing
function Desenho([int]$t) {
    $bmp = New-Object System.Drawing.Bitmap $t, $t
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $m = $t * 0.05
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(46,160,67))), $m, $m, $t - 2*$m, $t - 2*$m)
    $p = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, $t * 0.09))
    $p.StartCap = 'Round'; $p.EndCap = 'Round'
    $c = $t / 2.0
    $g.DrawLine($p, $c, $c, $c, $t * 0.22)
    $g.DrawLine($p, $c, $c, $t * 0.70, $c + $t * 0.10)
    $g.Dispose()
    return $bmp
}
$tamanhos = 16, 32, 48, 256
$pngs = foreach ($t in $tamanhos) {
    $b = Desenho $t
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    ,($ms.ToArray())
}
$fs = [System.IO.File]::Create($ico)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$tamanhos.Count)
$offset = 6 + 16 * $tamanhos.Count
for ($i = 0; $i -lt $tamanhos.Count; $i++) {
    $t = $tamanhos[$i]; $dim = if ($t -ge 256) { 0 } else { $t }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close(); $fs.Close()

# --- compila ---
$fontes = Get-ChildItem (Join-Path $aqui 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /out:$saida /win32icon:$ico `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $fontes
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar (codigo $LASTEXITCODE)" }
$tam = [Math]::Round((Get-Item $saida).Length / 1KB)
Write-Host "Pronto: $saida ($tam KB)"

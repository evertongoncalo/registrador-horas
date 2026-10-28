# Compila o programa e roda todos os testes:
#   1. lógica   - banco + cronômetro, com relógio simulado (sem telas)
#   2. telas    - abre as janelas de verdade com dados de exemplo e salva capturas PNG
#   3. e2e      - abre o RegistradorHoras.exe real e o controla por UI Automation
#
#   powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
#
# Janelas abrem e fecham sozinhas por alguns segundos (e um som de aviso toca): não mexa no mouse durante o teste.
# Usa uma pasta de dados própria; não encosta nos dados reais nem na inicialização do Windows.
$ErrorActionPreference = 'Stop'
$proj = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$work = Join-Path $env:TEMP 'registrador-horas-testes'
$src  = Join-Path $proj 'src'
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
New-Item -ItemType Directory -Force "$work\logica", "$work\telas", "$work\e2e" | Out-Null
$script:falhou = $false

function Compila([string]$saida, [string[]]$arquivos) {
    & $csc /nologo /target:exe /platform:anycpu "/out:$saida" @refs @arquivos
    if ($LASTEXITCODE -ne 0) { throw "falha ao compilar $saida" }
}
function Roda([string]$titulo, [scriptblock]$bloco) {
    "`n=== $titulo"
    & $bloco
    if ($LASTEXITCODE -ne 0) { $script:falhou = $true; "  -> FALHOU (código $LASTEXITCODE)" }
}

"=== Compilando RegistradorHoras.exe"
& (Join-Path $proj 'build.ps1')

Roda '1/3  Lógica (banco e cronômetro, relógio simulado)' {
    $dados = 'Sqlite.cs', 'Store.cs', 'Tracker.cs', 'Relatorio.cs' | ForEach-Object { Join-Path $src $_ }
    Compila "$work\logica\LogicTest.exe" ($dados + (Join-Path $proj 'tests\LogicTest.cs'))
    & "$work\logica\LogicTest.exe" "$work\logica" | Select-String 'FALHOU|verificações'
}

Roda '2/3  Telas (janelas reais, eventos e capturas)' {
    $todos = Get-ChildItem $src -Filter *.cs | Where-Object { $_.Name -ne 'Program.cs' } | ForEach-Object { $_.FullName }
    Compila "$work\telas\UiShots.exe" ($todos + (Join-Path $proj 'tests\UiShots.cs'))
    & "$work\telas\UiShots.exe" "$work\telas" | Select-String 'FALHOU|verificações'
    "  capturas salvas em $work\telas"
}

Roda '3/3  Ponta a ponta (o .exe de verdade)' {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $proj 'tests\e2e.ps1') -Work "$work\e2e" | Select-String 'FALHOU|verificações'
}

"`n" + $(if ($script:falhou) { 'RESULTADO: HOUVE FALHAS' } else { 'RESULTADO: tudo certo' })
exit $(if ($script:falhou) { 1 } else { 0 })

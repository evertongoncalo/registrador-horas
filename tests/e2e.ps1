# Teste ponta a ponta com o RegistradorHoras.exe de verdade, controlando as janelas por UI Automation.
# Usa uma pasta de dados própria (--dados), sem encostar nos dados reais nem no registro do Windows.
#   powershell -ExecutionPolicy Bypass -File tests\e2e.ps1 -Work <pasta temporária>
param([Parameter(Mandatory)][string]$Work)
$ErrorActionPreference = 'Stop'
$proj = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exe  = Join-Path $proj 'RegistradorHoras.exe'
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$seed = Join-Path $Work 'E2eSeed.exe'
$dados = Join-Path $Work 'dados'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
                        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
'@
$AE = [System.Windows.Automation.AutomationElement]
$falhas = 0; $total = 0
function Ok($cond, $msg) { $script:total++; if (-not $cond) { $script:falhas++ }; "  {0}  {1}" -f ($(if ($cond) { 'ok     ' } else { 'FALHOU ' }), $msg) }

New-Item -ItemType Directory -Force $Work | Out-Null
$fontes = 'Sqlite.cs', 'Store.cs' | ForEach-Object { Join-Path $proj "src\$_" }
& $csc /nologo /target:exe /platform:anycpu /out:$seed /r:System.dll /r:System.Core.dll $fontes (Join-Path $proj 'tests\E2eSeed.cs')
if ($LASTEXITCODE -ne 0) { throw 'falha ao compilar o E2eSeed' }

function Janelas($procId) {
    $c = New-Object System.Windows.Automation.PropertyCondition ($AE::ProcessIdProperty), $procId
    $AE::RootElement.FindAll('Children', $c)
}
function Espera($procId, $titulo, $seg = 10) {
    # diálogos aparecem dentro da janela principal na árvore do UI Automation (são "owned"), então procura nos dois níveis
    $soJanelas = New-Object System.Windows.Automation.PropertyCondition ($AE::ControlTypeProperty), ([System.Windows.Automation.ControlType]::Window)
    $fim = (Get-Date).AddSeconds($seg)
    while ((Get-Date) -lt $fim) {
        foreach ($w in (Janelas $procId)) {
            if ($w.Current.Name -like $titulo) { return $w }
            foreach ($f in $w.FindAll('Children', $soJanelas)) { if ($f.Current.Name -like $titulo) { return $f } }
        }
        Start-Sleep -Milliseconds 250
    }
    return $null
}
function Clica($win, $texto) {
    # acha o controle pelo texto e manda BM_CLICK para a janela dele (funciona para botão e opção de rádio)
    foreach ($b in $win.FindAll('Descendants', [System.Windows.Automation.Condition]::TrueCondition)) {
        if ($b.Current.NativeWindowHandle -ne 0 -and $b.Current.Name -like $texto) {
            [W]::SendMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            return $true
        }
    }
    return $false
}
function Dump { , @(& $seed dump $dados | Where-Object { $_ -like 'REG|*' }) }   # a vírgula evita que uma lista de 1 item vire texto
function Diag($p) {
    $p.Refresh()
    $j = @(Janelas $p.Id | ForEach-Object { '"' + $_.Current.Name + '"' }) -join ', '
    "      diagnóstico: processo $(if ($p.HasExited) { 'ENCERROU (código ' + $p.ExitCode + ')' } else { 'vivo' }); janelas: $j"
}
function Inicia([string[]]$extra = @()) {
    $p = Start-Process $exe -ArgumentList (@('--dados', "`"$dados`"") + $extra) -PassThru
    $script:procs += $p
    return $p
}
$procs = @()

try {
    # ---------------------------------------------------------------- A. recuperação após queda
    "`n[A] queda de energia: registro ficou aberto"
    Remove-Item $dados -Recurse -Force -ErrorAction SilentlyContinue
    $sinal = (& $seed seed $dados | Where-Object { $_ -like 'SINAL=*' }) -replace 'SINAL=', ''
    Ok ((Dump)[0] -like '*|ABERTO|*') "banco preparado com um registro aberto (último sinal $sinal)"
    $p = Inicia
    $dlg = Espera $p.Id 'Registro que ficou aberto*'
    Ok ($dlg -ne $null) 'ao abrir o programa apareceu a pergunta sobre o registro deixado aberto'
    if (-not $dlg) { Diag $p }
    if ($dlg) { Ok (Clica $dlg 'Confirmar') 'confirmou a opção padrão (encerrar no último sinal)' }
    Start-Sleep -Milliseconds 800
    $reg = (Dump)[0].Split('|')
    Ok ($reg[4] -eq $sinal -and $reg[6] -eq 'queda') "registro encerrado no último sinal ($($reg[4])), origem '$($reg[6])'"
    $main = Espera $p.Id 'Registrador de Horas*'
    Ok ($main -ne $null) 'a janela principal ficou disponível'

    # ---------------------------------------------------------------- B. iniciar pelo botão e desligamento do Windows
    "`n[B] iniciar pelo botão e simular o Windows desligando"
    Ok (Clica $main '*Iniciar*') 'clicou em Iniciar'
    Start-Sleep -Milliseconds 800
    $d = Dump
    Ok ($d.Count -eq 2 -and $d[1] -like '*|ABERTO|*') 'novo registro em andamento no banco'
    $main = Espera $p.Id '*Empresa Teste*Registrador de Horas'
    Ok ($main -ne $null) 'o título da janela passou a mostrar o tempo e a empresa'
    $h = [IntPtr]$main.Current.NativeWindowHandle
    [W]::SendMessage($h, 0x11, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null   # WM_QUERYENDSESSION: o aviso que o Windows manda ao desligar
    Start-Sleep -Milliseconds 600
    $reg = (Dump)[1].Split('|')
    Ok ($reg[4] -ne 'ABERTO' -and $reg[6] -eq 'desligamento') "no aviso de desligamento, o registro foi encerrado na hora ($($reg[4]))"

    # ---------------------------------------------------------------- C. fechar no X / segunda instância
    "`n[C] fechar no X e abrir uma segunda vez"
    [W]::PostMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null    # WM_CLOSE = clicar no X
    Start-Sleep -Milliseconds 800
    $p.Refresh()
    Ok (-not $p.HasExited) 'fechar no X não encerra o programa (continua na bandeja)'
    Ok ((Espera $p.Id 'Registrador de Horas*' 1) -eq $null) 'a janela ficou escondida'
    $p2 = Inicia
    $p2.WaitForExit(5000) | Out-Null
    Ok $p2.HasExited 'abrir de novo não cria uma segunda cópia (a nova sai sozinha)'
    Ok ((Espera $p.Id 'Registrador de Horas*' 5) -ne $null) 'e a janela escondida voltou para a frente'
    Ok ((@(Get-Process RegistradorHoras -ErrorAction SilentlyContinue | Where-Object { $procs.Id -contains $_.Id })).Count -eq 1) 'existe só um processo rodando'

    # ---------------------------------------------------------------- D. início escondido (--bandeja)
    "`n[D] início com o Windows (--bandeja)"
    Stop-Process -Id $p.Id -Force; $p.WaitForExit(3000) | Out-Null
    $p3 = Inicia @('--bandeja')
    Start-Sleep -Seconds 2
    Ok (-not $p3.HasExited) 'iniciou e continua rodando'
    Ok ((Espera $p3.Id 'Registrador de Horas*' 1) -eq $null) 'sem janela na tela: ficou só na bandeja'
    $p4 = Inicia
    $p4.WaitForExit(5000) | Out-Null
    Ok ((Espera $p3.Id 'Registrador de Horas*' 5) -ne $null) 'abrir o atalho de novo mostra a janela do programa que estava na bandeja'

    # ---------------------------------------------------------------- E. recuperar e continuar contando
    "`n[E] queda de energia, escolhendo 'continuar contando'"
    Stop-Process -Id $p3.Id -Force; $p3.WaitForExit(3000) | Out-Null
    Remove-Item $dados -Recurse -Force
    & $seed seed $dados | Out-Null
    $p5 = Inicia
    $dlg = Espera $p5.Id 'Registro que ficou aberto*'
    Ok ($dlg -ne $null) 'apareceu a pergunta'
    if ($dlg) {
        Ok (Clica $dlg 'Continuar contando*') "marcou 'Continuar contando'"
        Clica $dlg 'Confirmar' | Out-Null
    }
    Start-Sleep -Milliseconds 800
    $reg = (Dump)[0].Split('|')
    Ok ($reg[4] -eq 'ABERTO') 'o registro seguiu aberto, contando'
    $main = Espera $p5.Id '*Empresa Teste*Registrador de Horas'
    Ok ($main -ne $null) 'a janela mostra o relógio rodando (tempo desde o início original)'
}
finally {
    foreach ($x in $procs) { try { if (-not $x.HasExited) { Stop-Process -Id $x.Id -Force } } catch { } }
}
"`n$($total - $falhas) de $total verificações ok" + $(if ($falhas) { ", $falhas FALHARAM" } else { '' })
exit $(if ($falhas) { 1 } else { 0 })

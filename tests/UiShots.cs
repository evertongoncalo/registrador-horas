using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using RegistradorHoras;

// Abre as telas de verdade com dados de exemplo e relógio simulado, exercita os eventos
// (pergunta de 20 min, falta de resposta, recuperação) e salva capturas PNG para conferir.
static class UiShots
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern int GetDpiForSystem();

    static string saida;
    static int total, falhas;
    static DateTime agora = DateTime.ParseExact("2026-10-28 14:35:12", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    static DateTime D(string s) { return DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }

    static void Ok(bool c, string msg)
    {
        total++; if (!c) falhas++;
        Console.WriteLine((c ? "  ok      " : "  FALHOU  ") + msg);
    }

    static void Bombeia(int ms)
    {
        DateTime fim = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < fim) { Application.DoEvents(); Thread.Sleep(15); }
    }

    // avança o relógio simulado em passos curtos, como se o tempo passasse de verdade
    static void Avanca(Tracker t, int segundos)
    {
        for (int i = 0; i < segundos; i += 10) { agora = agora.AddSeconds(Math.Min(10, segundos - i)); t.Tick(); Application.DoEvents(); }
    }

    static void Foto(Control c, string nome)
    {
        c.Refresh();
        Bombeia(80);
        using (var bmp = new Bitmap(c.Width, c.Height))
        {
            c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
            bmp.Save(Path.Combine(saida, nome + ".png"), ImageFormat.Png);
        }
        Console.WriteLine("  captura: " + nome + ".png (" + c.Width + "x" + c.Height + ")");
    }

    static T Campo<T>(object o, string nome)
    {
        return (T)o.GetType().GetField(nome, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);
    }

    static void Chama(object o, string metodo, params object[] args)
    {
        o.GetType().GetMethod(metodo, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public).Invoke(o, args);
    }

    static void Reg(Store s, long emp, string tipo, string ini, string fim, string origem, string obs)
    {
        s.Inserir(new Reg { EmpresaId = emp, Tipo = tipo, Inicio = D(ini), Fim = D(fim), Origem = origem, Obs = obs });
    }

    [STAThread]
    static int Main(string[] args)
    {
        saida = args[0];
        Directory.CreateDirectory(saida);
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int dpi = 96;
        try { dpi = GetDpiForSystem(); } catch { }
        Console.WriteLine("DPI do sistema: " + dpi + " (" + (dpi * 100 / 96) + "%)");

        string db = Path.Combine(saida, "ui.db");
        if (File.Exists(db)) File.Delete(db);
        var s = new Store(db);
        // e1 é a que está rodando; e3 fica em 1º lugar na ordem alfabética (usada para testar o botão "Trocar")
        long e1 = s.AddEmpresa("Empresa 02"), e2 = s.AddEmpresa("Empresa 03"), e3 = s.AddEmpresa("Empresa 01");

        // segunda 26
        Reg(s, e1, Tipos.Trabalho, "2026-10-26 08:30:00", "2026-10-26 10:15:00", "manual", null);
        Reg(s, e2, Tipos.Trabalho, "2026-10-26 10:30:00", "2026-10-26 12:00:00", "troca", null);
        Reg(s, e1, Tipos.Trabalho, "2026-10-26 13:30:00", "2026-10-26 15:45:00", "pergunta_nao", null);
        Reg(s, e3, Tipos.Deslocamento, "2026-10-26 15:45:00", "2026-10-26 16:10:00", "adicionado", "ida de carro");
        Reg(s, e3, Tipos.Visita, "2026-10-26 16:10:00", "2026-10-26 18:00:00", "adicionado", "reunião de kickoff");
        // terça 27
        Reg(s, e1, Tipos.Trabalho, "2026-10-27 09:00:00", "2026-10-27 10:20:00", "sem_resposta", null);
        Reg(s, e2, Tipos.Trabalho, "2026-10-27 10:40:00", "2026-10-27 11:35:00", "manual", null);
        Reg(s, e3, Tipos.Deslocamento, "2026-10-27 12:20:00", "2026-10-27 13:00:00", "manual", null);
        Reg(s, e3, Tipos.Visita, "2026-10-27 13:00:00", "2026-10-27 16:30:00", "manual", "instalação do sistema");
        // quarta 28 (hoje)
        Reg(s, e2, Tipos.Trabalho, "2026-10-28 08:05:00", "2026-10-28 09:00:00", "troca", null);
        Reg(s, e1, Tipos.Trabalho, "2026-10-28 09:35:00", "2026-10-28 10:40:00", "pergunta_nao", null);
        Reg(s, e3, Tipos.Trabalho, "2026-10-28 11:50:00", "2026-10-28 12:40:00", "manual", null);

        var t = new Tracker(s);
        t.Clock = delegate { return agora; };
        t.IntervaloPc = 120; // só para avançar 35 min sem pergunta no cenário inicial; volta a 20 antes da troca
        agora = D("2026-10-28 14:00:00");
        t.Iniciar(e1, Tipos.Trabalho);
        Avanca(t, 35 * 60 + 12); // 14:35:12, em passos curtos como no uso real

        var f = new MainForm(t, false);
        f.Show();
        Bombeia(900);

        Console.WriteLine("\n[tela principal]");
        Ok(f.Visible, "janela abriu");
        var btn = Campo<Button>(f, "btnPlay");
        var lblRelogio = Campo<Label>(f, "lblRelogio");
        Ok(btn.Text.Contains("Parar"), "botão mostra 'Parar' com a empresa em andamento: \"" + btn.Text + "\"");
        Ok(lblRelogio.Text == "00:35:12", "relógio mostra 00:35:12 (14:00:00 até 14:35:12): " + lblRelogio.Text);
        Foto(f, "01-registrar-rodando");

        var cbEmpresa = Campo<ComboBox>(f, "cbEmpresa");
        cbEmpresa.SelectedIndex = 0;
        Bombeia(100);
        Ok(btn.Text.Contains("Trocar"), "escolher outra empresa vira 'Trocar': \"" + btn.Text + "\"");
        Foto(f, "02-registrar-trocar");
        t.IntervaloPc = 20;
        btn.PerformClick();
        Bombeia(100);
        Ok(t.Atual != null && t.Atual.Empresa != "Empresa 02", "clicar em Trocar passou para " + t.Atual.Empresa);
        Ok(s.Periodo(D("2026-10-28 14:00:00"), D("2026-10-28 15:00:00"), e1, null)[0].Origem == "troca", "a anterior ficou encerrada como 'troca'");

        Console.WriteLine("\n[pergunta de 20 minutos na tela]");
        Avanca(t, 20 * 60 - 20);
        Ok(Application.OpenForms.Count == 1, "ainda sem pergunta aos 19min40s");
        Avanca(t, 30);
        PerguntaForm p = null;
        foreach (Form fm in Application.OpenForms) if (fm is PerguntaForm) p = (PerguntaForm)fm;
        Ok(p != null && p.Visible && p.TopMost, "a pergunta apareceu sozinha, visível e por cima de tudo");
        if (p != null) Foto(p, "03-pergunta");
        Avanca(t, 40);
        if (p != null) Foto(p, "03b-pergunta-contagem");
        var sim = p == null ? null : (Button)p.AcceptButton;
        if (sim != null) sim.PerformClick();
        Bombeia(100);
        Ok(t.Atual != null && t.PerguntaEm == null && p.IsDisposed, "clicar 'Sim, continuo' fecha a pergunta e segue contando");

        Console.WriteLine("\n[sem resposta: relógio para]");
        string empresaAntes = t.Atual.Empresa;
        Avanca(t, 20 * 60 - 20);
        Avanca(t, 30);
        p = null;
        foreach (Form fm in Application.OpenForms) if (fm is PerguntaForm) p = (PerguntaForm)fm;
        Ok(p != null, "segunda pergunta apareceu");
        Avanca(t, 130);
        Ok(t.Atual == null, "sem resposta por 2 min: o relógio parou");
        Ok(p == null || p.IsDisposed || !p.Visible, "a janela da pergunta se fechou sozinha");
        Foto(f, "04-parado-aviso");

        Console.WriteLine("\n[relatório]");
        Chama(f, "PeriodoSemana");
        Campo<TabControl>(f, "tabs").SelectedIndex = 1;
        Bombeia(300);
        Foto(f, "05-relatorio-semana");
        var gridRel = Campo<DataGridView>(f, "gridRel");
        var gridRes = Campo<DataGridView>(f, "gridResumo");
        Ok(gridRel.Rows.Count >= 12, "relatório da semana lista " + gridRel.Rows.Count + " registros");
        Ok(gridRes.Rows.Count >= 4, "resumo tem as 3 empresas + TOTAL (" + gridRes.Rows.Count + " linhas)");

        Console.WriteLine("\n[janelas auxiliares]");
        Campo<TabControl>(f, "tabs").SelectedIndex = 0;
        Bombeia(100);
        var reg = s.Obter(s.Periodo(D("2026-10-28 09:00:00"), D("2026-10-29 00:00:00"), e1, null)[0].Id);
        using (var ed = new EdicaoForm(s, t, reg, null)) { ed.Show(); Bombeia(100); Foto(ed, "06-editar"); ed.Close(); }
        using (var emp = new EmpresasForm(s, t)) { emp.Show(); Bombeia(100); Foto(emp, "07-empresas"); emp.Close(); }
        using (var cfg = new ConfigForm(t)) { cfg.Show(); Bombeia(100); Foto(cfg, "08-config"); cfg.Close(); }
        var aberto = new Reg { Id = 99, Empresa = "Empresa 02", Tipo = Tipos.Trabalho, Inicio = D("2026-10-28 13:12:00"), UltimoSinal = D("2026-10-28 14:58:40") };
        using (var rec = new RecuperacaoForm(aberto, D("2026-10-29 08:00:00"))) { rec.Show(); Bombeia(100); Foto(rec, "09-recuperacao"); rec.Close(); }

        typeof(MainForm).GetField("sair", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(f, true); // fechar de verdade (sem esconder na bandeja)
        f.Close();
        s.Dispose();
        Console.WriteLine("\n" + (total - falhas) + " de " + total + " verificações ok" + (falhas > 0 ? ", " + falhas + " FALHARAM" : ""));
        return falhas == 0 ? 0 : 1;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RegistradorHoras;

// Teste da lógica (banco + cronômetro) com relógio simulado. Sem tela.
static class LogicTest
{
    static int total, falhas;

    static void Ok(bool cond, string msg)
    {
        total++;
        if (!cond) falhas++;
        Console.WriteLine((cond ? "  ok      " : "  FALHOU  ") + msg);
    }

    static DateTime D(string s) { return DateTime.ParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }

    class Relogio
    {
        public DateTime T;
    }

    // avança o relógio simulado segundo a segundo, chamando Tick como a tela faz
    static void Passa(Tracker t, Relogio r, int segundos)
    {
        for (int i = 0; i < segundos; i++) { r.T = r.T.AddSeconds(1); t.Tick(); }
    }

    static Tracker Novo(Store s, Relogio r)
    {
        var t = new Tracker(s);
        t.Clock = delegate { return r.T; };
        return t;
    }

    static Reg Ultimo(Store s, long empresa)
    {
        var l = s.Periodo(D("2000-01-01 00:00:00"), D("2100-01-01 00:00:00"), empresa, null);
        return l[l.Count - 1];
    }

    static int Main(string[] args)
    {
        string pasta = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "rh-logic");
        Directory.CreateDirectory(pasta);
        string db = Path.Combine(pasta, "teste.db");
        if (File.Exists(db)) File.Delete(db);

        var s = new Store(db);
        Console.WriteLine("SQLite do Windows, versão " + s.Db.Query("SELECT sqlite_version()")[0][0]);

        Console.WriteLine("\n[empresas]");
        long a = s.AddEmpresa("Empresa 01"), b = s.AddEmpresa("Empresa 02");
        Ok(s.AddEmpresa("  empresa 01 ") == a, "nome repetido (outra caixa/espaços) devolve a mesma empresa");
        Ok(s.Empresas(true).Count == 2, "duas empresas cadastradas");
        bool lancou = false;
        try { s.RenomearEmpresa(b, "EMPRESA 01"); } catch (ArgumentException) { lancou = true; }
        Ok(lancou, "renomear para um nome que já existe é recusado");
        long acentos = s.AddEmpresa("Padaria São João ção");
        Ok(s.Empresas(true).Exists(delegate (Empresa e) { return e.Nome == "Padaria São João ção"; }), "acentos gravam e voltam corretos");
        s.ArquivarEmpresa(acentos);
        Ok(s.Empresas(true).Count == 2 && s.Empresas(false).Count == 3, "arquivar esconde da lista ativa mas mantém no banco");

        var rel = new Relogio { T = D("2026-10-28 09:00:00") };
        var t = Novo(s, rel);
        int perguntas = 0; string aviso = null;
        t.Perguntar += delegate { perguntas++; };
        t.Encerrado += delegate (string m) { aviso = m; };

        Console.WriteLine("\n[pergunta de 20 minutos]");
        t.Iniciar(a, Tipos.Trabalho);
        Ok(t.Atual != null && t.Atual.Inicio == D("2026-10-28 09:00:00"), "iniciou às 09:00:00");
        Passa(t, rel, 19 * 60 + 59);
        Ok(perguntas == 0, "aos 19min59s ainda não perguntou");
        Passa(t, rel, 1);
        Ok(perguntas == 1 && t.PerguntaEm == D("2026-10-28 09:20:00"), "aos 20min perguntou (09:20:00)");
        Passa(t, rel, 30);
        t.Responder(true);
        Ok(t.Atual != null && t.PerguntaEm == null, "respondeu SIM: segue contando");
        Ok(t.ProximaPergunta == D("2026-10-28 09:40:30"), "próxima pergunta 20min depois da resposta (09:40:30)");
        Passa(t, rel, 20 * 60);
        Ok(perguntas == 2, "perguntou de novo às 09:40:30");

        Console.WriteLine("\n[sem resposta]");
        Passa(t, rel, 119);
        Ok(t.Atual != null, "aos 1min59s sem resposta o relógio ainda corre");
        Passa(t, rel, 1);
        Ok(t.Atual == null, "aos 2min sem resposta o relógio PAROU");
        var r1 = Ultimo(s, a);
        Ok(r1.Fim == D("2026-10-28 09:40:30") && r1.Origem == "sem_resposta", "saída registrada no horário da pergunta (09:40:30), origem sem_resposta");
        Ok(aviso != null && aviso.Contains("09:40"), "avisou o usuário: " + aviso);

        Console.WriteLine("\n[respondeu NÃO]");
        rel.T = D("2026-10-28 10:00:00");
        t.Iniciar(b, Tipos.Trabalho);
        Passa(t, rel, 1200 + 15);
        t.Responder(false);
        var r2 = Ultimo(s, b);
        Ok(t.Atual == null && r2.Fim == D("2026-10-28 10:20:00") && r2.Origem == "pergunta_nao", "NÃO às 10:20:15 grava saída às 10:20:00 (hora da pergunta)");

        Console.WriteLine("\n[visita no cliente]");
        rel.T = D("2026-10-28 11:00:00");
        perguntas = 0;
        t.Iniciar(a, Tipos.Visita);
        Passa(t, rel, 3600);
        Ok(perguntas == 0, "em visita NÃO pergunta de 20 em 20 min (1h depois, zero perguntas)");
        Passa(t, rel, 3600);
        Ok(perguntas == 1 && t.PerguntaEm == D("2026-10-28 13:00:00"), "pergunta só após 2h (intervalo externo)");
        t.Responder(false);
        var r3 = Ultimo(s, a);
        Ok(r3.Tipo == Tipos.Visita && r3.Minutos(rel.T) == 120, "registro de visita com 2h gravado como tipo 'visita'");

        Console.WriteLine("\n[troca de empresa e desligamento]");
        rel.T = D("2026-10-28 14:00:00");
        t.Iniciar(a, Tipos.Trabalho);
        Passa(t, rel, 600);
        t.Iniciar(b, Tipos.Trabalho);
        var r4 = s.Periodo(D("2026-10-28 14:00:00"), D("2026-10-28 15:00:00"), a, null)[0];
        Ok(r4.Fim == D("2026-10-28 14:10:00") && r4.Origem == "troca", "trocar de empresa fecha a anterior às 14:10 e abre a nova");
        Ok(t.Atual.EmpresaId == b && s.Aberto().Id == t.Atual.Id, "só existe um registro aberto");
        Passa(t, rel, 1200);
        t.Desligar();
        var r5 = Ultimo(s, b);
        Ok(t.Atual == null && r5.Fim == D("2026-10-28 14:30:00") && r5.Origem == "desligamento", "desligar o PC encerra na hora (14:30:00)");
        t.Desligar();
        Ok(true, "desligar de novo, sem registro aberto, não faz nada");

        Console.WriteLine("\n[queda de energia / processo morto]");
        rel.T = D("2026-10-28 15:00:00");
        t.Iniciar(a, Tipos.Trabalho);
        Passa(t, rel, 5 * 60 + 5);
        // "queda": o processo some sem encerrar nada; um novo programa abre o mesmo banco
        var s2 = new Store(db);
        var t2 = Novo(s2, rel);
        var pend = t2.Pendente();
        Ok(pend != null && pend.Aberto && t2.Recuperando, "ao abrir, achou o registro deixado aberto");
        Ok(pend.UltimoSinal == D("2026-10-28 15:05:00"), "último sinal de atividade gravado: 15:05:00");
        int p2 = 0;
        t2.Perguntar += delegate { p2++; };
        Passa(t2, rel, 3600);
        Ok(p2 == 0 && t2.Atual != null, "enquanto espera a decisão, não pergunta nem para sozinho");
        t2.RecuperarEncerrar(pend.UltimoSinal.Value);
        var r6 = Ultimo(s, a);
        Ok(r6.Fim == D("2026-10-28 15:05:00") && r6.Origem == "queda", "encerrou no último sinal (15:05:00), origem 'queda'");

        rel.T = D("2026-10-28 16:00:00");
        t2.Iniciar(b, Tipos.Trabalho);
        Passa(t2, rel, 100);
        var s3 = new Store(db);
        var t3 = Novo(s3, rel);
        Ok(t3.Pendente() != null, "segunda queda detectada");
        t3.RecuperarContinuar();
        Ok(t3.Atual != null && !t3.Recuperando && t3.ProximaPergunta == rel.T.AddMinutes(20), "escolheu continuar: segue contando, pergunta em 20min");
        Passa(t3, rel, 60);
        t3.Parar("manual");
        Ok(Ultimo(s, b).Origem == "manual" && Ultimo(s, b).Fim == rel.T, "parou manualmente");
        s2.Dispose(); s3.Dispose();

        Console.WriteLine("\n[PC suspenso]");
        rel.T = D("2026-10-28 17:00:00");
        var t4 = Novo(s, rel);
        t4.Iniciar(a, Tipos.Trabalho);
        Passa(t4, rel, 60);
        rel.T = rel.T.AddMinutes(10); // o PC dormiu e o programa não ficou sabendo
        t4.Tick();
        Ok(t4.Atual == null && Ultimo(s, a).Fim == D("2026-10-28 17:01:00") && Ultimo(s, a).Origem == "suspensao", "salto de relógio de 10min: encerra no último instante vivo (17:01:00)");
        rel.T = D("2026-10-28 18:00:00");
        t4.Iniciar(a, Tipos.Trabalho);
        Passa(t4, rel, 90);
        t4.Suspender();
        Ok(t4.Atual == null && Ultimo(s, a).Fim == D("2026-10-28 18:01:30"), "evento de suspensão encerra na hora (18:01:30)");

        Console.WriteLine("\n[banco: integridade]");
        bool dois = false;
        long x1 = s.Iniciar(a, Tipos.Trabalho, D("2026-10-29 08:00:00"));
        try { s.Iniciar(b, Tipos.Trabalho, D("2026-10-29 08:05:00")); } catch (Exception) { dois = true; }
        Ok(dois, "o banco recusa dois registros em andamento ao mesmo tempo");
        s.Fechar(x1, D("2026-10-29 09:00:00"), "manual");
        Ok(s.Sobrepoe(D("2026-10-29 08:30:00"), D("2026-10-29 08:45:00"), 0, rel.T), "detecta horário que cruza outro registro");
        Ok(!s.Sobrepoe(D("2026-10-29 09:00:00"), D("2026-10-29 10:00:00"), 0, rel.T), "encostar no fim de outro registro não é sobreposição");
        Ok(!s.Sobrepoe(D("2026-10-29 08:30:00"), D("2026-10-29 08:45:00"), x1, rel.T), "ao editar um registro, ele não conflita consigo mesmo");

        var man = new Reg { EmpresaId = a, Tipo = Tipos.Deslocamento, Inicio = D("2026-10-29 10:00:00"), Fim = D("2026-10-29 10:35:00"), Origem = "adicionado", Obs = "ida; \"cliente\"" };
        s.Inserir(man);
        var lido = s.Obter(man.Id);
        Ok(lido.Obs == "ida; \"cliente\"" && lido.Tipo == Tipos.Deslocamento && lido.Minutos(rel.T) == 35, "registro manual grava e lê com observação (aspas e ';')");
        lido.Fim = D("2026-10-29 10:50:00"); lido.Origem = "editado";
        s.Atualizar(lido);
        Ok(s.Obter(man.Id).Minutos(rel.T) == 50, "editar o fim atualiza a duração");
        s.Excluir(man.Id);
        Ok(s.Obter(man.Id) == null, "excluir remove");

        s.SetCfg("intervalo_pc", "25");
        s.SetCfg("intervalo_pc", "30");
        Ok(s.CfgInt("intervalo_pc", 20) == 30 && s.CfgInt("nao_existe", 7) == 7, "configurações gravam, regravam e têm padrão");

        string bk = Path.Combine(pasta, "backup");
        s.Backup(bk, 7);
        Ok(Directory.Exists(bk) && Directory.GetFiles(bk, "horas-*.db").Length == 1, "backup do dia gerado (VACUUM INTO)");
        if (Directory.GetFiles(bk, "horas-*.db").Length == 1)
        {
            var sb = new Store(Directory.GetFiles(bk, "horas-*.db")[0]);
            Ok(sb.Empresas(false).Count == 3, "o backup abre e contém os dados");
            sb.Dispose();
        }

        Console.WriteLine("\n[relatório]");
        var regs = s.Periodo(D("2026-10-28 00:00:00"), D("2026-10-29 00:00:00"), 0, null);
        var res = Relatorio.Resumo(regs, rel.T);
        int somaLinhas = 0, somaRegs = 0;
        foreach (var l in res) somaLinhas += l.Total;
        foreach (var r in regs) somaRegs += r.Minutos(rel.T);
        Ok(somaLinhas == somaRegs && somaRegs > 0, "total do resumo = soma dos registros (" + Relatorio.Hm(somaRegs) + ")");
        Ok(res.Count == 2 && res[0].Empresa == "Empresa 01", "resumo agrupa por empresa em ordem alfabética");
        Ok(res[0].Min[1] == 120, "Empresa 01: 2:00 de visita separado do trabalho");
        Ok(s.Periodo(D("2026-10-28 00:00:00"), D("2026-10-29 00:00:00"), b, null).Count == 3, "filtro por empresa");
        Ok(regs.Find(delegate (Reg r) { return r.Inicio == D("2026-10-28 09:00:00"); }).Minutos(rel.T) == 40, "09:00:00 até 09:40:30 aparece como 09:00-09:40 e conta 40 min");
        Ok(regs.Find(delegate (Reg r) { return r.Inicio == D("2026-10-28 16:00:00"); }).Minutos(rel.T) == 2, "16:00:00 até 16:02:40 aparece como 16:00-16:02 e conta 2 min (bate com o que se vê)");
        Ok(regs.Find(delegate (Reg r) { return r.Inicio == D("2026-10-28 18:00:00"); }).Minutos(rel.T) == 1, "18:00:00 até 18:01:30 aparece como 18:00-18:01 e conta 1 min");
        Ok(s.Periodo(D("2026-10-28 00:00:00"), D("2026-10-29 00:00:00"), 0, Tipos.Visita).Count == 1, "filtro por tipo");
        string csv = Relatorio.Csv(regs, rel.T);
        Ok(csv.StartsWith("Data;Dia;Empresa;Tipo;In"), "CSV começa pelo cabeçalho com ';'");
        Ok(csv.Contains("Empresa 02;Trabalho (no PC);10:00;10:20;0:20;0,33;\"Respondeu \"\"não\"\"\""), "linha do CSV: 10:00 a 10:20 = 0:20 = 0,33 h (aspas escapadas)");
        Ok(csv.Contains("RESUMO POR EMPRESA") && csv.Contains("\nTOTAL;"), "CSV traz o resumo e o total");
        File.WriteAllText(Path.Combine(pasta, "exemplo.csv"), csv, new System.Text.UTF8Encoding(true));

        s.Dispose();
        Console.WriteLine("\n" + (total - falhas) + " de " + total + " verificações ok" + (falhas > 0 ? ", " + falhas + " FALHARAM" : ""));
        return falhas == 0 ? 0 : 1;
    }
}

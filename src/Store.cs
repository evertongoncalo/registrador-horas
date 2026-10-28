using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RegistradorHoras
{
    // Tipos de registro: o que muda é se o programa pergunta "ainda está aí?" e com que frequência.
    static class Tipos
    {
        public const string Trabalho = "trabalho";
        public const string Visita = "visita";
        public const string Deslocamento = "deslocamento";
        public static readonly string[] Todos = { Trabalho, Visita, Deslocamento };

        public static string Nome(string t)
        {
            switch (t)
            {
                case Visita: return "Visita no cliente";
                case Deslocamento: return "Deslocamento";
                default: return "Trabalho (no PC)";
            }
        }
    }

    // Como cada registro foi encerrado (útil para saber quais horários de saída foram automáticos).
    static class Origens
    {
        public static string Nome(string o)
        {
            switch (o)
            {
                case "manual": return "Parou manualmente";
                case "pergunta_nao": return "Respondeu \"não\"";
                case "sem_resposta": return "Sem resposta";
                case "desligamento": return "Desligou/fechou o programa";
                case "suspensao": return "PC suspenso/parado";
                case "queda": return "Recuperado após queda";
                case "troca": return "Troca de empresa";
                case "adicionado": return "Adicionado à mão";
                case "editado": return "Editado";
                default: return "";
            }
        }
    }

    class Empresa
    {
        public long Id;
        public string Nome;
        public bool Ativa;
        public override string ToString() { return Nome; }
    }

    class Reg
    {
        public long Id, EmpresaId;
        public string Empresa, Tipo, Origem, Obs;
        public DateTime Inicio;
        public DateTime? Fim, UltimoSinal;

        public bool Aberto { get { return !Fim.HasValue; } }

        // Duração em minutos inteiros, contada pelos horários como aparecem na tela (HH:mm):
        // 16:00 até 16:02 são 2 min, sempre. Os segundos ficam guardados no banco, mas não
        // entram na conta, assim a lista, os totais e o CSV nunca discordam do horário mostrado.
        public int Minutos(DateTime agora)
        {
            DateTime f = Fim.HasValue ? Fim.Value : agora;
            int m = (int)(Corta(f) - Corta(Inicio)).TotalMinutes;
            return m < 0 ? 0 : m;
        }

        static DateTime Corta(DateTime d) { return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0); }
    }

    class Store : IDisposable
    {
        public const string Fmt = "yyyy-MM-dd HH:mm:ss";
        public readonly Db Db;
        public readonly string Caminho;

        public Store(string caminho)
        {
            Caminho = caminho;
            Db = new Db(caminho);
            Migrar();
        }

        public static string F(DateTime d) { return d.ToString(Fmt, CultureInfo.InvariantCulture); }
        public static DateTime P(string s) { return DateTime.ParseExact(s, Fmt, CultureInfo.InvariantCulture); }
        static DateTime? PN(object o) { return o == null ? (DateTime?)null : P((string)o); }

        void Migrar()
        {
            Db.Exec("CREATE TABLE IF NOT EXISTS empresa (id INTEGER PRIMARY KEY AUTOINCREMENT, nome TEXT NOT NULL UNIQUE COLLATE NOCASE, ativa INTEGER NOT NULL DEFAULT 1)");
            Db.Exec("CREATE TABLE IF NOT EXISTS registro (id INTEGER PRIMARY KEY AUTOINCREMENT, empresa_id INTEGER NOT NULL REFERENCES empresa(id), tipo TEXT NOT NULL DEFAULT 'trabalho', inicio TEXT NOT NULL, fim TEXT, ultimo_sinal TEXT, origem_fim TEXT, obs TEXT)");
            Db.Exec("CREATE INDEX IF NOT EXISTS ix_registro_inicio ON registro(inicio)");
            // garante no máximo UM registro em andamento (fim vazio) por vez
            Db.Exec("CREATE UNIQUE INDEX IF NOT EXISTS ux_registro_aberto ON registro((1)) WHERE fim IS NULL");
            Db.Exec("CREATE TABLE IF NOT EXISTS config (chave TEXT PRIMARY KEY, valor TEXT)");
        }

        // ---------- empresas ----------

        public List<Empresa> Empresas(bool soAtivas)
        {
            var l = new List<Empresa>();
            foreach (var r in Db.Query("SELECT id, nome, ativa FROM empresa " + (soAtivas ? "WHERE ativa = 1 " : "") + "ORDER BY nome COLLATE NOCASE"))
                l.Add(new Empresa { Id = (long)r[0], Nome = (string)r[1], Ativa = (long)r[2] == 1 });
            return l;
        }

        public long AddEmpresa(string nome)
        {
            nome = (nome ?? "").Trim();
            if (nome.Length == 0) throw new ArgumentException("Informe o nome da empresa.");
            var r = Db.Query("SELECT id FROM empresa WHERE nome = ?", nome);
            if (r.Count > 0)
            {
                long id = (long)r[0][0];
                Db.Exec("UPDATE empresa SET ativa = 1 WHERE id = ?", id); // reativa se estava arquivada
                return id;
            }
            Db.Exec("INSERT INTO empresa(nome) VALUES (?)", nome);
            return Db.LastId;
        }

        public void RenomearEmpresa(long id, string nome)
        {
            nome = (nome ?? "").Trim();
            if (nome.Length == 0) throw new ArgumentException("Informe o nome da empresa.");
            if (Db.Query("SELECT id FROM empresa WHERE nome = ? AND id <> ?", nome, id).Count > 0)
                throw new ArgumentException("Já existe uma empresa com esse nome.");
            Db.Exec("UPDATE empresa SET nome = ? WHERE id = ?", nome, id);
        }

        // Arquivar esconde a empresa da lista, mas o histórico dela continua nos relatórios.
        public void ArquivarEmpresa(long id)
        {
            Db.Exec("UPDATE empresa SET ativa = 0 WHERE id = ?", id);
        }

        // ---------- registros ----------

        const string Sel = "SELECT r.id, r.empresa_id, e.nome, r.tipo, r.inicio, r.fim, r.ultimo_sinal, r.origem_fim, r.obs " +
                           "FROM registro r JOIN empresa e ON e.id = r.empresa_id ";

        static Reg Map(object[] r)
        {
            return new Reg
            {
                Id = (long)r[0],
                EmpresaId = (long)r[1],
                Empresa = (string)r[2],
                Tipo = (string)r[3],
                Inicio = P((string)r[4]),
                Fim = PN(r[5]),
                UltimoSinal = PN(r[6]),
                Origem = r[7] as string,
                Obs = r[8] as string
            };
        }

        public Reg Obter(long id)
        {
            var l = Db.Query(Sel + "WHERE r.id = ?", id);
            return l.Count == 0 ? null : Map(l[0]);
        }

        public Reg Aberto()
        {
            var l = Db.Query(Sel + "WHERE r.fim IS NULL");
            return l.Count == 0 ? null : Map(l[0]);
        }

        // Registros que COMEÇAM entre [de, ateExclusivo). empresaId 0 = todas; tipo null = todos.
        public List<Reg> Periodo(DateTime de, DateTime ateExclusivo, long empresaId, string tipo)
        {
            string sql = Sel + "WHERE r.inicio >= ? AND r.inicio < ?";
            var args = new List<object> { F(de), F(ateExclusivo) };
            if (empresaId > 0) { sql += " AND r.empresa_id = ?"; args.Add(empresaId); }
            if (!string.IsNullOrEmpty(tipo)) { sql += " AND r.tipo = ?"; args.Add(tipo); }
            sql += " ORDER BY r.inicio, r.id";
            var l = new List<Reg>();
            foreach (var r in Db.Query(sql, args.ToArray())) l.Add(Map(r));
            return l;
        }

        public long Iniciar(long empresaId, string tipo, DateTime inicio)
        {
            Db.Exec("INSERT INTO registro(empresa_id, tipo, inicio, ultimo_sinal) VALUES (?, ?, ?, ?)", empresaId, tipo, F(inicio), F(inicio));
            return Db.LastId;
        }

        public void Fechar(long id, DateTime fim, string origem)
        {
            Db.Exec("UPDATE registro SET fim = ?, origem_fim = ?, ultimo_sinal = ? WHERE id = ?", F(fim), origem, F(fim), id);
        }

        public void Sinal(long id, DateTime t)
        {
            Db.Exec("UPDATE registro SET ultimo_sinal = ? WHERE id = ? AND fim IS NULL", F(t), id);
        }

        public void Inserir(Reg r)
        {
            Db.Exec("INSERT INTO registro(empresa_id, tipo, inicio, fim, ultimo_sinal, origem_fim, obs) VALUES (?, ?, ?, ?, ?, ?, ?)",
                r.EmpresaId, r.Tipo, F(r.Inicio), r.Fim.HasValue ? F(r.Fim.Value) : null, r.Fim.HasValue ? F(r.Fim.Value) : null, r.Origem, r.Obs);
            r.Id = Db.LastId;
        }

        public void Atualizar(Reg r)
        {
            Db.Exec("UPDATE registro SET empresa_id = ?, tipo = ?, inicio = ?, fim = ?, origem_fim = ?, obs = ? WHERE id = ?",
                r.EmpresaId, r.Tipo, F(r.Inicio), r.Fim.HasValue ? F(r.Fim.Value) : null, r.Origem, r.Obs, r.Id);
        }

        public void Excluir(long id)
        {
            Db.Exec("DELETE FROM registro WHERE id = ?", id);
        }

        // Existe outro registro cruzando [ini, fim)? Registro em andamento conta até "agora".
        public bool Sobrepoe(DateTime ini, DateTime fim, long ignorarId, DateTime agora)
        {
            var r = Db.Query("SELECT COUNT(*) FROM registro WHERE id <> ? AND inicio < ? AND COALESCE(fim, ?) > ?",
                ignorarId, F(fim), F(agora), F(ini));
            return (long)r[0][0] > 0;
        }

        // ---------- configurações ----------

        public string Cfg(string chave, string padrao)
        {
            var r = Db.Query("SELECT valor FROM config WHERE chave = ?", chave);
            return r.Count == 0 ? padrao : (string)r[0][0];
        }

        public int CfgInt(string chave, int padrao)
        {
            int v;
            return int.TryParse(Cfg(chave, null), out v) ? v : padrao;
        }

        public void SetCfg(string chave, string valor)
        {
            Db.Exec("INSERT INTO config(chave, valor) VALUES (?, ?) ON CONFLICT(chave) DO UPDATE SET valor = excluded.valor", chave, valor);
        }

        // ---------- backup ----------

        // Uma cópia consistente do banco por dia, guardando as últimas N.
        public void Backup(string pasta, int manter)
        {
            try
            {
                Directory.CreateDirectory(pasta);
                string dest = Path.Combine(pasta, "horas-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".db");
                if (File.Exists(dest)) return;
                Db.Exec("VACUUM INTO ?", dest);
                string[] antigos = Directory.GetFiles(pasta, "horas-*.db");
                Array.Sort(antigos);
                for (int i = 0; i < antigos.Length - manter; i++) File.Delete(antigos[i]);
            }
            catch { }
        }

        public void Dispose() { Db.Dispose(); }
    }
}

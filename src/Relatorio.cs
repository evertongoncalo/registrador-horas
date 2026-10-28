using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RegistradorHoras
{
    // Totais e exportação. Tudo em minutos inteiros, como o Reg.Minutos().
    static class Relatorio
    {
        public static readonly CultureInfo Pt = new CultureInfo("pt-BR");

        public static string Hm(int min) { return (min / 60).ToString() + ":" + (min % 60).ToString("00"); }
        public static string Dec(int min) { return (min / 60.0).ToString("0.00", Pt); }
        public static string Hora(DateTime d) { return d.ToString("HH:mm", Pt); }
        public static string Dia(DateTime d) { return d.ToString("dd/MM/yyyy", Pt); }
        public static string DiaSemana(DateTime d) { return d.ToString("ddd", Pt); }

        public static string FimTxt(Reg r)
        {
            if (!r.Fim.HasValue) return "em andamento";
            if (r.Fim.Value.Date != r.Inicio.Date) return r.Fim.Value.ToString("dd/MM HH:mm", Pt);
            return Hora(r.Fim.Value);
        }

        public class Linha
        {
            public string Empresa;
            public int[] Min = new int[3]; // na ordem de Tipos.Todos
            public int Total { get { return Min[0] + Min[1] + Min[2]; } }
        }

        public static List<Linha> Resumo(List<Reg> regs, DateTime agora)
        {
            var d = new Dictionary<string, Linha>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var r in regs)
            {
                Linha l;
                if (!d.TryGetValue(r.Empresa, out l)) { l = new Linha { Empresa = r.Empresa }; d[r.Empresa] = l; }
                int i = Array.IndexOf(Tipos.Todos, r.Tipo);
                if (i < 0) i = 0;
                l.Min[i] += r.Minutos(agora);
            }
            var lista = new List<Linha>(d.Values);
            lista.Sort(delegate (Linha a, Linha b) { return string.Compare(a.Empresa, b.Empresa, StringComparison.CurrentCultureIgnoreCase); });
            return lista;
        }

        static string C(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        // CSV para abrir direto no Excel em português (separador ";", decimal com vírgula).
        public static string Csv(List<Reg> regs, DateTime agora)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Data;Dia;Empresa;Tipo;Início;Fim;Duração (h:mm);Horas (decimal);Encerrado por;Observação");
            foreach (var r in regs)
            {
                int m = r.Minutos(agora);
                sb.AppendLine(string.Join(";", new[] {
                    Dia(r.Inicio), DiaSemana(r.Inicio), C(r.Empresa), Tipos.Nome(r.Tipo), Hora(r.Inicio), FimTxt(r),
                    Hm(m), Dec(m), C(Origens.Nome(r.Origem)), C(r.Obs) }));
            }
            sb.AppendLine();
            sb.AppendLine("RESUMO POR EMPRESA");
            sb.AppendLine("Empresa;" + Tipos.Nome(Tipos.Trabalho) + ";" + Tipos.Nome(Tipos.Visita) + ";" + Tipos.Nome(Tipos.Deslocamento) + ";Total (h:mm);Total (horas decimal)");
            int[] tot = new int[3];
            foreach (var l in Resumo(regs, agora))
            {
                sb.AppendLine(C(l.Empresa) + ";" + Hm(l.Min[0]) + ";" + Hm(l.Min[1]) + ";" + Hm(l.Min[2]) + ";" + Hm(l.Total) + ";" + Dec(l.Total));
                for (int i = 0; i < 3; i++) tot[i] += l.Min[i];
            }
            int geral = tot[0] + tot[1] + tot[2];
            sb.AppendLine("TOTAL;" + Hm(tot[0]) + ";" + Hm(tot[1]) + ";" + Hm(tot[2]) + ";" + Hm(geral) + ";" + Dec(geral));
            return sb.ToString();
        }
    }
}

using System;
using System.IO;
using RegistradorHoras;

// Auxiliar do teste ponta a ponta (e2e.ps1): prepara e lê o banco usado pelo .exe de verdade.
//   E2eSeed.exe seed  <pasta>   cria empresa "Empresa Teste" + um registro deixado aberto (queda de energia simulada)
//   E2eSeed.exe vazio <pasta>   cria só a empresa
//   E2eSeed.exe dump  <pasta>   lista os registros
static class E2eSeed
{
    static DateTime Trunc(DateTime d) { return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second); }

    static int Main(string[] a)
    {
        Directory.CreateDirectory(a[1]);
        using (var s = new Store(Path.Combine(a[1], "horas.db")))
        {
            if (a[0] == "seed" || a[0] == "vazio")
            {
                long e = s.AddEmpresa("Empresa Teste");
                if (a[0] == "seed")
                {
                    DateTime ini = Trunc(DateTime.Now.AddHours(-2)), sinal = Trunc(DateTime.Now.AddMinutes(-30));
                    long id = s.Iniciar(e, Tipos.Trabalho, ini);
                    s.Sinal(id, sinal);
                    Console.WriteLine("SINAL=" + Store.F(sinal));
                }
            }
            else
            {
                foreach (var r in s.Db.Query("SELECT id, tipo, inicio, fim, ultimo_sinal, origem_fim FROM registro ORDER BY id"))
                    Console.WriteLine("REG|" + r[0] + "|" + r[1] + "|" + r[2] + "|" + (r[3] ?? "ABERTO") + "|" + r[4] + "|" + (r[5] ?? ""));
            }
        }
        return 0;
    }
}

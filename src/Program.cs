using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RegistradorHoras
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool primeiro;
            using (var mutex = new Mutex(true, @"Local\RegistradorHoras.Instancia", out primeiro))
            {
                if (!primeiro)
                {
                    // já está rodando (provavelmente na bandeja): só traz a janela para frente
                    try { EventWaitHandle.OpenExisting(@"Local\RegistradorHoras.Mostrar").Set(); } catch { }
                    return;
                }

                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Dados em %APPDATA%\RegistradorHoras (ou na pasta dada em --dados "caminho")
                string pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RegistradorHoras");
                int i = Array.IndexOf(args, "--dados");
                if (i >= 0 && i + 1 < args.Length) pasta = args[i + 1];

                Store store;
                try
                {
                    Directory.CreateDirectory(pasta);
                    store = new Store(Path.Combine(pasta, "horas.db"));
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Não foi possível abrir o banco de dados em:\n" + pasta + "\n\n" + ex.Message,
                        "Registrador de Horas", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                store.Backup(Path.Combine(pasta, "backup"), 14);

                var tracker = new Tracker(store);
                tracker.Pendente(); // registro deixado aberto por queda de energia? a tela pergunta o que fazer

                var form = new MainForm(tracker, Array.IndexOf(args, "--bandeja") >= 0);

                var mostrar = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\RegistradorHoras.Mostrar");
                var th = new Thread(delegate ()
                {
                    while (mostrar.WaitOne())
                    {
                        try { form.BeginInvoke(new Action(form.MostrarJanela)); }
                        catch { return; }
                    }
                });
                th.IsBackground = true;
                th.Start();

                Application.Run(form);
                store.Dispose();
            }
        }
    }
}

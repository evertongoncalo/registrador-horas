using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace RegistradorHoras
{
    // Base das janelinhas: mesma fonte, escala de DPI e aparência de diálogo.
    class Dialogo : Form
    {
        public Dialogo()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
        }

        protected Label Rotulo(string texto, int x, int y, int w, int h)
        {
            var l = new Label { Text = texto, Left = x, Top = y, Width = w, Height = h };
            Controls.Add(l);
            return l;
        }

        protected Button Botao(string texto, int x, int y, int w, int h, EventHandler clique)
        {
            var b = new Button { Text = texto, Left = x, Top = y, Width = w, Height = h, UseVisualStyleBackColor = true };
            if (clique != null) b.Click += clique;
            Controls.Add(b);
            return b;
        }

        public static void Erro(IWin32Window dono, string msg)
        {
            MessageBox.Show(dono, msg, "Registrador de Horas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Caixinha "digite um texto".
    static class Caixa
    {
        public static string Pedir(IWin32Window dono, string titulo, string rotulo, string inicial)
        {
            using (var f = new Dialogo())
            {
                f.Text = titulo;
                f.ClientSize = new Size(400, 140);
                var l = new Label { Text = rotulo, Left = 16, Top = 16, Width = 368, Height = 22 };
                var t = new TextBox { Text = inicial ?? "", Left = 16, Top = 44, Width = 368 };
                var ok = new Button { Text = "OK", Left = 204, Top = 90, Width = 86, Height = 32, DialogResult = DialogResult.OK };
                var cancela = new Button { Text = "Cancelar", Left = 298, Top = 90, Width = 86, Height = 32, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { l, t, ok, cancela });
                f.AcceptButton = ok;
                f.CancelButton = cancela;
                t.SelectAll();
                f.ActiveControl = t;
                return f.ShowDialog(dono) == DialogResult.OK ? t.Text.Trim() : null;
            }
        }
    }

    // A pergunta "ainda está trabalhando?". Não é modal: o relógio da tela principal segue normal
    // e o Tracker decide quando a falta de resposta para o relógio.
    class PerguntaForm : Dialogo
    {
        readonly Tracker T;
        readonly Label lblContagem;
        readonly Timer tm;
        readonly DateTime quando;
        bool liberado;

        public PerguntaForm(Tracker t)
        {
            T = t;
            Reg r = t.Atual;
            quando = t.PerguntaEm.Value;

            Text = "Registrador de Horas";
            ShowInTaskbar = true;
            TopMost = true;
            ControlBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 236);

            string pergunta;
            if (r.Tipo == Tipos.Trabalho) pergunta = "Ainda está trabalhando para " + r.Empresa + "?";
            else if (r.Tipo == Tipos.Visita) pergunta = "Ainda está em visita no cliente " + r.Empresa + "?";
            else pergunta = "Ainda está em deslocamento (" + r.Empresa + ")?";

            var lblP = Rotulo(pergunta, 20, 18, 460, 50);
            lblP.Font = new Font("Segoe UI Semibold", 13F);
            var lblI = Rotulo("Registro iniciado às " + Relatorio.Hora(r.Inicio) + ".  Pergunta feita às " + Relatorio.Hora(quando) + ".", 20, 72, 460, 22);
            lblI.ForeColor = Color.DimGray;
            lblContagem = Rotulo("", 20, 100, 460, 44);
            lblContagem.ForeColor = Color.FromArgb(170, 90, 0);

            var sim = Botao("Sim, continuo", 20, 158, 220, 52, delegate { Responder(true); });
            sim.Font = new Font("Segoe UI Semibold", 11F);
            sim.BackColor = Color.FromArgb(46, 160, 67);
            sim.ForeColor = Color.White;
            sim.FlatStyle = FlatStyle.Flat;
            sim.FlatAppearance.BorderSize = 0;
            sim.UseVisualStyleBackColor = false;
            var nao = Botao("Não, parei às " + Relatorio.Hora(quando), 260, 158, 220, 52, delegate { Responder(false); });
            nao.Font = new Font("Segoe UI", 10F);
            AcceptButton = sim;

            tm = new Timer { Interval = 500 };
            tm.Tick += delegate { Atualizar(); };
            tm.Start();
            Atualizar();
        }

        void Atualizar()
        {
            double resta = (quando.AddSeconds(T.RespostaSeg) - T.Agora).TotalSeconds;
            if (resta < 0) resta = 0;
            int s = (int)Math.Ceiling(resta);
            lblContagem.Text = "Sem resposta em " + (s / 60) + ":" + (s % 60).ToString("00") +
                               ", o relógio para e a saída fica registrada às " + Relatorio.Hora(quando) + ".";
        }

        void Responder(bool continua)
        {
            T.Responder(continua);
            Liberar();
            Close();
        }

        // Só fecha quando a decisão foi tomada (resposta ou fim por falta de resposta).
        public void Liberar() { liberado = true; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!liberado && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tm.Stop();
            tm.Dispose();
            base.OnFormClosed(e);
        }
    }

    // Aparece ao abrir o programa quando um registro ficou aberto (luz acabou, processo encerrado à força...).
    class RecuperacaoForm : Dialogo
    {
        public const int Encerrar = 0, EncerrarOutro = 1, Continuar = 2;
        public int Escolha;
        public DateTime Hora;
        readonly RadioButton r1, r2, r3;
        readonly DateTimePicker dt;
        readonly Reg reg;
        readonly DateTime agora, sinal;

        public RecuperacaoForm(Reg a, DateTime now)
        {
            reg = a;
            agora = now;
            sinal = a.UltimoSinal.HasValue ? a.UltimoSinal.Value : a.Inicio;
            Text = "Registro que ficou aberto";
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            ControlBox = false;
            ClientSize = new Size(540, 340);

            var l1 = Rotulo("O programa foi fechado sem encerrar este registro:", 20, 16, 500, 22);
            l1.Font = new Font("Segoe UI Semibold", 10F);
            Rotulo(a.Empresa + "  ·  " + Tipos.Nome(a.Tipo) + "\niniciado em " + a.Inicio.ToString("dd/MM/yyyy 'às' HH:mm") +
                   "\núltimo sinal de atividade: " + sinal.ToString("dd/MM/yyyy 'às' HH:mm:ss"), 20, 44, 500, 66);

            r1 = new RadioButton { Text = "Encerrar às " + Relatorio.Hora(sinal) + " (último sinal, recomendado)", Left = 24, Top = 126, Width = 490, Checked = true };
            r2 = new RadioButton { Text = "Encerrar em outro horário:", Left = 24, Top = 160, Width = 230 };
            dt = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy HH:mm",
                Left = 258,
                Top = 158,
                Width = 170,
                Value = new DateTime(sinal.Year, sinal.Month, sinal.Day, sinal.Hour, sinal.Minute, 0)
            };
            dt.ValueChanged += delegate { r2.Checked = true; };
            r3 = new RadioButton { Text = "Continuar contando (eu ainda estava nesse trabalho)", Left = 24, Top = 196, Width = 490 };
            Controls.AddRange(new Control[] { r1, r2, dt, r3 });

            var info = Rotulo("Se o PC foi desligado ou faltou energia, o último sinal costuma ser o horário certo da saída.", 20, 236, 500, 40);
            info.ForeColor = Color.DimGray;

            var ok = Botao("Confirmar", 394, 286, 126, 38, OK);
            AcceptButton = ok;
        }

        void OK(object s, EventArgs e)
        {
            if (r3.Checked) { Escolha = Continuar; DialogResult = DialogResult.OK; return; }
            if (r2.Checked)
            {
                Hora = dt.Value;
                if (Hora < reg.Inicio) { Erro(this, "O horário de saída não pode ser antes do início (" + reg.Inicio.ToString("dd/MM HH:mm") + ")."); return; }
                if (Hora > agora) { Erro(this, "O horário de saída não pode estar no futuro."); return; }
                Escolha = EncerrarOutro;
            }
            else
            {
                Hora = sinal;
                Escolha = Encerrar;
            }
            DialogResult = DialogResult.OK;
        }
    }

    // Cadastro de empresas: adicionar, renomear e arquivar (o histórico sempre fica).
    class EmpresasForm : Dialogo
    {
        readonly Store S;
        readonly Tracker T;
        readonly ListBox lb;
        readonly TextBox txt;
        public bool Mudou;

        public EmpresasForm(Store s, Tracker t)
        {
            S = s;
            T = t;
            Text = "Empresas";
            ClientSize = new Size(400, 410);
            lb = new ListBox { Left = 16, Top = 16, Width = 250, Height = 290, IntegralHeight = false };
            Controls.Add(lb);
            Botao("Renomear", 280, 16, 104, 32, Renomear);
            Botao("Arquivar", 280, 56, 104, 32, Arquivar);
            Rotulo("Arquivar tira a empresa da lista, mas o histórico dela continua nos relatórios.", 16, 312, 368, 40).ForeColor = Color.DimGray;
            txt = new TextBox { Left = 16, Top = 362, Width = 250 };
            Controls.Add(txt);
            var add = Botao("Adicionar", 280, 359, 104, 30, Adicionar);
            AcceptButton = add;
            Carregar();
        }

        void Carregar()
        {
            lb.Items.Clear();
            foreach (var e in S.Empresas(true)) lb.Items.Add(e);
        }

        void Adicionar(object s, EventArgs e)
        {
            try
            {
                S.AddEmpresa(txt.Text);
                txt.Clear();
                Mudou = true;
                Carregar();
            }
            catch (ArgumentException ex) { Erro(this, ex.Message); }
        }

        void Renomear(object s, EventArgs e)
        {
            var emp = lb.SelectedItem as Empresa;
            if (emp == null) return;
            string novo = Caixa.Pedir(this, "Renomear empresa", "Novo nome:", emp.Nome);
            if (novo == null) return;
            try
            {
                S.RenomearEmpresa(emp.Id, novo);
                Mudou = true;
                Carregar();
            }
            catch (ArgumentException ex) { Erro(this, ex.Message); }
        }

        void Arquivar(object s, EventArgs e)
        {
            var emp = lb.SelectedItem as Empresa;
            if (emp == null) return;
            if (T.Atual != null && T.Atual.EmpresaId == emp.Id) { Erro(this, "Esta empresa está com um registro em andamento. Pare o registro primeiro."); return; }
            if (MessageBox.Show(this, "Arquivar \"" + emp.Nome + "\"?\n\nEla some da lista de seleção; o histórico continua nos relatórios. " +
                "Para voltar, basta cadastrar o mesmo nome de novo.", "Arquivar empresa", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            S.ArquivarEmpresa(emp.Id);
            Mudou = true;
            Carregar();
        }
    }

    class ConfigForm : Dialogo
    {
        readonly Tracker T;
        readonly NumericUpDown nPc, nResp, nExt;

        public ConfigForm(Tracker t)
        {
            T = t;
            Text = "Configurações";
            ClientSize = new Size(500, 250);
            Rotulo("Perguntar \"ainda está trabalhando?\" a cada (min):", 20, 24, 350, 24);
            nPc = Num(380, 22, 1, 240, t.IntervaloPc);
            Rotulo("Se não responder em (min), o relógio para:", 20, 64, 350, 24);
            nResp = Num(380, 62, 1, 30, Math.Max(1, t.RespostaSeg / 60));
            Rotulo("Em visita/deslocamento, perguntar a cada (min):", 20, 104, 350, 24);
            nExt = Num(380, 102, 5, 720, t.IntervaloExterno);
            Rotulo("Quando para por falta de resposta, a saída fica no horário em que a pergunta apareceu.", 20, 146, 460, 40).ForeColor = Color.DimGray;
            var ok = Botao("Salvar", 262, 196, 100, 34, Salvar);
            AcceptButton = ok;
            Botao("Cancelar", 372, 196, 100, 34, delegate { DialogResult = DialogResult.Cancel; });
        }

        NumericUpDown Num(int x, int y, int min, int max, int valor)
        {
            var n = new NumericUpDown { Left = x, Top = y, Width = 90, Minimum = min, Maximum = max, Value = Math.Min(max, Math.Max(min, valor)) };
            Controls.Add(n);
            return n;
        }

        void Salvar(object s, EventArgs e)
        {
            T.IntervaloPc = (int)nPc.Value;
            T.RespostaSeg = (int)nResp.Value * 60;
            T.IntervaloExterno = (int)nExt.Value;
            T.SalvarConfig();
            if (T.Atual != null && T.PerguntaEm == null)
                T.ProximaPergunta = T.Agora.AddMinutes(T.IntervaloMin(T.Atual.Tipo));
            DialogResult = DialogResult.OK;
        }
    }

    // Adicionar um registro à mão (esqueci de iniciar, visita no cliente, etc.) ou editar um existente.
    class EdicaoForm : Dialogo
    {
        readonly Store S;
        readonly Tracker T;
        readonly Reg reg;
        readonly ComboBox cbEmp, cbTipo;
        readonly DateTimePicker dtIni, dtFim;
        readonly TextBox txtObs;

        public EdicaoForm(Store s, Tracker t, Reg existente, Empresa padrao)
        {
            S = s;
            T = t;
            reg = existente;
            Text = existente == null ? "Adicionar registro" : "Editar registro";
            ClientSize = new Size(440, 320);

            Rotulo("Empresa:", 20, 24, 90, 24);
            cbEmp = new ComboBox { Left = 120, Top = 20, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            Rotulo("Tipo:", 20, 62, 90, 24);
            cbTipo = new ComboBox { Left = 120, Top = 58, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            Rotulo("Início:", 20, 100, 90, 24);
            dtIni = Picker(120, 96);
            Rotulo("Fim:", 20, 138, 90, 24);
            dtFim = Picker(120, 134);
            Rotulo("Observação:", 20, 176, 90, 24);
            txtObs = new TextBox { Left = 120, Top = 172, Width = 300 };
            Controls.AddRange(new Control[] { cbEmp, cbTipo, txtObs });

            var lista = S.Empresas(true);
            if (existente != null && !lista.Exists(delegate (Empresa e) { return e.Id == existente.EmpresaId; }))
                lista.Add(new Empresa { Id = existente.EmpresaId, Nome = existente.Empresa, Ativa = false });
            foreach (var e in lista) cbEmp.Items.Add(e);
            foreach (var tp in Tipos.Todos) cbTipo.Items.Add(new TipoItem { Id = tp, Nome = Tipos.Nome(tp) });

            DateTime fim, ini;
            long empId = padrao != null ? padrao.Id : 0;
            string tipo = Tipos.Trabalho;
            if (existente != null)
            {
                empId = existente.EmpresaId;
                tipo = existente.Tipo;
                ini = existente.Inicio;
                fim = existente.Fim.HasValue ? existente.Fim.Value : T.Agora;
                txtObs.Text = existente.Obs ?? "";
            }
            else
            {
                fim = T.Agora;
                ini = fim.AddHours(-1);
            }
            dtIni.Value = Corta(ini);
            dtFim.Value = Corta(fim);
            for (int i = 0; i < cbEmp.Items.Count; i++) if (((Empresa)cbEmp.Items[i]).Id == empId) cbEmp.SelectedIndex = i;
            if (cbEmp.SelectedIndex < 0 && cbEmp.Items.Count > 0) cbEmp.SelectedIndex = 0;
            for (int i = 0; i < cbTipo.Items.Count; i++) if (((TipoItem)cbTipo.Items[i]).Id == tipo) cbTipo.SelectedIndex = i;

            Rotulo("Dica: para sair para o cliente, escolha \"Visita no cliente\" ou \"Deslocamento\".", 20, 214, 400, 40).ForeColor = Color.DimGray;
            var ok = Botao("Salvar", 222, 266, 96, 34, Salvar);
            AcceptButton = ok;
            Botao("Cancelar", 326, 266, 96, 34, delegate { DialogResult = DialogResult.Cancel; });
        }

        static DateTime Corta(DateTime d) { return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0); }

        DateTimePicker Picker(int x, int y)
        {
            var p = new DateTimePicker { Left = x, Top = y, Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy  HH:mm" };
            Controls.Add(p);
            return p;
        }

        void Salvar(object s, EventArgs e)
        {
            var emp = cbEmp.SelectedItem as Empresa;
            var tp = cbTipo.SelectedItem as TipoItem;
            if (emp == null || tp == null) { Erro(this, "Escolha a empresa e o tipo."); return; }
            DateTime ini = Corta(dtIni.Value), fim = Corta(dtFim.Value);
            if (fim <= ini) { Erro(this, "O fim precisa ser depois do início."); return; }
            if (S.Sobrepoe(ini, fim, reg == null ? 0 : reg.Id, T.Agora))
            { Erro(this, "Esse horário se sobrepõe a outro registro. Ajuste os horários."); return; }

            var r = reg ?? new Reg();
            r.EmpresaId = emp.Id;
            r.Empresa = emp.Nome;
            r.Tipo = tp.Id;
            r.Inicio = ini;
            r.Fim = fim;
            r.Obs = txtObs.Text.Trim().Length == 0 ? null : txtObs.Text.Trim();
            r.Origem = reg == null ? "adicionado" : "editado";
            if (reg == null) S.Inserir(r); else S.Atualizar(r);
            DialogResult = DialogResult.OK;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace RegistradorHoras
{
    class TipoItem
    {
        public string Id; // null = "todos" (nos filtros)
        public string Nome;
        public override string ToString() { return Nome; }
    }

    class MainForm : Form
    {
        static readonly Color Verde = Color.FromArgb(46, 160, 67);
        static readonly Color Vermelho = Color.FromArgb(200, 55, 55);
        static readonly Color Azul = Color.FromArgb(0, 110, 200);

        readonly Tracker T;
        readonly Store S;

        TabControl tabs;
        TabPage tabReg, tabRel;
        ComboBox cbEmpresa, cbTipo, cbRelEmpresa, cbRelTipo;
        Button btnPlay;
        Label lblRelogio, lblStatus, lblAviso, lblTotalHoje;
        DataGridView gridHoje, gridRel, gridResumo;
        DateTimePicker dtDe, dtAte;
        NotifyIcon tray;
        ToolStripMenuItem miParar, miAutoStart;
        Timer timer;
        PerguntaForm pergunta;
        Icon icoParado, icoAtivo;
        List<Reg> relRegs = new List<Reg>();
        bool podeMostrar, sair, avisouBandeja, carregando, fechandoPelaJanela;
        string aviso;
        int minutoListado = -1;

        public MainForm(Tracker tracker, bool iniciarNaBandeja)
        {
            T = tracker;
            S = tracker.S;
            podeMostrar = !iniciarNaBandeja;
            icoParado = Icones.Icone(Color.FromArgb(120, 120, 120));
            icoAtivo = Icones.Icone(Verde);

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.5F);
            Text = "Registrador de Horas";
            Icon = icoParado;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 700);
            MinimumSize = new Size(780, 620);

            ConstruirAbas();
            ConstruirMenu();
            ConstruirBandeja();

            carregando = true;
            long ultima;
            long.TryParse(S.Cfg("ultima_empresa", "0"), out ultima);
            CarregarEmpresas(ultima);
            string ultimoTipo = S.Cfg("ultimo_tipo", Tipos.Trabalho);
            for (int i = 0; i < cbTipo.Items.Count; i++) if (((TipoItem)cbTipo.Items[i]).Id == ultimoTipo) cbTipo.SelectedIndex = i;
            PeriodoHoje();
            carregando = false;

            T.Mudou += AoMudar;
            T.Perguntar += AoPerguntar;
            T.Encerrado += AoEncerrar;
            SystemEvents.SessionEnding += AoFimDeSessao;
            SystemEvents.PowerModeChanged += AoEnergia;

            timer = new Timer { Interval = 1000 };
            timer.Tick += Tique;
            timer.Start();

            IntPtr h = Handle; // cria o handle mesmo começando escondido na bandeja
            AoMudar();

            // depois que a janela estiver de pé: trata registro deixado aberto e primeira empresa
            var inicio = new Timer { Interval = 400 };
            inicio.Tick += delegate { inicio.Stop(); inicio.Dispose(); AoIniciar(); };
            inicio.Start();
        }

        // ================= construção da tela =================

        static Label Rot(string texto)
        {
            return new Label { Text = texto, AutoSize = true, Margin = new Padding(8, 7, 4, 0) };
        }

        static Button Botao(string texto, EventHandler clique)
        {
            var b = new Button { Text = texto, AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(4), UseVisualStyleBackColor = true };
            b.Click += clique;
            return b;
        }

        static DataGridView NovaGrade(params string[] colunas)
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            g.RowTemplate.Height = 26;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 252);
            foreach (var c in colunas) g.Columns.Add(c, c);
            foreach (DataGridViewColumn c in g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            return g;
        }

        void ConstruirAbas()
        {
            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
            tabReg = new TabPage("Registrar");
            tabRel = new TabPage("Relatório");
            tabs.TabPages.Add(tabReg);
            tabs.TabPages.Add(tabRel);
            tabs.SelectedIndexChanged += delegate
            {
                if (tabs.SelectedTab != tabRel) return;
                AtualizarRelatorio();
                // ao aparecer pela primeira vez a grade marca a 1ª linha sozinha; tira esse destaque
                BeginInvoke(new Action(delegate { gridRel.CurrentCell = null; gridRel.ClearSelection(); }));
            };
            Controls.Add(tabs);
            ConstruirRegistrar();
            ConstruirRelatorio();
        }

        void ConstruirRegistrar()
        {
            var topo = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(10, 16, 10, 8) };
            topo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var linha = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 6) };
            cbEmpresa = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, Margin = new Padding(0, 3, 4, 3) };
            var btnNova = Botao("+ Empresa", NovaEmpresa);
            btnNova.Margin = new Padding(0, 2, 18, 2);
            cbTipo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(0, 3, 0, 3) };
            cbTipo.Items.Add(new TipoItem { Id = Tipos.Trabalho, Nome = Tipos.Nome(Tipos.Trabalho) });
            cbTipo.Items.Add(new TipoItem { Id = Tipos.Visita, Nome = Tipos.Nome(Tipos.Visita) });
            cbTipo.Items.Add(new TipoItem { Id = Tipos.Deslocamento, Nome = Tipos.Nome(Tipos.Deslocamento) });
            cbTipo.SelectedIndex = 0;
            cbEmpresa.SelectedIndexChanged += delegate { AtualizarBotao(); };
            cbTipo.SelectedIndexChanged += delegate { AtualizarBotao(); };
            linha.Controls.Add(Rot("Empresa:"));
            linha.Controls.Add(cbEmpresa);
            linha.Controls.Add(btnNova);
            linha.Controls.Add(Rot("Tipo:"));
            linha.Controls.Add(cbTipo);
            topo.Controls.Add(linha);

            lblRelogio = new Label { Text = "00:00:00", AutoSize = true, Font = new Font("Segoe UI Light", 44F), ForeColor = Color.Gray, Anchor = AnchorStyles.None };
            topo.Controls.Add(lblRelogio);
            lblStatus = new Label { Text = "Parado", AutoSize = true, Anchor = AnchorStyles.None, Font = new Font("Segoe UI", 10.5F), Margin = new Padding(3, 0, 3, 2) };
            topo.Controls.Add(lblStatus);
            lblAviso = new Label { Text = "", AutoSize = true, Anchor = AnchorStyles.None, ForeColor = Color.FromArgb(180, 90, 0), Margin = new Padding(3, 0, 3, 6) };
            topo.Controls.Add(lblAviso);

            btnPlay = new Button
            {
                Text = "Iniciar",
                Size = new Size(300, 58),
                Anchor = AnchorStyles.None,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Verde,
                Font = new Font("Segoe UI Semibold", 14F),
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 4, 3, 10),
                UseVisualStyleBackColor = false
            };
            btnPlay.FlatAppearance.BorderSize = 0;
            btnPlay.Click += ClicarPlay;
            topo.Controls.Add(btnPlay);

            var baixo = new Panel { Dock = DockStyle.Fill };
            gridHoje = NovaGrade("Empresa", "Tipo", "Início", "Fim", "Duração", "Encerrado por");
            gridHoje.Columns[0].FillWeight = 160;
            gridHoje.Columns[1].FillWeight = 120;
            gridHoje.Columns[2].FillWeight = 60;
            gridHoje.Columns[3].FillWeight = 80;
            gridHoje.Columns[4].FillWeight = 70;
            gridHoje.Columns[5].FillWeight = 150;
            gridHoje.CellDoubleClick += delegate { Editar(gridHoje); };
            lblTotalHoje = new Label { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(8, 6, 0, 0), Font = new Font("Segoe UI Semibold", 9.5F) };
            var titulo = new Label { Text = "Hoje", Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 6, 0, 0), Font = new Font("Segoe UI Semibold", 10F) };
            baixo.Controls.Add(gridHoje);
            baixo.Controls.Add(lblTotalHoje);
            baixo.Controls.Add(BarraAcoes(gridHoje));
            baixo.Controls.Add(titulo);

            tabReg.Controls.Add(baixo);
            tabReg.Controls.Add(topo);
        }

        FlowLayoutPanel BarraAcoes(DataGridView g)
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4, 2, 4, 2) };
            f.Controls.Add(Botao("Adicionar registro à mão", delegate { AdicionarManual(); }));
            f.Controls.Add(Botao("Editar", delegate { Editar(g); }));
            f.Controls.Add(Botao("Excluir", delegate { Excluir(g); }));
            return f;
        }

        void ConstruirRelatorio()
        {
            var filtros = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6, 8, 6, 2) };
            dtDe = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Margin = new Padding(0, 3, 4, 3) };
            dtAte = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", Width = 110, Margin = new Padding(0, 3, 12, 3) };
            cbRelEmpresa = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(0, 3, 12, 3) };
            cbRelTipo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Margin = new Padding(0, 3, 12, 3) };
            cbRelTipo.Items.Add(new TipoItem { Id = null, Nome = "Todos os tipos" });
            foreach (var tp in Tipos.Todos) cbRelTipo.Items.Add(new TipoItem { Id = tp, Nome = Tipos.Nome(tp) });
            cbRelTipo.SelectedIndex = 0;
            filtros.Controls.Add(Rot("De:"));
            filtros.Controls.Add(dtDe);
            filtros.Controls.Add(Rot("até:"));
            filtros.Controls.Add(dtAte);
            filtros.Controls.Add(cbRelEmpresa);
            filtros.Controls.Add(cbRelTipo);
            dtDe.ValueChanged += delegate { AtualizarRelatorio(); };
            dtAte.ValueChanged += delegate { AtualizarRelatorio(); };
            cbRelEmpresa.SelectedIndexChanged += delegate { AtualizarRelatorio(); };
            cbRelTipo.SelectedIndexChanged += delegate { AtualizarRelatorio(); };

            var atalhos = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2, 0, 6, 4) };
            atalhos.Controls.Add(Botao("Hoje", delegate { PeriodoHoje(); }));
            atalhos.Controls.Add(Botao("Esta semana", delegate { PeriodoSemana(); }));
            atalhos.Controls.Add(Botao("Este mês", delegate { PeriodoMes(0); }));
            atalhos.Controls.Add(Botao("Mês passado", delegate { PeriodoMes(-1); }));
            var exp = Botao("Exportar para Excel (CSV)", ExportarCsv);
            exp.Margin = new Padding(24, 4, 4, 4);
            atalhos.Controls.Add(exp);

            gridRel = NovaGrade("Data", "Empresa", "Tipo", "Início", "Fim", "Duração", "Encerrado por", "Observação");
            gridRel.Columns[0].FillWeight = 105;
            gridRel.Columns[1].FillWeight = 130;
            gridRel.Columns[2].FillWeight = 110;
            gridRel.Columns[3].FillWeight = 55;
            gridRel.Columns[4].FillWeight = 75;
            gridRel.Columns[5].FillWeight = 60;
            gridRel.Columns[6].FillWeight = 130;
            gridRel.Columns[7].FillWeight = 110;
            gridRel.CellDoubleClick += delegate { Editar(gridRel); };

            gridResumo = NovaGrade("Empresa", Tipos.Nome(Tipos.Trabalho), Tipos.Nome(Tipos.Visita), Tipos.Nome(Tipos.Deslocamento), "Total (h:mm)", "Total (horas)");
            gridResumo.Dock = DockStyle.Fill;
            for (int i = 1; i < gridResumo.Columns.Count; i++) gridResumo.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            gridResumo.Columns[0].FillWeight = 160;
            gridResumo.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

            var painelResumo = new Panel { Dock = DockStyle.Bottom, Height = 190 };
            var tituloResumo = new Label { Text = "Resumo por empresa", Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 6, 0, 0), Font = new Font("Segoe UI Semibold", 10F) };
            painelResumo.Controls.Add(gridResumo);
            painelResumo.Controls.Add(tituloResumo);

            tabRel.Controls.Add(gridRel);
            tabRel.Controls.Add(BarraAcoes(gridRel));
            tabRel.Controls.Add(painelResumo);
            tabRel.Controls.Add(atalhos);
            tabRel.Controls.Add(filtros);
        }

        void ConstruirMenu()
        {
            var menu = new MenuStrip();
            var arq = new ToolStripMenuItem("Arquivo");
            var miEmp = new ToolStripMenuItem("Empresas...");
            miEmp.Click += delegate { GerenciarEmpresas(); };
            var miCfg = new ToolStripMenuItem("Configurações...");
            miCfg.Click += delegate { using (var f = new ConfigForm(T)) f.ShowDialog(this); AtualizarStatus(); };
            var miPasta = new ToolStripMenuItem("Abrir pasta dos dados (banco e backups)");
            miPasta.Click += delegate { Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(S.Caminho) + "\""); };
            miAutoStart = new ToolStripMenuItem("Iniciar junto com o Windows") { CheckOnClick = true, Checked = AutoStartLigado() };
            miAutoStart.Click += delegate { DefinirAutoStart(miAutoStart.Checked); };
            var miSair = new ToolStripMenuItem("Sair");
            miSair.Click += delegate { Sair(); };
            arq.DropDownItems.AddRange(new ToolStripItem[] { miEmp, miCfg, miPasta, new ToolStripSeparator(), miAutoStart, new ToolStripSeparator(), miSair });
            menu.Items.Add(arq);
            MainMenuStrip = menu;
            Controls.Add(menu);
        }

        void ConstruirBandeja()
        {
            var menu = new ContextMenuStrip();
            var miAbrir = new ToolStripMenuItem("Abrir");
            miAbrir.Click += delegate { MostrarJanela(); };
            miParar = new ToolStripMenuItem("Parar registro");
            miParar.Click += delegate { T.Parar("manual"); };
            var miSair = new ToolStripMenuItem("Sair");
            miSair.Click += delegate { Sair(); };
            menu.Items.AddRange(new ToolStripItem[] { miAbrir, miParar, new ToolStripSeparator(), miSair });
            tray = new NotifyIcon { Icon = icoParado, Text = "Registrador de Horas", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MostrarJanela(); };
        }

        // ================= empresas / seleção =================

        void CarregarEmpresas(long selecionar)
        {
            bool antes = carregando;
            carregando = true;
            var lista = S.Empresas(true);
            cbEmpresa.Items.Clear();
            cbRelEmpresa.Items.Clear();
            cbRelEmpresa.Items.Add(new Empresa { Id = 0, Nome = "Todas as empresas", Ativa = true });
            foreach (var e in lista) { cbEmpresa.Items.Add(e); cbRelEmpresa.Items.Add(e); }
            // empresas arquivadas ainda aparecem no filtro do relatório (histórico)
            foreach (var e in S.Empresas(false)) if (!e.Ativa) cbRelEmpresa.Items.Add(new Empresa { Id = e.Id, Nome = e.Nome + " (arquivada)", Ativa = false });
            cbEmpresa.SelectedIndex = -1;
            for (int i = 0; i < cbEmpresa.Items.Count; i++) if (((Empresa)cbEmpresa.Items[i]).Id == selecionar) cbEmpresa.SelectedIndex = i;
            if (cbEmpresa.SelectedIndex < 0 && cbEmpresa.Items.Count > 0) cbEmpresa.SelectedIndex = 0;
            cbRelEmpresa.SelectedIndex = 0;
            carregando = antes;
            AtualizarBotao();
        }

        Empresa EmpresaSelecionada() { return cbEmpresa.SelectedItem as Empresa; }
        string TipoSelecionado() { var t = cbTipo.SelectedItem as TipoItem; return t == null ? Tipos.Trabalho : t.Id; }

        void SelecionarCombos(Reg a)
        {
            bool antes = carregando;
            carregando = true;
            for (int i = 0; i < cbEmpresa.Items.Count; i++) if (((Empresa)cbEmpresa.Items[i]).Id == a.EmpresaId) cbEmpresa.SelectedIndex = i;
            for (int i = 0; i < cbTipo.Items.Count; i++) if (((TipoItem)cbTipo.Items[i]).Id == a.Tipo) cbTipo.SelectedIndex = i;
            carregando = antes;
        }

        void NovaEmpresa(object s, EventArgs e)
        {
            string nome = Caixa.Pedir(Visible ? this : null, "Nova empresa", "Nome da empresa:", "");
            if (string.IsNullOrEmpty(nome)) return;
            try
            {
                long id = S.AddEmpresa(nome);
                CarregarEmpresas(id);
                AtualizarRelatorio();
            }
            catch (ArgumentException ex) { Dialogo.Erro(this, ex.Message); }
        }

        void GerenciarEmpresas()
        {
            long atual = EmpresaSelecionada() == null ? 0 : EmpresaSelecionada().Id;
            using (var f = new EmpresasForm(S, T))
            {
                f.ShowDialog(this);
                if (f.Mudou) { CarregarEmpresas(atual); AtualizarTudo(); }
            }
        }

        // ================= play / parar / trocar =================

        void ClicarPlay(object s, EventArgs e)
        {
            var emp = EmpresaSelecionada();
            if (emp == null) { NovaEmpresa(null, null); return; }
            string tipo = TipoSelecionado();
            if (T.Atual != null && T.Atual.EmpresaId == emp.Id && T.Atual.Tipo == tipo)
            {
                T.Parar("manual");
            }
            else
            {
                aviso = null;
                T.Iniciar(emp.Id, tipo);
            }
            S.SetCfg("ultima_empresa", emp.Id.ToString());
            S.SetCfg("ultimo_tipo", tipo);
        }

        void AtualizarBotao()
        {
            if (btnPlay == null || carregando) return;
            var emp = EmpresaSelecionada();
            var a = T.Atual;
            btnPlay.Enabled = true;
            if (emp == null)
            {
                btnPlay.Text = "Cadastre uma empresa para começar";
                btnPlay.BackColor = Color.Gray;
            }
            else if (a == null)
            {
                btnPlay.Text = "▶   Iniciar";
                btnPlay.BackColor = Verde;
            }
            else if (a.EmpresaId == emp.Id && a.Tipo == TipoSelecionado())
            {
                btnPlay.Text = "■   Parar";
                btnPlay.BackColor = Vermelho;
            }
            else
            {
                btnPlay.Text = "⇄   Trocar para " + emp.Nome + (TipoSelecionado() == Tipos.Trabalho ? "" : " (" + Tipos.Nome(TipoSelecionado()).ToLower() + ")");
                btnPlay.BackColor = Azul;
            }
        }

        // ================= eventos do cronômetro =================

        void AoMudar()
        {
            var a = T.Atual;
            if (a != null) SelecionarCombos(a);
            AtualizarBotao();
            AtualizarStatus();
            AtualizarRelogio();
            AtualizarHoje();
            if (tabs.SelectedTab == tabRel) AtualizarRelatorio();
            Icon = a != null ? icoAtivo : icoParado;
            tray.Icon = a != null ? icoAtivo : icoParado;
            miParar.Visible = a != null;
            if (pergunta != null && !pergunta.IsDisposed && (a == null || T.PerguntaEm == null))
            {
                pergunta.Liberar();
                pergunta.Close();
            }
        }

        void AoPerguntar()
        {
            if (pergunta != null && !pergunta.IsDisposed) return;
            pergunta = new PerguntaForm(T);
            pergunta.FormClosed += delegate { pergunta = null; };
            pergunta.Show();
            pergunta.Activate();
            System.Media.SystemSounds.Exclamation.Play();
        }

        void AoEncerrar(string mensagem)
        {
            aviso = mensagem;
            AtualizarStatus();
            tray.ShowBalloonTip(8000, "Registrador de Horas", mensagem, ToolTipIcon.Warning);
        }

        void AoIniciar()
        {
            Reg a = T.Atual != null && T.Recuperando ? T.Atual : null;
            if (a != null) Recuperar(a);
            if (Visible && cbEmpresa.Items.Count == 0) NovaEmpresa(null, null);
        }

        void Recuperar(Reg a)
        {
            using (var f = new RecuperacaoForm(a, T.Agora))
            {
                f.ShowDialog();
                if (f.Escolha == RecuperacaoForm.Continuar) T.RecuperarContinuar();
                else T.RecuperarEncerrar(f.Hora);
            }
            AoMudar();
        }

        void Tique(object s, EventArgs e)
        {
            T.Tick();
            AtualizarRelogio();
            // a duração do registro em andamento só muda de minuto em minuto
            if (T.Atual != null && Visible && T.Agora.Minute != minutoListado)
            {
                AtualizarHoje();
                if (tabs.SelectedTab == tabRel) AtualizarRelatorio();
            }
        }

        // ================= atualização da tela =================

        void AtualizarRelogio()
        {
            var a = T.Atual;
            if (a == null)
            {
                lblRelogio.Text = "00:00:00";
                lblRelogio.ForeColor = Color.Gray;
                Text = "Registrador de Horas";
                tray.Text = "Registrador de Horas — parado";
                return;
            }
            TimeSpan d = T.Agora - a.Inicio;
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            string tempo = string.Format("{0:00}:{1:00}:{2:00}", (int)d.TotalHours, d.Minutes, d.Seconds);
            lblRelogio.Text = tempo;
            lblRelogio.ForeColor = Verde;
            Text = tempo + " · " + a.Empresa + " — Registrador de Horas";
            string t = tempo + " · " + a.Empresa;
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void AtualizarStatus()
        {
            var a = T.Atual;
            if (a == null)
            {
                lblStatus.Text = "Parado";
            }
            else
            {
                string quem = a.Tipo == Tipos.Trabalho ? "Trabalhando para " + a.Empresa : Tipos.Nome(a.Tipo) + " · " + a.Empresa;
                string prox = T.Recuperando ? "aguardando sua decisão sobre este registro"
                            : T.PerguntaEm == null ? "próxima pergunta às " + Relatorio.Hora(T.ProximaPergunta)
                            : "aguardando sua resposta";
                lblStatus.Text = quem + "   ·   desde " + Relatorio.Hora(a.Inicio) + "   ·   " + prox;
            }
            lblAviso.Text = aviso ?? "";
            lblAviso.Visible = !string.IsNullOrEmpty(aviso);
        }

        void Preencher(DataGridView g, List<Reg> regs, bool comData)
        {
            // guarda qual registro estava selecionado, para não perder a seleção ao recarregar a lista
            long selId = 0;
            var selReg = g.CurrentRow == null ? null : g.CurrentRow.Tag as Reg;
            if (selReg != null) selId = selReg.Id;
            g.Rows.Clear();
            DateTime agora = T.Agora;
            foreach (var r in regs)
            {
                object[] v;
                if (comData)
                    v = new object[] { Relatorio.Dia(r.Inicio) + " (" + Relatorio.DiaSemana(r.Inicio) + ")", r.Empresa, Tipos.Nome(r.Tipo), Relatorio.Hora(r.Inicio), Relatorio.FimTxt(r), Relatorio.Hm(r.Minutos(agora)), Origens.Nome(r.Origem), r.Obs };
                else
                    v = new object[] { r.Empresa, Tipos.Nome(r.Tipo), Relatorio.Hora(r.Inicio), Relatorio.FimTxt(r), Relatorio.Hm(r.Minutos(agora)), Origens.Nome(r.Origem) };
                int i = g.Rows.Add(v);
                var row = g.Rows[i];
                row.Tag = r;
                if (r.Aberto)
                {
                    row.DefaultCellStyle.ForeColor = Color.FromArgb(25, 125, 55);
                    row.DefaultCellStyle.Font = new Font(g.Font, FontStyle.Bold);
                }
            }
            g.CurrentCell = null;
            g.ClearSelection();
            if (selId != 0)
                foreach (DataGridViewRow row in g.Rows)
                {
                    var rr = row.Tag as Reg;
                    if (rr != null && rr.Id == selId) { g.CurrentCell = row.Cells[0]; row.Selected = true; break; }
                }
        }

        void AtualizarHoje()
        {
            minutoListado = T.Agora.Minute;
            DateTime hoje = T.Agora.Date;
            var regs = S.Periodo(hoje, hoje.AddDays(1), 0, null);
            Preencher(gridHoje, regs, false);
            if (regs.Count == 0) { lblTotalHoje.Text = "Nada registrado hoje."; return; }
            var partes = new List<string>();
            int tot = 0;
            foreach (var l in Relatorio.Resumo(regs, T.Agora)) { partes.Add(l.Empresa + ": " + Relatorio.Hm(l.Total)); tot += l.Total; }
            lblTotalHoje.Text = string.Join("      ", partes.ToArray()) + "      |      Total do dia: " + Relatorio.Hm(tot);
        }

        void AtualizarRelatorio()
        {
            if (carregando || gridRel == null || cbRelEmpresa.SelectedItem == null) return;
            DateTime de = dtDe.Value.Date, ate = dtAte.Value.Date;
            var emp = cbRelEmpresa.SelectedItem as Empresa;
            var tp = cbRelTipo.SelectedItem as TipoItem;
            relRegs = S.Periodo(de, ate.AddDays(1), emp == null ? 0 : emp.Id, tp == null ? null : tp.Id);
            Preencher(gridRel, relRegs, true);

            gridResumo.Rows.Clear();
            int[] tot = new int[3];
            foreach (var l in Relatorio.Resumo(relRegs, T.Agora))
            {
                gridResumo.Rows.Add(l.Empresa, Relatorio.Hm(l.Min[0]), Relatorio.Hm(l.Min[1]), Relatorio.Hm(l.Min[2]), Relatorio.Hm(l.Total), Relatorio.Dec(l.Total));
                for (int i = 0; i < 3; i++) tot[i] += l.Min[i];
            }
            if (gridResumo.Rows.Count > 0)
            {
                int geral = tot[0] + tot[1] + tot[2];
                int idx = gridResumo.Rows.Add("TOTAL", Relatorio.Hm(tot[0]), Relatorio.Hm(tot[1]), Relatorio.Hm(tot[2]), Relatorio.Hm(geral), Relatorio.Dec(geral));
                gridResumo.Rows[idx].DefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
                gridResumo.Rows[idx].DefaultCellStyle.BackColor = Color.FromArgb(232, 240, 250);
            }
            gridResumo.ClearSelection();
        }

        void AtualizarTudo()
        {
            AtualizarHoje();
            AtualizarRelatorio();
            AtualizarStatus();
        }

        // ================= períodos do relatório =================

        void DefinirPeriodo(DateTime de, DateTime ate)
        {
            bool antes = carregando;
            carregando = true;
            dtDe.Value = de;
            dtAte.Value = ate;
            carregando = antes;
            AtualizarRelatorio();
        }

        void PeriodoHoje() { DefinirPeriodo(T.Agora.Date, T.Agora.Date); }

        void PeriodoSemana()
        {
            DateTime h = T.Agora.Date;
            int dias = ((int)h.DayOfWeek + 6) % 7; // semana começa na segunda
            DefinirPeriodo(h.AddDays(-dias), h.AddDays(6 - dias));
        }

        void PeriodoMes(int delta)
        {
            DateTime h = T.Agora.Date;
            DateTime ini = new DateTime(h.Year, h.Month, 1).AddMonths(delta);
            DefinirPeriodo(ini, ini.AddMonths(1).AddDays(-1));
        }

        // ================= adicionar / editar / excluir / exportar =================

        void AdicionarManual()
        {
            using (var f = new EdicaoForm(S, T, null, EmpresaSelecionada()))
                if (f.ShowDialog(this) == DialogResult.OK) AtualizarTudo();
        }

        Reg Selecionado(DataGridView g)
        {
            if (g.CurrentRow == null) { Dialogo.Erro(this, "Clique em um registro da lista primeiro."); return null; }
            var r = g.CurrentRow.Tag as Reg;
            if (r != null && r.Aberto) { Dialogo.Erro(this, "Esse registro ainda está em andamento. Pare o relógio antes de editar ou excluir."); return null; }
            return r;
        }

        void Editar(DataGridView g)
        {
            var r = Selecionado(g);
            if (r == null) return;
            using (var f = new EdicaoForm(S, T, S.Obter(r.Id), EmpresaSelecionada()))
                if (f.ShowDialog(this) == DialogResult.OK) AtualizarTudo();
        }

        void Excluir(DataGridView g)
        {
            var r = Selecionado(g);
            if (r == null) return;
            string txt = r.Empresa + "\n" + Relatorio.Dia(r.Inicio) + "  " + Relatorio.Hora(r.Inicio) + " → " + Relatorio.FimTxt(r) + "  (" + Relatorio.Hm(r.Minutos(T.Agora)) + ")";
            if (MessageBox.Show(this, "Excluir este registro?\n\n" + txt, "Excluir", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            S.Excluir(r.Id);
            AtualizarTudo();
        }

        void ExportarCsv(object sender, EventArgs e)
        {
            if (relRegs.Count == 0) { Dialogo.Erro(this, "Não há registros neste período para exportar."); return; }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Planilha CSV (abre no Excel)|*.csv";
                dlg.FileName = "horas_" + dtDe.Value.ToString("yyyy-MM-dd") + "_a_" + dtAte.Value.ToString("yyyy-MM-dd") + ".csv";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dlg.FileName, Relatorio.Csv(relRegs, T.Agora), new UTF8Encoding(true));
                }
                catch (Exception ex) { Dialogo.Erro(this, "Não foi possível salvar o arquivo:\n" + ex.Message); return; }
                if (MessageBox.Show(this, "Relatório salvo.\n\nAbrir agora?", "Exportar", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Process.Start(dlg.FileName);
            }
        }

        // ================= bandeja, fechar, desligar =================

        protected override void SetVisibleCore(bool value)
        {
            if (!podeMostrar) value = false; // começa escondido quando iniciado com --bandeja
            base.SetVisibleCore(value);
        }

        public void MostrarJanela()
        {
            podeMostrar = true;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        public void Sair()
        {
            if (T.Atual != null)
            {
                MostrarJanela();
                var r = MessageBox.Show(this, "Há um registro em andamento (" + T.Atual.Empresa + ", desde " + Relatorio.Hora(T.Atual.Inicio) + ").\n\nEncerrar agora e sair?",
                    "Sair", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
                T.Parar("manual");
            }
            sair = true;
            Application.Exit();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // na primeira exibição o DataGridView marca a 1ª linha sozinho; tira esse destaque sem ninguém ter clicado
            BeginInvoke(new Action(delegate { gridHoje.CurrentCell = null; gridHoje.ClearSelection(); }));
        }

        // O WinForms "gruda" o motivo do fechamento depois de um aviso de desligamento (mesmo cancelado),
        // então o clique no X (ou Alt+F4) é reconhecido pela própria mensagem WM_CLOSE.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0010) fechandoPelaJanela = true; // WM_CLOSE
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool doX = fechandoPelaJanela || e.CloseReason == CloseReason.UserClosing;
            fechandoPelaJanela = false;
            if (!sair && doX)
            {
                // o X só esconde a janela: o cronômetro e as perguntas continuam em segundo plano
                e.Cancel = true;
                Hide();
                if (!avisouBandeja)
                {
                    avisouBandeja = true;
                    tray.ShowBalloonTip(4000, "Registrador de Horas", "Continua rodando aqui na bandeja. Clique no ícone para abrir.", ToolTipIcon.Info);
                }
                return;
            }
            // Windows desligando / saindo da sessão / encerrado por outro motivo: fecha o registro agora
            if (!sair) T.Desligar();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.SessionEnding -= AoFimDeSessao;
            SystemEvents.PowerModeChanged -= AoEnergia;
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }

        void AoFimDeSessao(object s, SessionEndingEventArgs e)
        {
            T.Desligar();
        }

        void AoEnergia(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) T.Suspender();
        }

        // ================= iniciar com o Windows =================

        const string ChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string NomeRun = "RegistradorHoras";

        static bool AutoStartLigado()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ChaveRun))
                    return k != null && k.GetValue(NomeRun) != null;
            }
            catch { return false; }
        }

        void DefinirAutoStart(bool ligar)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ChaveRun, true))
                {
                    if (ligar) k.SetValue(NomeRun, "\"" + Application.ExecutablePath + "\" --bandeja");
                    else k.DeleteValue(NomeRun, false);
                }
            }
            catch (Exception ex)
            {
                Dialogo.Erro(this, "Não foi possível alterar a inicialização com o Windows:\n" + ex.Message);
                miAutoStart.Checked = AutoStartLigado();
            }
        }
    }
}

using System;

namespace RegistradorHoras
{
    // Regras do cronômetro, sem nenhuma tela: iniciar/parar/trocar, a pergunta
    // "ainda está trabalhando?", suspensão do PC, desligamento e recuperação após queda.
    // O relógio é injetável (Clock) para poder testar 20 minutos em milissegundos.
    class Tracker
    {
        public readonly Store S;
        public Func<DateTime> Clock;

        public Reg Atual;                 // registro em andamento (null = parado)
        public DateTime ProximaPergunta;  // quando a próxima pergunta aparece
        public DateTime? PerguntaEm;      // momento em que a pergunta foi feita (null = sem pergunta pendente)
        public bool Recuperando;          // há um registro deixado aberto esperando decisão do usuário

        public int IntervaloPc = 20;        // minutos entre perguntas em trabalho no PC
        public int IntervaloExterno = 120;  // minutos entre perguntas em visita/deslocamento
        public int RespostaSeg = 120;       // segundos esperando resposta antes de parar o relógio
        public int LimiteSaltoSeg = 120;    // salto maior que isso entre dois ticks = PC parou/suspendeu

        public event Action Mudou;          // iniciou, parou, trocou, respondeu...
        public event Action Perguntar;      // a tela deve mostrar a pergunta
        public event Action<string> Encerrado; // encerrou sozinho (mensagem explicando o porquê)

        DateTime ultimoTick = DateTime.MinValue;
        DateTime ultimoSinal = DateTime.MinValue;

        public Tracker(Store store)
        {
            S = store;
            Clock = delegate { return DateTime.Now; };
            CarregarConfig();
        }

        public DateTime Agora
        {
            get
            {
                DateTime d = Clock();
                return new DateTime(d.Ticks - d.Ticks % TimeSpan.TicksPerSecond); // sem milissegundos
            }
        }

        public void CarregarConfig()
        {
            IntervaloPc = S.CfgInt("intervalo_pc", 20);
            IntervaloExterno = S.CfgInt("intervalo_externo", 120);
            RespostaSeg = S.CfgInt("resposta_seg", 120);
        }

        public void SalvarConfig()
        {
            S.SetCfg("intervalo_pc", IntervaloPc.ToString());
            S.SetCfg("intervalo_externo", IntervaloExterno.ToString());
            S.SetCfg("resposta_seg", RespostaSeg.ToString());
        }

        public int IntervaloMin(string tipo)
        {
            return tipo == Tipos.Trabalho ? IntervaloPc : IntervaloExterno;
        }

        void Notificar() { if (Mudou != null) Mudou(); }

        // ---------- iniciar / parar ----------

        // Inicia um registro. Se já houver um em andamento, encerra-o agora e começa o novo (troca).
        public void Iniciar(long empresaId, string tipo)
        {
            DateTime now = Agora;
            Reg anterior = Atual;
            long id = 0;
            S.Db.Transacao(delegate
            {
                if (anterior != null) S.Fechar(anterior.Id, now < anterior.Inicio ? anterior.Inicio : now, "troca");
                id = S.Iniciar(empresaId, tipo, now);
            });
            Atual = S.Obter(id);
            PerguntaEm = null;
            Recuperando = false;
            ProximaPergunta = now.AddMinutes(IntervaloMin(tipo));
            ultimoSinal = now;
            ultimoTick = now;
            Notificar();
        }

        public void Parar(string origem)
        {
            if (Atual == null) return;
            PararEm(Agora, origem);
        }

        void PararEm(DateTime fim, string origem)
        {
            if (Atual == null) return;
            if (fim < Atual.Inicio) fim = Atual.Inicio;
            S.Fechar(Atual.Id, fim, origem);
            Atual = null;
            PerguntaEm = null;
            Notificar();
        }

        // ---------- passagem do tempo ----------

        // Chamado uma vez por segundo pela tela.
        public void Tick()
        {
            DateTime now = Agora;
            if (Atual == null || Recuperando) { ultimoTick = now; return; }

            // O relógio "pulou": o PC suspendeu/hibernou sem avisar (ou a hora foi mudada).
            // O trabalho vale até o último instante em que sabemos que estava ligado.
            if (ultimoTick != DateTime.MinValue && (now - ultimoTick).TotalSeconds > LimiteSaltoSeg)
            {
                Reg a = Atual;
                DateTime ate = ultimoTick;
                ultimoTick = now;
                PararEm(ate, "suspensao");
                if (Encerrado != null)
                    Encerrado(string.Format("O computador ficou parado/suspenso. O registro de {0} foi encerrado às {1:HH:mm}.", a.Empresa, ate));
                return;
            }
            ultimoTick = now;

            // batimento: grava que o programa estava vivo, para recuperar o horário se faltar energia
            if ((now - ultimoSinal).TotalSeconds >= 30)
            {
                S.Sinal(Atual.Id, now);
                ultimoSinal = now;
            }

            if (PerguntaEm == null)
            {
                if (now >= ProximaPergunta)
                {
                    PerguntaEm = now;
                    if (Perguntar != null) Perguntar();
                }
            }
            else if ((now - PerguntaEm.Value).TotalSeconds >= RespostaSeg)
            {
                // ninguém respondeu: o relógio para e a saída fica no horário em que a pergunta foi feita
                Reg a = Atual;
                DateTime q = PerguntaEm.Value;
                PararEm(q, "sem_resposta");
                if (Encerrado != null)
                    Encerrado(string.Format("Sem resposta à pergunta das {0:HH:mm}: o registro de {1} foi encerrado às {0:HH:mm}.", q, a.Empresa));
            }
        }

        // Resposta à pergunta: continua trabalhando (true) ou parou (false).
        public void Responder(bool continua)
        {
            if (Atual == null || PerguntaEm == null) return;
            if (continua)
            {
                PerguntaEm = null;
                ProximaPergunta = Agora.AddMinutes(IntervaloMin(Atual.Tipo));
                Notificar();
            }
            else
            {
                PararEm(PerguntaEm.Value, "pergunta_nao"); // saída = horário em que perguntou
            }
        }

        // ---------- eventos do sistema ----------

        // Windows desligando / fim de sessão / programa sendo fechado: encerra agora.
        public void Desligar()
        {
            if (Atual != null) PararEm(Agora, "desligamento");
        }

        // PC indo dormir/hibernar.
        public void Suspender()
        {
            if (Atual == null) return;
            Reg a = Atual;
            DateTime now = Agora;
            PararEm(now, "suspensao");
            if (Encerrado != null)
                Encerrado(string.Format("O computador foi suspenso. O registro de {0} foi encerrado às {1:HH:mm}.", a.Empresa, now));
        }

        // ---------- recuperação (queda de energia, processo morto) ----------

        // No início do programa: existe registro que ficou aberto? Se sim, aguarda decisão do usuário.
        public Reg Pendente()
        {
            Reg a = S.Aberto();
            if (a != null)
            {
                Atual = a;
                Recuperando = true;
                PerguntaEm = null;
            }
            ultimoTick = Agora;
            return a;
        }

        public void RecuperarEncerrar(DateTime fim)
        {
            Recuperando = false;
            PararEm(fim, "queda");
        }

        public void RecuperarContinuar()
        {
            Recuperando = false;
            DateTime now = Agora;
            PerguntaEm = null;
            ProximaPergunta = now.AddMinutes(IntervaloMin(Atual.Tipo));
            ultimoSinal = now;
            ultimoTick = now;
            Notificar();
        }
    }
}

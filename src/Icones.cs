using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace RegistradorHoras
{
    // Ícone de relógio desenhado em código (cinza = parado, verde = contando).
    static class Icones
    {
        public static Bitmap Desenho(int tam, Color cor)
        {
            var bmp = new Bitmap(tam, tam);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float m = tam * 0.05f;
                using (var b = new SolidBrush(cor))
                    g.FillEllipse(b, m, m, tam - 2 * m, tam - 2 * m);
                float c = tam / 2f;
                using (var p = new Pen(Color.White, Math.Max(1.5f, tam * 0.09f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawLine(p, c, c, c, tam * 0.22f);                 // ponteiro dos minutos
                    g.DrawLine(p, c, c, tam * 0.70f, c + tam * 0.10f);   // ponteiro das horas
                }
            }
            return bmp;
        }

        public static Icon Icone(Color cor)
        {
            using (var bmp = Desenho(32, cor))
                return Icon.FromHandle(bmp.GetHicon());
        }
    }
}

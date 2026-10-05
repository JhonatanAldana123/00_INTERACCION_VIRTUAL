using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FlujoAR
{
    // ── Datos del animador ────────────────────────────────────────
    // El trabajo de cada pieza animada vive en 001_ANIMACIÓN\<Pieza>\:
    //   animacion.json            la receta (grupos y movimientos), se guarda sola con cada cambio
    //   02_IMPORTAR\<Pieza>.glb   la pieza de Rhino con el mapeo reparado
    //   _animador\                vistas y mapas de piezas para hacer clic (scripts\animador.py analizar)
    //   04_BLENDER, 05_EXPORTAR, 06_PREVIEW   lo que crea scripts\animador.py generar
    class PiezaAnim
    {
        public string Nombre, Material;
        public int Triangulos, Pixeles;
        public double[] Min = new double[3], Max = new double[3];
        // Por vista: rectángulo en la imagen (x0, y0, x1, y1) y profundidad, para el menú del clic derecho
        public Dictionary<string, double[]> Rects = new Dictionary<string, double[]>();
    }

    class VistaAnim
    {
        public string Nombre;
        public Bitmap Imagen;
        public ushort[] Ids;   // 0 = fondo; k = pieza k-1
    }

    class AnalisisAnim
    {
        public int Ancho, Alto;
        public List<PiezaAnim> Piezas = new List<PiezaAnim>();
        public List<VistaAnim> Vistas = new List<VistaAnim>();
        public Dictionary<string, int> Indice = new Dictionary<string, int>();
    }

    class GrupoAnim
    {
        public string Nombre = "Paso", Tipo = "bisagra", Lado = "izquierda", Borde = "abajo", Desde = "arriba", Empieza = "despues";
        public double Angulo = 90, Distancia = 30, Duracion = 1, Retraso = 0;
        public bool Aparece = true;
        public bool Bloqueado;   // sus piezas no se pueden quitar ni tomar desde otro grupo
        // Movimiento encadenado: el grupo viaja con su padre (p. ej. laterales pegados al espaldar) y su propio
        // movimiento se suma encima. Id es fijo aunque el grupo cambie de nombre o de orden.
        public string Id = NuevoId(), Padre;
        public static string NuevoId() { return "g" + Guid.NewGuid().ToString("N").Substring(0, 8); }
        // Ajustes hechos con el gumball de la vista previa (null = automático). Coordenadas de Blender (Z arriba,
        // frente = -Y, en metros) con la pieza ya centrada y apoyada en el piso, como en animador.py
        public double[] Pivote, Eje, Desplazamiento;
        public HashSet<string> Piezas = new HashSet<string>();     // elegidas con clic
        public HashSet<string> Excluidas = new HashSet<string>();  // quitadas a mano (no se incluyen solas)
    }

    class RecetaAnim
    {
        public string Pieza;
        public double Inicio = 1, Pausa = 0.33, Final = 2;
        public List<GrupoAnim> Grupos = new List<GrupoAnim>();

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static RecetaAnim Cargar(string ruta)
        {
            var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ruta, Encoding.UTF8)) as Dictionary<string, object>;
            if (d == null) return null;
            var r = new RecetaAnim { Pieza = Txt(d, "pieza", null), Inicio = Num(d, "inicio", 1), Pausa = Num(d, "pausa", 0.33), Final = Num(d, "final", 2) };
            object gs;
            if (d.TryGetValue("grupos", out gs) && gs is object[])
            {
                foreach (var o in (object[])gs)
                {
                    var g = o as Dictionary<string, object>;
                    if (g == null) continue;
                    var x = new GrupoAnim
                    {
                        Nombre = Txt(g, "nombre", "Paso"), Tipo = Txt(g, "tipo", "bisagra"), Lado = Txt(g, "lado", "izquierda"),
                        Borde = Txt(g, "borde", "abajo"), Desde = Txt(g, "desde", "arriba"), Empieza = Txt(g, "empieza", "despues"),
                        Angulo = Num(g, "angulo", 90), Distancia = Num(g, "distancia", 30), Duracion = Num(g, "duracion", 1),
                        Retraso = Num(g, "retraso", 0), Aparece = !g.ContainsKey("aparece") || Convert.ToBoolean(g["aparece"]),
                        Bloqueado = g.ContainsKey("bloqueado") && Convert.ToBoolean(g["bloqueado"]),
                        Id = Txt(g, "id", null) ?? GrupoAnim.NuevoId(), Padre = Txt(g, "padre", null),
                    };
                    // "elegidas" son las del clic; si no está (receta escrita a mano), valen "piezas"
                    foreach (var n in Lista(g, g.ContainsKey("elegidas") ? "elegidas" : "piezas")) x.Piezas.Add(n);
                    foreach (var n in Lista(g, "excluidas")) x.Excluidas.Add(n);
                    x.Pivote = Vec(g, "pivote");
                    x.Eje = Vec(g, "eje");
                    x.Desplazamiento = Vec(g, "desplazamiento");
                    r.Grupos.Add(x);
                }
            }
            return r;
        }

        // efectiva: pieza → índice de grupo (elegidas + incluidas solas), lo que usa Blender
        public void Guardar(string ruta, Dictionary<string, int> efectiva)
        {
            // Si el cambio deja menos grupos que antes, se guarda una copia de la receta anterior (por si fue sin querer)
            try
            {
                if (File.Exists(ruta))
                {
                    var anterior = Cargar(ruta);
                    if (anterior != null && anterior.Grupos.Count > Grupos.Count)
                        File.Copy(ruta, Path.Combine(Path.GetDirectoryName(ruta), "animacion_respaldo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"), true);
                }
            }
            catch { }
            var sb = new StringBuilder("{\n");
            sb.Append(" \"pieza\": ").Append(Colecciones.Json(Pieza)).Append(",\n");
            sb.Append(" \"inicio\": ").Append(N(Inicio)).Append(",\n \"pausa\": ").Append(N(Pausa)).Append(",\n \"final\": ").Append(N(Final)).Append(",\n");
            sb.Append(" \"grupos\": [");
            for (int i = 0; i < Grupos.Count; i++)
            {
                var g = Grupos[i];
                int idx = i;
                var todas = efectiva.Where(p => p.Value == idx).Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal);
                sb.Append(i == 0 ? "\n" : ",\n").Append("  {\"nombre\": ").Append(Colecciones.Json(g.Nombre))
                  .Append(", \"tipo\": ").Append(Colecciones.Json(g.Tipo))
                  .Append(", \"lado\": ").Append(Colecciones.Json(g.Lado)).Append(", \"borde\": ").Append(Colecciones.Json(g.Borde))
                  .Append(", \"angulo\": ").Append(N(g.Angulo))
                  .Append(", \"desde\": ").Append(Colecciones.Json(g.Desde)).Append(", \"distancia\": ").Append(N(g.Distancia))
                  .Append(", \"aparece\": ").Append(g.Aparece ? "true" : "false")
                  .Append(", \"duracion\": ").Append(N(g.Duracion)).Append(", \"empieza\": ").Append(Colecciones.Json(g.Empieza))
                  .Append(", \"retraso\": ").Append(N(g.Retraso))
                  .Append(g.Bloqueado ? ", \"bloqueado\": true" : "")
                  .Append(", \"id\": ").Append(Colecciones.Json(g.Id))
                  .Append(g.Padre != null && Grupos.Any(x => x.Id == g.Padre && x != g) ? ", \"padre\": " + Colecciones.Json(g.Padre) : "")
                  .Append(g.Pivote != null && g.Eje != null ? ", \"pivote\": " + VecTxt(g.Pivote) + ", \"eje\": " + VecTxt(g.Eje) : "")
                  .Append(g.Desplazamiento != null ? ", \"desplazamiento\": " + VecTxt(g.Desplazamiento) : "")
                  .Append(",\n   \"piezas\": ").Append(Arr(todas))
                  .Append(",\n   \"elegidas\": ").Append(Arr(g.Piezas.OrderBy(n => n, StringComparer.Ordinal)))
                  .Append(",\n   \"excluidas\": ").Append(Arr(g.Excluidas.OrderBy(n => n, StringComparer.Ordinal))).Append("}");
            }
            sb.Append("\n ]\n}\n");
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
        }

        static string N(double v) { return v.ToString("0.###", Inv); }
        static string VecTxt(double[] v) { return "[" + string.Join(", ", v.Select(x => x.ToString("0.#####", Inv))) + "]"; }

        public static double[] Vec(Dictionary<string, object> d, string k)
        {
            object v;
            if (!d.TryGetValue(k, out v) || !(v is object[])) return null;
            var a = (object[])v;
            if (a.Length != 3) return null;
            try { return a.Select(x => Convert.ToDouble(x, Inv)).ToArray(); } catch { return null; }
        }
        static string Arr(IEnumerable<string> l) { return "[" + string.Join(", ", l.Select(Colecciones.Json)) + "]"; }

        static string Txt(Dictionary<string, object> d, string k, string def)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, Inv) : def;
        }

        static double Num(Dictionary<string, object> d, string k, double def)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToDouble(v, Inv); } catch { return def; }
        }

        static IEnumerable<string> Lista(Dictionary<string, object> d, string k)
        {
            object v;
            if (d.TryGetValue(k, out v) && v is object[]) return ((object[])v).Select(x => Convert.ToString(x, Inv));
            return Enumerable.Empty<string>();
        }
    }

    // ── Vista de la pieza en la que se hace clic ──────────────────
    // Dibuja la imagen de la vista y pinta encima cada pieza con el color de su grupo.
    // Clic: pone o quita una pieza · arrastrar: agrega las que quedan dentro · arrastrar con clic derecho: las quita.
    class VisorPiezas : Control
    {
        public VistaAnim Vista;
        public int Ancho, Alto;
        public Func<int, Color> ColorPieza;    // índice de pieza → color de su grupo (Color.Empty: sin grupo)
        public Func<int, bool> EnGrupoActual;
        public int Resaltada = -1;
        public string Vacio = "";
        public Func<int, string> Describir;      // texto de la etiqueta junto al cursor
        public Func<int, bool> Oculta;           // piezas ocultas: oscurecidas y sin clic
        // Selección «ventana» (arrastrar hacia la derecha): piezas completas dentro del recuadro, también las de atrás.
        // Recibe el recuadro en coordenadas de la imagen; null = no hay datos (análisis viejo) y se usa lo visible
        public Func<RectangleF, HashSet<int>> PiezasDentro;
        public string Aviso;                     // texto arriba a la izquierda (p. ej. «redibujando…»)
        public event Action<int> Clic;
        public event Action<HashSet<int>, bool> Rectangulo;
        public event Action<int> Encima;
        public event Action<Point, Point> MenuPiezas;  // clic derecho sin arrastrar: (punto en la imagen, punto en el control)

        Bitmap compuesta;
        bool sucia = true;
        Point inicio = Point.Empty, actual, raton;
        bool arrastrando, paneando;
        MouseButtons boton;
        // Zoom con la rueda (hacia donde apunta el mouse) y desplazamiento con el botón central
        float zoom = 1;
        PointF centro, centroAlPanear;
        bool centroListo;

        public VisorPiezas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(28, 31, 38);
            Cursor = Cursors.Hand;
        }

        public void Refrescar() { sucia = true; Invalidate(); }

        public void Encuadrar() { zoom = 1; centroListo = false; Invalidate(); }

        float Escala()
        {
            if (Ancho == 0 || Alto == 0) return 1;
            return Math.Min((float)ClientSize.Width / Ancho, (float)ClientSize.Height / Alto) * zoom;
        }

        void AsegurarCentro()
        {
            if (!centroListo) { centro = new PointF(Ancho / 2f, Alto / 2f); centroListo = true; }
            centro = new PointF(Math.Max(0, Math.Min(Ancho, centro.X)), Math.Max(0, Math.Min(Alto, centro.Y)));
        }

        RectangleF Destino()
        {
            AsegurarCentro();
            float s = Escala();
            return new RectangleF(ClientSize.Width / 2f - centro.X * s, ClientSize.Height / 2f - centro.Y * s, Ancho * s, Alto * s);
        }

        PointF AImagenF(Point p)
        {
            AsegurarCentro();
            float s = Escala();
            return new PointF((p.X - ClientSize.Width / 2f) / s + centro.X, (p.Y - ClientSize.Height / 2f) / s + centro.Y);
        }

        Point AImagen(Point p)
        {
            var f = AImagenF(p);
            return new Point((int)Math.Floor(f.X), (int)Math.Floor(f.Y));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Vista == null) { base.OnMouseWheel(e); return; }
            var antes = AImagenF(e.Location);
            zoom = Math.Max(1f, Math.Min(12f, zoom * (e.Delta > 0 ? 1.25f : 0.8f)));
            float s = Escala();
            centro = new PointF(antes.X - (e.X - ClientSize.Width / 2f) / s, antes.Y - (e.Y - ClientSize.Height / 2f) / s);
            if (zoom == 1) centroListo = false;
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;   // que no se mueva también la ventana
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            // La rueda llega al control con el foco; no se le quita a un campo de texto que se está escribiendo
            var f = FindForm();
            if (f != null && !(f.ActiveControl is TextBoxBase) && !(f.ActiveControl is NumericUpDown)) Focus();
        }

        int PiezaEn(Point img)
        {
            if (Vista == null || img.X < 0 || img.Y < 0 || img.X >= Ancho || img.Y >= Alto) return -1;
            int id = Vista.Ids[img.Y * Ancho + img.X] - 1;
            return id >= 0 && Oculta != null && Oculta(id) ? -1 : id;
        }

        void Componer()
        {
            sucia = false;
            if (Vista == null) return;
            if (compuesta == null || compuesta.Width != Ancho || compuesta.Height != Alto)
            {
                if (compuesta != null) compuesta.Dispose();
                compuesta = new Bitmap(Ancho, Alto, PixelFormat.Format32bppArgb);
            }
            var r = new Rectangle(0, 0, Ancho, Alto);
            var px = new int[Ancho * Alto];
            var src = Vista.Imagen.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(src.Scan0, px, 0, px.Length); } finally { Vista.Imagen.UnlockBits(src); }

            // Color final por pieza: se calcula una vez por pieza, no por píxel
            var colores = new Dictionary<int, int[]>();
            var ids = Vista.Ids;
            for (int i = 0; i < px.Length; i++)
            {
                int id = ids[i];
                if (id == 0) continue;
                int[] c;
                if (!colores.TryGetValue(id, out c))
                {
                    if (Oculta != null && Oculta(id - 1))
                    {
                        // Oculta (mientras Blender redibuja sin ella): casi del color del fondo
                        colores[id] = new[] { 28, 31, 38, 880, 0 };
                        c = colores[id];
                        goto pintar;
                    }
                    Color g = ColorPieza != null ? ColorPieza(id - 1) : Color.Empty;
                    bool actualG = EnGrupoActual != null && EnGrupoActual(id - 1);
                    float a = g.IsEmpty ? 0 : actualG ? 0.62f : 0.38f;
                    float luz = id - 1 == Resaltada ? 0.35f : 0;
                    c = g.IsEmpty && luz == 0 ? null : new[] { g.IsEmpty ? 0 : g.R, g.IsEmpty ? 0 : g.G, g.IsEmpty ? 0 : g.B, (int)(a * 1000), (int)(luz * 1000) };
                    colores[id] = c;
                }
            pintar:
                if (c == null) continue;
                int p = px[i];
                int al = (p >> 24) & 255, rr = (p >> 16) & 255, gg = (p >> 8) & 255, bb = p & 255;
                float fa = c[3] / 1000f, fl = c[4] / 1000f;
                rr = (int)(rr * (1 - fa) + c[0] * fa); gg = (int)(gg * (1 - fa) + c[1] * fa); bb = (int)(bb * (1 - fa) + c[2] * fa);
                rr = (int)(rr + (255 - rr) * fl); gg = (int)(gg + (255 - gg) * fl); bb = (int)(bb + (255 - bb) * fl);
                px[i] = (Math.Max(al, 200) << 24) | (rr << 16) | (gg << 8) | bb;
            }
            // Contorno de la pieza bajo el mouse: así se ve exactamente qué se va a elegir, aunque sea pequeña
            if (Resaltada >= 0)
            {
                int h = Resaltada + 1, borde = unchecked((int)0xFF00E5C8);
                for (int i = 0; i < ids.Length; i++)
                {
                    if (ids[i] != h) continue;
                    int x = i % Ancho, y = i / Ancho;
                    if (x == 0 || y == 0 || x == Ancho - 1 || y == Alto - 1
                        || ids[i - 1] != h || ids[i + 1] != h || ids[i - Ancho] != h || ids[i + Ancho] != h)
                        px[i] = borde;
                }
            }
            var dst = compuesta.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(px, 0, dst.Scan0, px.Length); } finally { compuesta.UnlockBits(dst); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (Vista == null)
            {
                TextRenderer.DrawText(g, Vacio, Font, ClientRectangle, Color.FromArgb(170, 176, 190),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            if (sucia) Componer();
            // De cerca, píxeles nítidos: se ve exactamente dónde termina cada pieza
            g.InterpolationMode = zoom >= 2.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(compuesta, Destino());
            if (arrastrando)
            {
                // Como en Rhino: hacia la derecha = ventana (línea continua), hacia la izquierda = cruce (punteada)
                var rc = Rect(inicio, actual);
                bool ventana = actual.X >= inicio.X;
                bool quita = boton == MouseButtons.Right || (ModifierKeys & Keys.Control) != 0;
                var color = quita ? Color.OrangeRed : Color.Turquoise;
                using (var b = new SolidBrush(Color.FromArgb(40, color)))
                    g.FillRectangle(b, rc);
                using (var p = new Pen(color, 2) { DashStyle = ventana ? DashStyle.Solid : DashStyle.Dash })
                    g.DrawRectangle(p, rc);
                string modo = (quita ? "Quitar · " : "Agregar · ") + (ventana ? "piezas completas dentro (también las de atrás)" : "lo que toca el recuadro");
                var tam = TextRenderer.MeasureText(modo, Font);
                int ty = rc.Bottom + 6 + tam.Height > ClientSize.Height ? rc.Top - tam.Height - 8 : rc.Bottom + 6;
                using (var b = new SolidBrush(Color.FromArgb(225, 16, 19, 26))) g.FillRectangle(b, rc.Left, ty, tam.Width + 10, tam.Height + 4);
                TextRenderer.DrawText(g, modo, Font, new Point(rc.Left + 5, ty + 2), color);
            }
            if (!string.IsNullOrEmpty(Aviso))
            {
                var tamAviso = TextRenderer.MeasureText(Aviso, Font);
                using (var b = new SolidBrush(Color.FromArgb(225, 16, 19, 26))) g.FillRectangle(b, 0, 0, tamAviso.Width + 16, tamAviso.Height + 10);
                TextRenderer.DrawText(g, Aviso, Font, new Point(8, 5), Color.FromArgb(0, 201, 176));
            }
            var tenue = Color.FromArgb(150, 156, 170);
            string ayuda = zoom > 1.01f
                ? "Zoom ×" + zoom.ToString("0.0") + "  ·  rueda: zoom  ·  botón central: mover  ·  «Encuadrar» para volver"
                : "Rueda: zoom  ·  clic derecho: todas las piezas en ese punto";
            var tamAyuda = TextRenderer.MeasureText(ayuda, Font);
            using (var b = new SolidBrush(Color.FromArgb(200, 16, 19, 26)))
                g.FillRectangle(b, 0, ClientSize.Height - tamAyuda.Height - 8, tamAyuda.Width + 14, tamAyuda.Height + 8);
            TextRenderer.DrawText(g, ayuda, Font, new Point(7, ClientSize.Height - tamAyuda.Height - 4), tenue);
            // Etiqueta junto al cursor: qué pieza es y qué pasará con el clic
            if (Resaltada >= 0 && Describir != null && !arrastrando && !paneando)
            {
                string t = Describir(Resaltada);
                var tam = TextRenderer.MeasureText(t, Font);
                int x = Math.Min(raton.X + 16, ClientSize.Width - tam.Width - 12), y = raton.Y + 20;
                if (y + tam.Height + 8 > ClientSize.Height) y = raton.Y - tam.Height - 14;
                var caja = new Rectangle(x, y, tam.Width + 10, tam.Height + 6);
                using (var b = new SolidBrush(Color.FromArgb(235, 16, 19, 26))) g.FillRectangle(b, caja);
                using (var p = new Pen(Color.FromArgb(0, 201, 176))) g.DrawRectangle(p, caja);
                TextRenderer.DrawText(g, t, Font, new Point(x + 5, y + 3), Color.FromArgb(232, 235, 244));
            }
        }

        static Rectangle Rect(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Vista == null) return;
            Focus();
            inicio = actual = e.Location;
            boton = e.Button;
            arrastrando = false;
            paneando = e.Button == MouseButtons.Middle;
            if (paneando) { AsegurarCentro(); centroAlPanear = centro; Cursor = Cursors.SizeAll; }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Vista == null) return;
            raton = e.Location;
            if (paneando)
            {
                float s = Escala();
                centro = new PointF(centroAlPanear.X - (e.X - inicio.X) / s, centroAlPanear.Y - (e.Y - inicio.Y) / s);
                Invalidate();
                return;
            }
            if (Capture && (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right))
            {
                actual = e.Location;
                if (arrastrando) Invalidate();   // también al soltar o apretar Ctrl a mitad del arrastre
                if (!arrastrando && (Math.Abs(actual.X - inicio.X) > 5 || Math.Abs(actual.Y - inicio.Y) > 5)) arrastrando = true;
                if (arrastrando) Invalidate();
                return;
            }
            int id = PiezaEn(AImagen(e.Location));
            if (id != Resaltada)
            {
                Resaltada = id;
                Refrescar();
                if (Encima != null) Encima(id);
            }
            else Invalidate();   // la etiqueta sigue al cursor
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (Vista == null || !Capture) return;
            Capture = false;
            if (paneando) { paneando = false; Cursor = Cursors.Hand; Invalidate(); return; }
            if (!arrastrando && e.Button == MouseButtons.Right)
            {
                if (MenuPiezas != null) MenuPiezas(AImagen(e.Location), e.Location);
                return;
            }
            if (arrastrando)
            {
                arrastrando = false;
                var a = AImagen(Rect(inicio, actual).Location);
                var b = AImagen(new Point(Rect(inicio, actual).Right, Rect(inicio, actual).Bottom));
                // Hacia la derecha: ventana (piezas completas dentro, aunque estén detrás de otras)
                HashSet<int> set = e.X >= inicio.X && PiezasDentro != null ? PiezasDentro(RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y)) : null;
                if (set == null)
                {
                    // Hacia la izquierda (o sin datos): cruce, lo que se ve dentro del recuadro
                    set = new HashSet<int>();
                    for (int y = Math.Max(0, a.Y); y < Math.Min(Alto, b.Y); y++)
                        for (int x = Math.Max(0, a.X); x < Math.Min(Ancho, b.X); x++)
                        {
                            int id = Vista.Ids[y * Ancho + x];
                            if (id > 0 && (Oculta == null || !Oculta(id - 1))) set.Add(id - 1);
                        }
                }
                Invalidate();
                if (Rectangulo != null && set.Count > 0) Rectangulo(set, boton == MouseButtons.Left);
            }
            else if (e.Button == MouseButtons.Left)
            {
                int id = PiezaEn(AImagen(e.Location));
                if (id >= 0 && Clic != null) Clic(id);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (Resaltada != -1)
            {
                Resaltada = -1;
                Refrescar();
                if (Encima != null) Encima(-1);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && compuesta != null) compuesta.Dispose();
            base.Dispose(disposing);
        }
    }

    // ── El animador dentro del paso 3 ─────────────────────────────
    partial class Ventana
    {
        static readonly Color[] ColoresGrupo =
        {
            Color.FromArgb(0, 201, 176), Color.FromArgb(255, 140, 40), Color.FromArgb(120, 110, 255), Color.FromArgb(240, 70, 120),
            Color.FromArgb(60, 170, 255), Color.FromArgb(250, 210, 40), Color.FromArgb(150, 220, 70), Color.FromArgb(200, 90, 230),
        };
        static readonly string[] TiposValor = { "bisagra", "entrar", "girar" };
        static readonly string[] TiposTexto = { "Bisagra: se dobla y se cierra", "Entrar: llega desde un lado", "Girar: da vueltas sobre sí" };
        static readonly string[] LadosValor = { "izquierda", "derecha", "frente", "atras" };
        static readonly string[] LadosTexto = { "Izquierda", "Derecha", "Frente", "Atrás" };
        static readonly string[] DesdeValor = { "arriba", "abajo", "frente", "atras", "izquierda", "derecha" };
        static readonly string[] DesdeTexto = { "Arriba", "Abajo", "Frente", "Atrás", "Izquierda", "Derecha" };
        static readonly string[] VistasTexto = { "Frente ¾", "Atrás ¾", "De frente", "Desde arriba" };
        const double Tolerancia = 0.005;   // 5 mm: piezas pegadas a un grupo que se incluyen solas

        bool modoAnimado;
        string blenderExe;
        AnalisisAnim analisis;
        RecetaAnim receta;
        string proyectoAnim;        // 001_ANIMACIÓN\<Pieza>
        int vistaActual, grupoActual = -1, ultimaPiezaClic = -1;
        bool trabajandoAnim;
        string estadoAnim = "";
        Color colorEstadoAnim = Gris;
        string paginaPrueba;        // pruebas/<enlace>/ cuando ya se preparó
        Label lblEstadoAnimVisible;
        Action refrescarAnimador;

        // ── Selector del paso 3: estática u animada ──
        Control SelectorModo()
        {
            var est = new RadioButton { Text = "Pieza estática", AutoSize = true, Checked = !modoAnimado, Margin = new Padding(0, 0, S(24), 0) };
            var ani = new RadioButton { Text = "Pieza animada (armado paso a paso)", AutoSize = true, Checked = modoAnimado, Margin = Padding.Empty };
            EventHandler cambio = (s, e) =>
            {
                if (!((RadioButton)s).Checked) return;
                bool nuevo = s == ani;
                if (nuevo == modoAnimado) return;
                modoAnimado = nuevo;
                BeginInvoke((Action)(() => IrA(2)));
            };
            est.CheckedChanged += cambio;
            ani.CheckedChanged += cambio;
            var f = Fila(est, ani);
            f.Margin = new Padding(0, 0, 0, S(12));
            return f;
        }

        void AjustarTamano(bool grande)
        {
            var objetivo = grande ? new Size(S(1360), S(880)) : new Size(S(960), S(660));
            var area = Screen.FromControl(this).WorkingArea;
            int bordeW = Width - ClientSize.Width, bordeH = Height - ClientSize.Height;
            objetivo = new Size(Math.Min(objetivo.Width, area.Width - bordeW), Math.Min(objetivo.Height, area.Height - bordeH));
            if (ClientSize == objetivo) return;
            ClientSize = objetivo;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        // ── Panel del animador ──
        void PasoAnimar(FlowLayoutPanel col)
        {
            if (blenderExe == null) blenderExe = BuscarBlender();
            string nombre = nombrePieza.Trim().TrimEnd('.');
            // Al volver al paso se retoma el proyecto de la pieza (receta y vistas guardadas)
            if (analisis == null && nombre != "" && ValidarNombre(nombre, "pieza") == null) CargarProyectoAnim(nombre, false);

            int ancho = S(1056);
            var lienzo = new Panel { Width = ancho, Height = S(700), Margin = Padding.Empty };
            int xDer = S(578), anchoDer = ancho - xDer;

            // Fila 1: archivo, nombre y analizar
            var txtArchivo = new TextBox { Location = new Point(0, S(2)), Width = S(380), ReadOnly = true, Text = archivoEntrada };
            var btnExaminar = Boton("Examinar…", false);
            btnExaminar.Location = new Point(S(388), 0);
            var lblNombre = new Label { Text = "Pieza:", AutoSize = true, Location = new Point(S(492), S(5)) };
            var txtPieza = new TextBox { Location = new Point(S(540), S(2)), Width = S(200), Text = nombrePieza };
            var btnAnalizar = Boton("Analizar pieza", true);
            btnAnalizar.Location = new Point(S(752), 0);
            var lblBlender = new LinkLabel { AutoSize = true, Location = new Point(0, S(38)), UseMnemonic = false };
            var lblInfo = new Label { AutoSize = true, Location = new Point(S(300), S(38)), ForeColor = Gris, UseMnemonic = false };

            // Izquierda: vistas y visor
            var vistasFila = new FlowLayoutPanel { Location = new Point(0, S(64)), AutoSize = true, WrapContents = false };
            var botonesVista = new List<Button>();
            for (int k = 0; k < VistasTexto.Length; k++)
            {
                var b = BotonChico(VistasTexto[k], false);
                int idx = k;
                b.Click += (s, e) => { vistaActual = idx; if (refrescarAnimador != null) refrescarAnimador(); };
                botonesVista.Add(b);
                vistasFila.Controls.Add(b);
            }
            var visor = new VisorPiezas
            {
                Location = new Point(0, S(96)), Size = new Size(S(560), S(420)), Font = Font,
                Vacio = "1.  Examinar… → elige el .glb exportado de Rhino\n2.  Analizar pieza\n\nAquí aparecerá la pieza: haz clic sobre las partes que se mueven juntas.",
            };
            var btnEncuadrar = BotonChico("Encuadrar", false);
            btnEncuadrar.Click += (s, e) => visor.Encuadrar();
            vistasFila.Controls.Add(btnEncuadrar);
            var lblAyuda = new Label
            {
                AutoSize = true, MaximumSize = new Size(S(560), 0), Location = new Point(0, S(520)), ForeColor = Gris, UseMnemonic = false,
                Font = new Font(Font.FontFamily, 8.5f),
                Text = "Clic: pone/quita · Ctrl+clic: quita · Arrastrar → : piezas completas dentro (también las de atrás) · ← : lo que toca · Ctrl+arrastrar: quita · Shift: también de otros grupos.",
            };
            var lblEncima = new Label { AutoSize = true, Location = new Point(0, S(556)), UseMnemonic = false, ForeColor = Oscuro, MaximumSize = new Size(S(560), 0) };

            // Derecha: grupos
            var lblGrupos = Etiqueta("Grupos: cada uno se mueve junto, en este orden");
            lblGrupos.Location = new Point(xDer, S(62));
            var lnkMostrar = new LinkLabel { AutoSize = true, Location = new Point(ancho - S(120), S(66)), Visible = false, UseMnemonic = false };
            var lista = new ListBox
            {
                Location = new Point(xDer, S(88)), Size = new Size(anchoDer, S(140)), DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = S(26), IntegralHeight = false,
            };
            var btnNuevo = BotonChico("+ Grupo", true);
            var btnQuitar = BotonChico("Quitar", false);
            var btnSubir = BotonChico("↑", false);
            var btnBajar = BotonChico("↓", false);
            var btnIguales = BotonChico("+ Iguales", false);
            var btnBloquear = BotonChico("Bloquear", false);
            var filaGrupos = new FlowLayoutPanel { Location = new Point(xDer, S(232)), AutoSize = true, WrapContents = false };
            filaGrupos.Controls.AddRange(new Control[] { btnNuevo, btnQuitar, btnSubir, btnBajar, btnIguales, btnBloquear });

            // Derecha: propiedades del grupo elegido
            int xc = xDer + S(150), wc = anchoDer - S(150);
            Func<string, int, Label> lbl = (t, y) => new Label { Text = t, AutoSize = true, Location = new Point(xDer, y + S(3)), UseMnemonic = false };
            var txtNombre = new TextBox { Location = new Point(xc, S(270)), Width = wc };
            var cmbTipo = Combo(TiposTexto, xc, S(302), wc);
            var lblC = lbl("", S(334)); var lblD = lbl("", S(366)); var lblE = lbl("", S(398));
            var cmbLado = Combo(LadosTexto, xc, S(334), S(160));
            var cmbDesde = Combo(DesdeTexto, xc, S(334), S(160));
            var cmbBorde = Combo(new[] { "Abajo", "Arriba" }, xc, S(366), S(160));
            var numDistancia = Numero(xc, S(366), 1, 500, 0);
            var numAngulo = Numero(xc, S(334), -720, 720, 0);   // se mueve de fila según el tipo
            var chkAparece = new CheckBox { Text = "Aparece al empezar (antes no se ve)", AutoSize = true, Location = new Point(xc, S(398)) };
            var lnkAuto = new LinkLabel { AutoSize = true, Location = new Point(xc, S(337)), UseMnemonic = false, Visible = false };
            var numDuracion = Numero(xc, S(430), 0.1m, 30, 2);
            var cmbEmpieza = Combo(new[] { "Después del anterior (paso nuevo)", "Junto con el anterior" }, xc, S(462), wc - S(78));
            var numRetraso = Numero(xc + wc - S(70), S(462), 0, 30, 2);
            numRetraso.Width = S(70);
            var cmbPadre = Combo(new string[0], xc, S(494), wc);
            var idsPadre = new List<string>();   // id del grupo de cada opción de cmbPadre (null = independiente)
            var lblTiempos = Etiqueta("Tiempos generales (segundos)");
            lblTiempos.Location = new Point(xDer, S(528));
            var numInicio = Numero(xDer + S(78), S(556), 0, 30, 2);
            var numPausa = Numero(xDer + S(220), S(556), 0, 30, 2);
            var numFinal = Numero(xDer + S(360), S(556), 0, 30, 2);
            numInicio.Width = numPausa.Width = numFinal.Width = S(62);
            var props = new Control[]
            {
                lbl("Nombre del paso", S(270)), txtNombre, lbl("Movimiento", S(302)), cmbTipo, lblC, lblD, lblE,
                cmbLado, cmbDesde, cmbBorde, numDistancia, numAngulo, chkAparece, lnkAuto,
                lbl("Duración (s)", S(430)), numDuracion, lbl("Empieza · retraso (s)", S(462)), cmbEmpieza, numRetraso, lbl("Se mueve con", S(494)), cmbPadre,
            };
            var globales = new Control[]
            {
                lblTiempos, new Label { Text = "Al inicio", AutoSize = true, Location = new Point(xDer, S(559)) }, numInicio,
                new Label { Text = "Pausa", AutoSize = true, Location = new Point(xDer + S(166), S(559)) }, numPausa,
                new Label { Text = "Al final", AutoSize = true, Location = new Point(xDer + S(300), S(559)) }, numFinal,
            };

            // Abajo: generar y probar
            var btnVivo = Boton("▶  Vista previa en vivo", true);
            var btnGenerar = Boton("Generar animación", false);
            var btnVer = BotonChico("Preparar prueba en celulares", false);
            var btnBlender = BotonChico("Abrir en Blender", false);
            var btnCarpeta = BotonChico("Abrir carpeta", false);
            var filaAcciones = new FlowLayoutPanel { Location = new Point(0, S(582)), AutoSize = true, WrapContents = false };
            filaAcciones.Controls.AddRange(new Control[] { btnVivo, btnGenerar });
            var filaAcciones2 = new FlowLayoutPanel { Location = new Point(0, S(620)), AutoSize = true, WrapContents = false };
            filaAcciones2.Controls.AddRange(new Control[] { btnVer, btnBlender, btnCarpeta });
            var lblEstado = new Label { Location = new Point(0, S(652)), AutoSize = true, MaximumSize = new Size(S(470), 0), UseMnemonic = false };
            var lblPasos = Etiqueta("Imágenes de cada paso (clic para ampliar)");
            lblPasos.Location = new Point(xDer, S(596));
            var tira = new FlowLayoutPanel { Location = new Point(xDer, S(622)), Size = new Size(anchoDer, S(60)), WrapContents = false, AutoScroll = false };
            var qr = new PictureBox { Location = new Point(S(484), S(582)), Size = new Size(S(84), S(84)), SizeMode = PictureBoxSizeMode.Zoom, Visible = false, BackColor = Color.White };
            var lblQr = new LinkLabel { AutoSize = true, Location = new Point(S(478), S(668)), Visible = false, Text = "Enlace de prueba", Font = new Font(Font.FontFamily, 8.5f) };
            var tip = new ToolTip();

            lienzo.Controls.AddRange(new Control[] { txtArchivo, btnExaminar, lblNombre, txtPieza, btnAnalizar, lblBlender, lblInfo, vistasFila, visor, lblAyuda, lblEncima, lblGrupos, lnkMostrar, lista, filaGrupos });
            lienzo.Controls.AddRange(props);
            lienzo.Controls.AddRange(globales);
            lienzo.Controls.AddRange(new Control[] { filaAcciones, filaAcciones2, lblEstado, lblPasos, tira, qr, lblQr });
            col.Controls.Add(lienzo);
            // La fila de vistas es más alta con la escala de Windows: la vista empieza justo debajo, sin taparse
            vistasFila.AutoSize = false;
            vistasFila.Height = vistasFila.Controls.Cast<Control>().Max(c => c.Bottom + c.Margin.Bottom);
            vistasFila.Width = vistasFila.Controls.Cast<Control>().Max(c => c.Right + c.Margin.Right);
            int arribaVisor = vistasFila.Bottom + S(4);
            visor.SetBounds(0, arribaVisor, visor.Width, Math.Max(S(300), lblAyuda.Top - S(4) - arribaVisor));
            lblEstadoAnimVisible = lblEstado;

            // ── Comportamiento ──
            bool cargando = false;
            Func<GrupoAnim> grupo = () => receta != null && grupoActual >= 0 && grupoActual < receta.Grupos.Count ? receta.Grupos[grupoActual] : null;
            Dictionary<string, int> asignacion = new Dictionary<string, int>();

            Action guardar = () =>
            {
                if (receta == null || proyectoAnim == null) return;
                try { receta.Guardar(Path.Combine(proyectoAnim, "animacion.json"), asignacion); }
                catch (Exception ex) { MostrarEstadoAnim("No se pudo guardar la receta: " + ex.Message, Rojo); }
            };

            Action mostrarProps = () =>
            {
                var g = grupo();
                cargando = true;
                foreach (var c in props) c.Enabled = g != null && !trabajandoAnim;
                if (g != null)
                {
                    txtNombre.Text = g.Nombre;
                    cmbTipo.SelectedIndex = Math.Max(0, Array.IndexOf(TiposValor, g.Tipo));
                    cmbLado.SelectedIndex = Math.Max(0, Array.IndexOf(LadosValor, g.Lado));
                    cmbDesde.SelectedIndex = Math.Max(0, Array.IndexOf(DesdeValor, g.Desde));
                    cmbBorde.SelectedIndex = g.Borde == "arriba" ? 1 : 0;
                    numDistancia.Value = Lim(numDistancia, g.Distancia);
                    numAngulo.Value = Lim(numAngulo, g.Angulo);
                    chkAparece.Checked = g.Aparece;
                    numDuracion.Value = Lim(numDuracion, g.Duracion);
                    cmbEmpieza.SelectedIndex = g.Empieza == "con" ? 1 : 0;
                    // Opciones de «Se mueve con»: cualquier otro grupo que no viaje ya con este (evita círculos)
                    cmbPadre.Items.Clear();
                    idsPadre.Clear();
                    cmbPadre.Items.Add("Nada: se mueve solo");
                    idsPadre.Add(null);
                    for (int k = 0; k < receta.Grupos.Count; k++)
                    {
                        var o = receta.Grupos[k];
                        if (o == g || DesciendeDe(o, g)) continue;
                        cmbPadre.Items.Add((k + 1) + ". " + o.Nombre + "  (viaja pegado a este grupo)");
                        idsPadre.Add(o.Id);
                    }
                    cmbPadre.SelectedIndex = Math.Max(0, idsPadre.IndexOf(g.Padre));
                    numRetraso.Value = Lim(numRetraso, g.Retraso);
                }
                string tipo = g != null ? g.Tipo : "bisagra";
                bool bis = tipo == "bisagra", ent = tipo == "entrar", gir = tipo == "girar";
                lblC.Text = bis ? "Se abre hacia" : ent ? "Llega desde" : "Ángulo (°)";
                lblD.Text = bis ? "Borde del doblez" : ent ? "Distancia (cm)" : "";
                lblE.Text = bis ? "Ángulo abierto (°)" : "";
                // Con bisagra o inicio ajustados con el gumball de la vista previa, los ajustes automáticos no aplican
                bool giroPropio = g != null && (bis || gir) && g.Pivote != null;
                bool inicioPropio = g != null && ent && g.Desplazamiento != null;
                if (giroPropio && bis) { lblC.Text = "Bisagra"; lblD.Text = ""; }
                if (giroPropio && gir) lblD.Text = "Eje de giro";
                if (inicioPropio) { lblC.Text = "Punto de partida"; lblD.Text = ""; }
                cmbLado.Visible = cmbBorde.Visible = bis && !giroPropio;
                cmbDesde.Visible = numDistancia.Visible = ent && !inicioPropio;
                chkAparece.Visible = ent;
                numAngulo.Visible = bis || gir;
                numAngulo.Top = bis ? S(398) : S(334);
                lnkAuto.Visible = giroPropio || inicioPropio;
                lnkAuto.Top = giroPropio && gir ? S(369) : S(337);
                lnkAuto.Text = giroPropio ? "ajustada con el gumball · volver a automática" : "ajustado con el gumball · volver a automático";
                if (receta != null)
                {
                    numInicio.Value = Lim(numInicio, receta.Inicio);
                    numPausa.Value = Lim(numPausa, receta.Pausa);
                    numFinal.Value = Lim(numFinal, receta.Final);
                }
                foreach (var c in globales) c.Enabled = receta != null && !trabajandoAnim;
                cargando = false;
            };

            Action refrescarLista = () =>
            {
                lista.BeginUpdate();
                lista.Items.Clear();
                if (receta != null) foreach (var g in receta.Grupos) lista.Items.Add(g);
                if (grupoActual >= lista.Items.Count) grupoActual = lista.Items.Count - 1;
                if (grupoActual >= 0) lista.SelectedIndex = grupoActual;
                lista.EndUpdate();
            };

            Action refrescar = () =>
            {
                asignacion = Asignacion();
                bool listo = analisis != null && receta != null;
                txtArchivo.Text = archivoEntrada;
                for (int k = 0; k < botonesVista.Count; k++)
                {
                    botonesVista[k].Enabled = listo;
                    botonesVista[k].BackColor = k == vistaActual && listo ? Acento : Color.White;
                }
                var vistas = listo ? VistasActuales() : null;
                visor.Vista = listo && vistaActual < vistas.Count ? vistas[vistaActual] : null;
                if (listo) { visor.Ancho = analisis.Ancho; visor.Alto = analisis.Alto; }
                visor.Aviso = ocultas.Count == 0 ? null
                    : redibujando || vistas != vistasSin ? "Redibujando sin las piezas ocultas…"
                    : ocultas.Count + (ocultas.Count == 1 ? " pieza oculta" : " piezas ocultas") + "  ·  Shift+H: mostrar todo";
                lnkMostrar.Visible = ocultas.Count > 0;
                lnkMostrar.Text = "Mostrar todo (" + ocultas.Count + ")";
                lnkMostrar.Left = lista.Right - lnkMostrar.PreferredWidth;
                visor.Refrescar();
                lista.Invalidate();
                btnAnalizar.Enabled = !trabajandoAnim;
                btnExaminar.Enabled = !trabajandoAnim;
                btnNuevo.Enabled = listo && !trabajandoAnim;
                btnQuitar.Enabled = btnSubir.Enabled = btnBajar.Enabled = btnBloquear.Enabled = grupo() != null && !trabajandoAnim;
                btnBloquear.Text = grupo() != null && grupo().Bloqueado ? "Desbloquear" : "Bloquear";
                btnIguales.Enabled = grupo() != null && ultimaPiezaClic >= 0 && !trabajandoAnim;
                btnGenerar.Enabled = listo && !trabajandoAnim && receta.Grupos.Any(g => asignacion.ContainsValue(receta.Grupos.IndexOf(g)));
                btnVivo.Enabled = listo && !trabajandoAnim;
                GuardarSeleccion();
                string blend = proyectoAnim != null && receta != null ? Path.Combine(proyectoAnim, "04_BLENDER", receta.Pieza + "_ANIM.blend") : null;
                btnBlender.Enabled = blend != null && File.Exists(blend) && blenderExe != null;
                btnVer.Enabled = btnBlender.Enabled && !trabajandoAnim && File.Exists(Path.Combine(proyectoAnim, "05_EXPORTAR", "resumen.json"));
                btnCarpeta.Enabled = proyectoAnim != null && Directory.Exists(proyectoAnim);
                lblBlender.Text = blenderExe != null ? "Blender: " + Path.GetFileName(Path.GetDirectoryName(blenderExe)) + "  (cambiar)" : "Falta Blender: elige blender.exe";
                lblBlender.LinkColor = blenderExe != null ? Gris : Rojo;
                if (listo)
                {
                    int animadas = asignacion.Count, solas = asignacion.Count(p => !receta.Grupos[p.Value].Piezas.Contains(p.Key));
                    lblInfo.Text = analisis.Piezas.Count + " piezas · " + animadas + " en grupos (" + solas + " incluidas solas) · " + (analisis.Piezas.Count - animadas) + " quedan fijas";
                }
                else lblInfo.Text = "";
                lblEstado.Text = estadoAnim;
                lblEstado.ForeColor = colorEstadoAnim;
                mostrarProps();
                if (paginaPrueba != null)
                {
                    string url = UrlBase + paginaPrueba;
                    if (qr.Tag as string != url)
                    {
                        var m = Qr.Codificar(url);
                        qr.Image = Qr.ABitmap(m, Math.Max(1, S(84) / (m.GetLength(0) + 8)));
                        qr.Tag = url;
                    }
                    qr.Visible = lblQr.Visible = true;
                    tip.SetToolTip(qr, url);
                    tip.SetToolTip(lblQr, url);
                }
            };
            refrescarAnimador = refrescar;

            // Visor: color de cada pieza según su grupo
            visor.ColorPieza = i =>
            {
                int g;
                if (analisis == null || !asignacion.TryGetValue(analisis.Piezas[i].Nombre, out g)) return Color.Empty;
                return ColoresGrupo[g % ColoresGrupo.Length];
            };
            visor.EnGrupoActual = i =>
            {
                int g;
                return analisis != null && asignacion.TryGetValue(analisis.Piezas[i].Nombre, out g) && g == grupoActual;
            };
            visor.Encima += i =>
            {
                if (analisis == null || i < 0) { lblEncima.Text = ""; return; }
                var p = analisis.Piezas[i];
                int g;
                string donde = asignacion.TryGetValue(p.Nombre, out g)
                    ? "grupo «" + receta.Grupos[g].Nombre + "»" + (receta.Grupos[g].Piezas.Contains(p.Nombre) ? "" : " (incluida sola)") + (receta.Grupos[g].Bloqueado ? " · bloqueado" : "")
                    : "fija";
                lblEncima.Text = p.Nombre + " · " + (p.Material == "" ? "sin material" : p.Material) + " · " + p.Triangulos.ToString("N0") + " triángulos · " + donde;
            };
            // soloLibres: no toma piezas que ya están en otro grupo (arrastrar sin Shift, + Iguales)
            Action<IEnumerable<int>, bool, bool> cambiarPiezas = (ids, agregar, soloLibres) =>
            {
                if (grupo() == null)
                {
                    MostrarEstadoAnim("Primero crea o elige un grupo (+ Grupo).", Naranja);
                    refrescar();
                    return;
                }
                bool importante;
                string aviso = CambiarPiezas(grupoActual, ids.Select(i => analisis.Piezas[i].Nombre).ToList(), agregar, soloLibres, out importante);
                if (aviso != null) MostrarEstadoAnim(aviso, importante ? Naranja : Gris);
                asignacion = Asignacion();
                guardar();
                refrescar();
            };
            visor.Clic += i =>
            {
                ultimaPiezaClic = i;
                int g;
                bool enActual = asignacion.TryGetValue(analisis.Piezas[i].Nombre, out g) && g == grupoActual;
                // Como en Rhino: Ctrl+clic solo quita, Shift+clic solo agrega, clic solo alterna
                var mod = Control.ModifierKeys;
                if ((mod & Keys.Control) != 0)
                {
                    if (enActual) cambiarPiezas(new[] { i }, false, false);
                    else { MostrarEstadoAnim(analisis.Piezas[i].Nombre + " no está en este grupo: no hay nada que quitar.", Gris); refrescar(); }
                }
                else if ((mod & Keys.Shift) != 0) { if (!enActual) cambiarPiezas(new[] { i }, true, false); }
                else cambiarPiezas(new[] { i }, !enActual, false);
            };
            // Arrastrar: solo piezas libres; con Shift también las de otros grupos (nunca las bloqueadas).
            // Ctrl + arrastrar (o arrastrar con clic derecho): quita las del recuadro
            visor.Rectangulo += (ids, agregar) =>
            {
                var mod = Control.ModifierKeys;
                if ((mod & Keys.Control) != 0) agregar = false;
                cambiarPiezas(ids, agregar, (mod & Keys.Shift) == 0);
            };

            // Selección ventana: piezas cuyo contorno en esta vista cabe completo en el recuadro, a cualquier profundidad
            // (así se toman las filas de producto de atrás desde la vista de frente, sin tocar piso ni espaldar)
            visor.PiezasDentro = r =>
            {
                if (analisis == null || visor.Vista == null || !analisis.Piezas.Any(p => p.Rects.Count > 0)) return null;
                string vista = visor.Vista.Nombre;
                var set = new HashSet<int>();
                for (int i = 0; i < analisis.Piezas.Count; i++)
                {
                    var p = analisis.Piezas[i];
                    double[] q;
                    if (ocultas.Contains(p.Nombre) || !p.Rects.TryGetValue(vista, out q)) continue;
                    if (q[0] >= r.Left - 1 && q[2] <= r.Right + 1 && q[1] >= r.Top - 1 && q[3] <= r.Bottom + 1) set.Add(i);
                }
                return set;
            };

            // Etiqueta junto al cursor: qué pieza es y qué hará el clic
            visor.Describir = i =>
            {
                var p = analisis.Piezas[i];
                int g;
                bool tiene = asignacion.TryGetValue(p.Nombre, out g);
                string donde = tiene ? "«" + receta.Grupos[g].Nombre + "»" + (receta.Grupos[g].Piezas.Contains(p.Nombre) ? "" : " (incluida sola)") : "libre";
                var ga = grupo();
                string accion = ga == null ? "Crea o elige un grupo para empezar"
                    : ga.Bloqueado ? "«" + ga.Nombre + "» está bloqueado"
                    : tiene && g == grupoActual ? "Clic o Ctrl+clic: quitar de «" + ga.Nombre + "»"
                    : tiene && receta.Grupos[g].Bloqueado ? "Es de un grupo bloqueado"
                    : "Clic: agregar a «" + ga.Nombre + "»";
                return p.Nombre + "  ·  " + donde + "\n" + accion + "   ·   clic derecho: piezas en este punto";
            };

            // Clic derecho: todas las piezas bajo ese punto, de adelante hacia atrás (como el menú de selección de Rhino)
            visor.MenuPiezas += (img, cli) =>
            {
                if (analisis == null || visor.Vista == null) return;
                string vista = visor.Vista.Nombre;
                bool dentro = img.X >= 0 && img.Y >= 0 && img.X < analisis.Ancho && img.Y < analisis.Alto;
                int frente = dentro ? visor.Vista.Ids[img.Y * analisis.Ancho + img.X] - 1 : -1;
                var candidatas = new List<int>();
                if (frente >= 0) candidatas.Add(frente);
                bool hayRects = analisis.Piezas.Any(p => p.Rects.Count > 0);
                candidatas.AddRange(Enumerable.Range(0, analisis.Piezas.Count)
                    .Where(i => i != frente && analisis.Piezas[i].Rects.ContainsKey(vista) && !ocultas.Contains(analisis.Piezas[i].Nombre))
                    .Select(i => new { i, r = analisis.Piezas[i].Rects[vista] })
                    .Where(x => img.X >= x.r[0] - 2 && img.X <= x.r[2] + 2 && img.Y >= x.r[1] - 2 && img.Y <= x.r[3] + 2)
                    .OrderBy(x => x.r[4]).Select(x => x.i).Take(24));

                var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Font = Font };
                if (candidatas.Count == 0) menu.Items.Add(new ToolStripMenuItem("No hay piezas en este punto") { Enabled = false });
                foreach (int i in candidatas)
                {
                    var p = analisis.Piezas[i];
                    int g;
                    bool tiene = asignacion.TryGetValue(p.Nombre, out g);
                    string texto = p.Nombre + (i == frente ? "  (la de adelante)" : "") + "   ·   "
                        + (tiene ? "«" + receta.Grupos[g].Nombre + "»" + (receta.Grupos[g].Bloqueado ? " bloqueado" : "") : "libre")
                        + "   ·   " + (p.Material == "" ? "sin material" : p.Material);
                    var item = new ToolStripMenuItem(texto) { Checked = tiene && g == grupoActual };
                    int idx = i;
                    item.MouseEnter += (s, e2) => { visor.Resaltada = idx; visor.Refrescar(); };
                    item.Click += (s, e2) =>
                    {
                        ultimaPiezaClic = idx;
                        int gg;
                        bool en = asignacion.TryGetValue(analisis.Piezas[idx].Nombre, out gg) && gg == grupoActual;
                        cambiarPiezas(new[] { idx }, !en, false);
                    };
                    menu.Items.Add(item);
                }
                if (!hayRects)
                    menu.Items.Add(new ToolStripMenuItem("Para ver también las piezas de atrás, pulsa «Analizar pieza» otra vez") { Enabled = false });
                if (frente >= 0)
                {
                    menu.Items.Add(new ToolStripSeparator());
                    var iguales = new ToolStripMenuItem("Agregar todas las iguales a " + analisis.Piezas[frente].Nombre);
                    iguales.Click += (s, e2) => { ultimaPiezaClic = frente; btnIguales.PerformClick(); };
                    menu.Items.Add(iguales);
                    // Quitar de una vez todas las iguales (p. ej. todo el producto) del grupo elegido
                    var ref0 = analisis.Piezas[frente];
                    var igualesEnGrupo = Enumerable.Range(0, analisis.Piezas.Count).Where(i =>
                    {
                        var q = analisis.Piezas[i];
                        int gg;
                        return q.Material == ref0.Material && q.Triangulos == ref0.Triangulos
                            && asignacion.TryGetValue(q.Nombre, out gg) && gg == grupoActual;
                    }).ToList();
                    var quitarIguales = new ToolStripMenuItem("Quitar del grupo todas las iguales a " + ref0.Nombre + " (" + igualesEnGrupo.Count + ")")
                    {
                        Enabled = igualesEnGrupo.Count > 0,
                    };
                    quitarIguales.Click += (s, e2) => cambiarPiezas(igualesEnGrupo, false, false);
                    menu.Items.Add(quitarIguales);
                }
                menu.Closed += (s, e2) => { visor.Resaltada = -1; visor.Refrescar(); };
                menu.Show(visor, cli);
            };
            btnBloquear.Click += (s, e) =>
            {
                var g = grupo();
                if (g == null) return;
                g.Bloqueado = !g.Bloqueado;
                MostrarEstadoAnim(g.Bloqueado ? "«" + g.Nombre + "» bloqueado: sus piezas quedan protegidas." : "«" + g.Nombre + "» desbloqueado.", Gris);
                guardar();
                refrescar();
            };

            lista.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || receta == null || e.Index >= receta.Grupos.Count) return;
                e.DrawBackground();
                var g = receta.Grupos[e.Index];
                int n = asignacion.Count(p => p.Value == e.Index);
                using (var b = new SolidBrush(ColoresGrupo[e.Index % ColoresGrupo.Length]))
                    e.Graphics.FillRectangle(b, e.Bounds.X + S(6), e.Bounds.Y + S(7), S(12), S(12));
                string tipo = g.Tipo == "bisagra" ? "bisagra" : g.Tipo == "girar" ? "gira" : "entra";
                var padre = g.Padre == null ? null : receta.Grupos.FirstOrDefault(x => x.Id == g.Padre);
                string texto = (e.Index + 1) + ".  " + (g.Bloqueado ? "🔒 " : "") + (padre != null ? "↳ " : "") + g.Nombre + "   ·   " + tipo
                    + (padre != null ? " · con «" + padre.Nombre + "»" : "") + " · " + n + (n == 1 ? " pieza" : " piezas")
                    + (e.Index > 0 && g.Empieza == "con" ? " · junto al anterior" : "");
                bool sel = (e.State & DrawItemState.Selected) != 0;
                var anchoTexto = new Rectangle(e.Bounds.X + S(24), e.Bounds.Y + S(4), e.Bounds.Width - S(24) - AnchoOjo, e.Bounds.Height - S(4));
                TextRenderer.DrawText(e.Graphics, texto, lista.Font, anchoTexto,
                    sel ? SystemColors.HighlightText : (n == 0 ? Rojo : Oscuro), TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                // Ojo: ver u ocultar las piezas del grupo
                var piezasG = asignacion.Where(p => p.Value == e.Index).Select(p => p.Key).ToList();
                bool visible = piezasG.Count == 0 || piezasG.Any(p => !ocultas.Contains(p));
                var ojo = new Rectangle(e.Bounds.Right - AnchoOjo, e.Bounds.Y, AnchoOjo, e.Bounds.Height);
                var colorOjo = sel ? SystemColors.HighlightText : visible ? Oscuro : Color.FromArgb(175, 180, 190);
                TextRenderer.DrawText(e.Graphics, "👁", lista.Font, ojo, colorOjo, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (!visible)
                    using (var p = new Pen(colorOjo, Math.Max(1, S(2))))
                        e.Graphics.DrawLine(p, ojo.Left + S(9), ojo.Bottom - S(6), ojo.Right - S(9), ojo.Top + S(6));
            };
            // Clic en el ojo: oculta o muestra el grupo sin cambiar el grupo que estás editando
            Func<Point, int> filaDelOjo = pt => pt.X >= lista.ClientSize.Width - AnchoOjo ? lista.IndexFromPoint(pt) : -1;
            Action<int> alternarOjo = i =>
            {
                if (i < 0 || receta == null || i >= receta.Grupos.Count) return;
                var piezasG = asignacion.Where(p => p.Value == i).Select(p => p.Key).ToList();
                if (piezasG.Count == 0) return;
                CambiarOcultas(piezasG, piezasG.Any(p => !ocultas.Contains(p)));
            };
            bool ojoHecho = false;
            lista.SelectedIndexChanged += (s, e) =>
            {
                if (lista.SelectedIndex == grupoActual) return;
                if (Control.MouseButtons == MouseButtons.Left)
                {
                    int fila = filaDelOjo(lista.PointToClient(Cursor.Position));
                    if (fila >= 0)
                    {
                        ojoHecho = true;
                        lista.SelectedIndex = grupoActual;   // vuelve al grupo que estabas editando
                        alternarOjo(fila);
                        return;
                    }
                }
                grupoActual = lista.SelectedIndex;
                refrescar();
            };
            lista.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (ojoHecho) { ojoHecho = false; return; }
                int fila = filaDelOjo(e.Location);
                if (fila >= 0) alternarOjo(fila);   // ojo del grupo que ya estaba elegido
            };
            lnkMostrar.LinkClicked += (s, e) => CambiarOcultas(ocultas.ToList(), false);

            // Tecla H sobre una pieza: la oculta · Shift+H: muestra todo (como en Rhino y en la vista previa)
            visor.Oculta = i => analisis != null && ocultas.Contains(analisis.Piezas[i].Nombre);
            visor.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.H || analisis == null) return;
                if (e.Shift) CambiarOcultas(ocultas.ToList(), false);
                else if (visor.Resaltada >= 0) CambiarOcultas(new[] { analisis.Piezas[visor.Resaltada].Nombre }, true);
                e.Handled = true;
            };
            tip.SetToolTip(lista, "Clic en 👁 (a la derecha de cada grupo): ocultar o mostrar sus piezas para llegar a lo que está detrás.");

            btnNuevo.Click += (s, e) =>
            {
                var g = new GrupoAnim { Nombre = "Paso " + (receta.Grupos.Count + 1) };
                if (receta.Grupos.Count > 0)
                {
                    // Copia el movimiento del grupo elegido: así varias filas o paredes se arman rápido
                    var b = grupo() ?? receta.Grupos.Last();
                    g.Tipo = b.Tipo; g.Lado = b.Lado; g.Borde = b.Borde; g.Desde = b.Desde; g.Angulo = b.Angulo;
                    g.Distancia = b.Distancia; g.Aparece = b.Aparece; g.Duracion = b.Duracion;
                }
                receta.Grupos.Add(g);
                grupoActual = receta.Grupos.Count - 1;
                guardar();
                refrescarLista();
                refrescar();
                MostrarEstadoAnim("Grupo «" + g.Nombre + "» creado: haz clic en sus piezas.", Gris);
                lblEstado.Text = estadoAnim;
                txtNombre.Focus();
                txtNombre.SelectAll();
            };
            btnQuitar.Click += (s, e) =>
            {
                if (grupo() == null) return;
                receta.Grupos.RemoveAt(grupoActual);
                grupoActual = Math.Min(grupoActual, receta.Grupos.Count - 1);
                asignacion = Asignacion();
                guardar();
                refrescarLista();
                refrescar();
            };
            Action<int> mover = d =>
            {
                int j = grupoActual + d;
                if (grupo() == null || j < 0 || j >= receta.Grupos.Count) return;
                var t = receta.Grupos[grupoActual];
                receta.Grupos[grupoActual] = receta.Grupos[j];
                receta.Grupos[j] = t;
                grupoActual = j;
                asignacion = Asignacion();
                guardar();
                refrescarLista();
                refrescar();
            };
            btnSubir.Click += (s, e) => mover(-1);
            btnBajar.Click += (s, e) => mover(+1);
            btnIguales.Click += (s, e) =>
            {
                if (ultimaPiezaClic < 0 || grupo() == null) return;
                var p = analisis.Piezas[ultimaPiezaClic];
                var iguales = Enumerable.Range(0, analisis.Piezas.Count).Where(i =>
                {
                    var q = analisis.Piezas[i];
                    return q.Material == p.Material && q.Triangulos == p.Triangulos;
                }).ToList();
                cambiarPiezas(iguales, true, true);
            };

            // Propiedades: cada cambio se guarda al momento
            Action<Action<GrupoAnim>> editar = accion =>
            {
                var g = grupo();
                if (cargando || g == null) return;
                accion(g);
                guardar();
                lista.Invalidate();
            };
            txtNombre.TextChanged += (s, e) => editar(g => g.Nombre = txtNombre.Text.Trim() == "" ? "Paso" : txtNombre.Text.Trim());
            cmbTipo.SelectedIndexChanged += (s, e) =>
            {
                editar(g =>
                {
                    string nuevo = TiposValor[cmbTipo.SelectedIndex];
                    if (g.Tipo == nuevo) return;
                    g.Tipo = nuevo;
                    if (nuevo == "girar" && Math.Abs(g.Angulo) == 90) g.Angulo = 360;
                    if (nuevo == "bisagra" && Math.Abs(g.Angulo) == 360) g.Angulo = 90;
                });
                if (!cargando) mostrarProps();
            };
            cmbLado.SelectedIndexChanged += (s, e) => editar(g => g.Lado = LadosValor[cmbLado.SelectedIndex]);
            cmbDesde.SelectedIndexChanged += (s, e) => editar(g => g.Desde = DesdeValor[cmbDesde.SelectedIndex]);
            cmbBorde.SelectedIndexChanged += (s, e) => editar(g => g.Borde = cmbBorde.SelectedIndex == 1 ? "arriba" : "abajo");
            numDistancia.ValueChanged += (s, e) => editar(g => g.Distancia = (double)numDistancia.Value);
            numAngulo.ValueChanged += (s, e) => editar(g => g.Angulo = (double)numAngulo.Value);
            chkAparece.CheckedChanged += (s, e) => editar(g => g.Aparece = chkAparece.Checked);
            numDuracion.ValueChanged += (s, e) => editar(g => g.Duracion = (double)numDuracion.Value);
            cmbEmpieza.SelectedIndexChanged += (s, e) => editar(g => g.Empieza = cmbEmpieza.SelectedIndex == 1 ? "con" : "despues");
            numRetraso.ValueChanged += (s, e) => editar(g => g.Retraso = (double)numRetraso.Value);
            cmbPadre.SelectedIndexChanged += (s, e) => editar(g =>
            {
                if (cmbPadre.SelectedIndex >= 0 && cmbPadre.SelectedIndex < idsPadre.Count) g.Padre = idsPadre[cmbPadre.SelectedIndex];
            });
            tip.SetToolTip(cmbPadre, "Movimiento encadenado: este grupo viaja pegado al que elijas (se levanta, gira o entra con él) y su propio movimiento se suma encima. Ej.: laterales que se mueven con el espaldar y después se abren.");
            tip.SetToolTip(numRetraso, "Retraso (s): espera antes de empezar. Con «Junto con el anterior» sirve para escalonar filas.");
            EventHandler globalCambio = (s, e) =>
            {
                if (cargando || receta == null) return;
                receta.Inicio = (double)numInicio.Value;
                receta.Pausa = (double)numPausa.Value;
                receta.Final = (double)numFinal.Value;
                guardar();
            };
            numInicio.ValueChanged += globalCambio;
            numPausa.ValueChanged += globalCambio;
            numFinal.ValueChanged += globalCambio;
            tip.SetToolTip(cmbLado, "La bisagra se pone sola en el borde del grupo que da hacia ese lado. Al empezar, el grupo está acostado hacia ese lado y se levanta.");
            tip.SetToolTip(cmbBorde, "Abajo: paredes que se levantan desde el piso. Arriba: tapas que cuelgan del borde de arriba.");
            tip.SetToolTip(numAngulo, "Bisagra: qué tan abierto empieza (90 = acostado). Girar: cuántos grados da (360 = una vuelta).");
            tip.SetToolTip(cmbEmpieza, "Después del anterior: es un paso nuevo. Junto con el anterior: se mueve a la vez (por ejemplo, las dos paredes).");
            tip.SetToolTip(numRetraso, "Espera antes de empezar. Con «Junto con el anterior» sirve para escalonar filas.");
            tip.SetToolTip(btnIguales, "Agrega al grupo todas las piezas iguales a la última que tocaste (mismo material y malla), por ejemplo todas las botellas.");

            // Archivo y análisis
            btnExaminar.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Filter = "Modelo glTF binario (*.glb)|*.glb", Title = "Elige el .glb exportado desde Rhino" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    archivoEntrada = dlg.FileName;
                    txtPieza.Text = Path.GetFileNameWithoutExtension(archivoEntrada);
                    nombrePieza = txtPieza.Text;
                    analisis = null; receta = null; proyectoAnim = null; paginaPrueba = null; grupoActual = -1;
                    CargarProyectoAnim(nombrePieza.Trim(), false);
                    VolverAlAnimador();
                }
            };
            txtPieza.Leave += (s, e) =>
            {
                if (txtPieza.Text.Trim() == nombrePieza.Trim()) return;
                nombrePieza = txtPieza.Text.Trim();
                analisis = null; receta = null; proyectoAnim = null; paginaPrueba = null; grupoActual = -1;
                CargarProyectoAnim(nombrePieza, false);
                VolverAlAnimador();
            };
            btnAnalizar.Click += (s, e) =>
            {
                nombrePieza = txtPieza.Text.Trim().TrimEnd('.');
                AnalizarPieza();
            };
            lblBlender.LinkClicked += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Filter = "Blender (blender.exe)|blender.exe", Title = "Elige blender.exe" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    blenderExe = dlg.FileName;
                    try { File.WriteAllText(Path.Combine(CarpetaAnimacion(), "_blender.txt"), blenderExe, new UTF8Encoding(false)); } catch { }
                    refrescar();
                }
            };

            lnkAuto.LinkClicked += (s, e) =>
            {
                var g = grupo();
                if (g == null) return;
                if (g.Tipo == "entrar") g.Desplazamiento = null;
                else { g.Pivote = null; g.Eje = null; }
                guardar();
                mostrarProps();
            };
            tip.SetToolTip(lblC, "Para poner la bisagra donde quieras (por ejemplo, vertical en el borde del espaldar): ▶ Vista previa en vivo → Editar");
            btnGenerar.Click += (s, e) => GenerarAnimacion();
            btnVivo.Click += (s, e) => { guardar(); AbrirVistaEnVivo(); };
            tip.SetToolTip(btnVivo, "Abre la pieza en tu navegador y reproduce el movimiento mientras lo configuras: cada cambio aquí se ve allá al instante.");
            tip.SetToolTip(btnGenerar, "Crea la animación final en Blender: archivo .blend, GLB (Android), USDZ (iPhone) e imágenes de cada paso.");
            btnVer.Click += (s, e) => PrepararPrueba();
            btnBlender.Click += (s, e) =>
            {
                try { Process.Start(blenderExe, Comillas2(Path.Combine(proyectoAnim, "04_BLENDER", receta.Pieza + "_ANIM.blend"))); }
                catch (Exception ex) { MostrarEstadoAnim("No se pudo abrir Blender: " + ex.Message, Rojo); refrescar(); }
            };
            btnCarpeta.Click += (s, e) => Abrir(proyectoAnim);
            lblQr.LinkClicked += (s, e) => { if (paginaPrueba != null) Abrir(UrlBase + paginaPrueba); };

            refrescarLista();
            refrescar();
            CargarTira(tira, tip);
        }

        // Redibuja el paso 3 (solo si sigue abierto: un proceso puede terminar con otro paso a la vista)
        void VolverAlAnimador() { if (paso == 2) IrA(2); }

        static ComboBox Combo(string[] items, int x, int y, int w)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Width = w };
            c.Items.AddRange(items);
            return c;
        }

        NumericUpDown Numero(int x, int y, decimal min, decimal max, int decimales)
        {
            return new NumericUpDown
            {
                Location = new Point(x, y), Width = S(90), Minimum = min, Maximum = max, DecimalPlaces = decimales,
                Increment = decimales > 0 ? 0.1m : 5m, TextAlign = HorizontalAlignment.Right,
            };
        }

        static decimal Lim(NumericUpDown n, double v)
        {
            decimal d = (decimal)Math.Round(v, n.DecimalPlaces);
            return Math.Max(n.Minimum, Math.Min(n.Maximum, d));
        }

        void MostrarEstadoAnim(string texto, Color color)
        {
            estadoAnim = texto;
            colorEstadoAnim = color;
            if (lblEstadoAnimVisible != null && !lblEstadoAnimVisible.IsDisposed)
            {
                lblEstadoAnimVisible.Text = texto;
                lblEstadoAnimVisible.ForeColor = color;
            }
        }

        // Pieza → grupo. Primero las elegidas con clic; después, las que no están en ningún grupo pero caben dentro
        // de la caja de un grupo (5 mm de margen) se suman a él solas: caras interiores, gráficas pegadas, cantos
        Dictionary<string, int> Asignacion()
        {
            var mapa = new Dictionary<string, int>();
            if (analisis == null || receta == null) return mapa;
            for (int i = 0; i < receta.Grupos.Count; i++)
                foreach (var n in receta.Grupos[i].Piezas)
                    if (analisis.Indice.ContainsKey(n) && !mapa.ContainsKey(n)) mapa[n] = i;

            var cajas = new List<double[]>();
            for (int i = 0; i < receta.Grupos.Count; i++)
            {
                var ps = receta.Grupos[i].Piezas.Where(analisis.Indice.ContainsKey).Select(n => analisis.Piezas[analisis.Indice[n]]).ToList();
                if (ps.Count == 0) { cajas.Add(null); continue; }
                cajas.Add(new[]
                {
                    ps.Min(p => p.Min[0]), ps.Min(p => p.Min[1]), ps.Min(p => p.Min[2]),
                    ps.Max(p => p.Max[0]), ps.Max(p => p.Max[1]), ps.Max(p => p.Max[2]),
                });
            }
            foreach (var p in analisis.Piezas)
            {
                if (mapa.ContainsKey(p.Nombre)) continue;
                int mejor = -1;
                double volMejor = double.MaxValue;
                for (int i = 0; i < cajas.Count; i++)
                {
                    var c = cajas[i];
                    if (c == null || receta.Grupos[i].Excluidas.Contains(p.Nombre)) continue;
                    bool dentro = true;
                    for (int k = 0; k < 3 && dentro; k++)
                        dentro = p.Min[k] >= c[k] - Tolerancia && p.Max[k] <= c[k + 3] + Tolerancia;
                    if (!dentro) continue;
                    double vol = (c[3] - c[0] + 0.001) * (c[4] - c[1] + 0.001) * (c[5] - c[2] + 0.001);
                    if (vol < volMejor) { volMejor = vol; mejor = i; }
                }
                if (mejor >= 0) mapa[p.Nombre] = mejor;
            }
            return mapa;
        }

        void CargarTira(FlowLayoutPanel tira, ToolTip tip)
        {
            foreach (var c in tira.Controls.Cast<Control>().ToList()) c.Dispose();
            if (proyectoAnim == null) return;
            string dir = Path.Combine(proyectoAnim, "06_PREVIEW");
            if (!Directory.Exists(dir)) return;
            var nombres = new List<string> { "Inicio" };
            try
            {
                var r = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(proyectoAnim, "05_EXPORTAR", "resumen.json"), Encoding.UTF8)) as Dictionary<string, object>;
                object ps;
                if (r != null && r.TryGetValue("pasos", out ps) && ps is object[])
                    foreach (var p in (object[])ps) nombres.Add(Convert.ToString(((Dictionary<string, object>)p)["nombre"]));
            }
            catch { }
            var archivos = Directory.GetFiles(dir, "paso_*.png").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < archivos.Count && i < 8; i++)
            {
                var pb = new PictureBox
                {
                    Size = new Size(S(74), S(56)), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(240, 242, 245),
                    Margin = new Padding(0, 0, S(6), 0), Image = CargarImagen(archivos[i]), Cursor = Cursors.Hand,
                };
                string ruta = archivos[i];
                pb.Click += (s, e) => Abrir(ruta);
                tip.SetToolTip(pb, i < nombres.Count ? (i == 0 ? "Inicio" : "Paso " + i + " · " + nombres[i]) : Path.GetFileName(ruta));
                tira.Controls.Add(pb);
            }
        }

        // Sin dejar el archivo bloqueado (Blender lo vuelve a escribir al generar)
        static Bitmap CargarImagen(string ruta)
        {
            using (var ms = new MemoryStream(File.ReadAllBytes(ruta)))
            using (var img = Image.FromStream(ms))
                return new Bitmap(img);
        }

        // ── Proyecto, Blender y procesos ──
        string CarpetaAnimacion()
        {
            var existente = Directory.GetDirectories(raiz, "001_ANIMACI*").FirstOrDefault();
            string dir = existente ?? Path.Combine(raiz, "001_ANIMACIÓN");
            Directory.CreateDirectory(dir);
            return dir;
        }

        string BuscarBlender()
        {
            var candidatos = new List<string>();
            try
            {
                string cfg = Path.Combine(CarpetaAnimacion(), "_blender.txt");
                if (File.Exists(cfg)) candidatos.Add(File.ReadAllText(cfg, Encoding.UTF8).Trim());
            }
            catch { }
            Func<string, int, IEnumerable<string>> buscar = null;
            buscar = (dir, nivel) =>
            {
                var res = new List<string>();
                try
                {
                    if (!Directory.Exists(dir)) return res;
                    string exe = Path.Combine(dir, "blender.exe");
                    if (File.Exists(exe)) res.Add(exe);
                    if (nivel > 0)
                        foreach (var sub in Directory.GetDirectories(dir))
                        {
                            string n = Path.GetFileName(sub).ToLowerInvariant();
                            if (n.StartsWith(".") || n == "python" || n == "datafiles" || n == "scripts") continue;
                            res.AddRange(buscar(sub, nivel - 1));
                        }
                }
                catch { }
                return res;
            };
            foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable("ProgramW6432") })
                if (!string.IsNullOrEmpty(pf)) candidatos.AddRange(buscar(Path.Combine(pf, "Blender Foundation"), 1));
            string padre = Path.GetDirectoryName(raiz);
            if (padre != null) candidatos.AddRange(buscar(Path.Combine(padre, "00_HERRAMIENTAS"), 3));
            try { candidatos.AddRange(buscar(CarpetaAnimacion(), 4)); } catch { }
            return candidatos.FirstOrDefault(File.Exists);
        }

        // Retoma un proyecto existente (receta y vistas) sin volver a analizar
        void CargarProyectoAnim(string nombre, bool crear)
        {
            if (nombre == "" || ValidarNombre(nombre, "pieza") != null) return;
            string dir = Path.Combine(CarpetaAnimacion(), nombre);
            if (!crear && !Directory.Exists(dir)) return;
            if (proyectoAnim != dir) { ocultas.Clear(); vistasSin = null; claveVistasSin = null; }
            proyectoAnim = dir;
            try
            {
                string rr = Path.Combine(dir, "animacion.json");
                receta = File.Exists(rr) ? RecetaAnim.Cargar(rr) : null;
                if (receta == null) receta = new RecetaAnim { Pieza = nombre };
                receta.Pieza = nombre;
                analisis = CargarAnalisis(Path.Combine(dir, "_animador"));
                grupoActual = receta.Grupos.Count > 0 ? 0 : -1;
                ultimaPiezaClic = -1;
                string pagina = Path.Combine(raiz, "pruebas", EnlacePrueba(nombre), "index.html");
                paginaPrueba = File.Exists(pagina) ? "pruebas/" + EnlacePrueba(nombre) + "/" : null;
                if (analisis != null) MostrarEstadoAnim("Proyecto retomado: " + dir.Substring(raiz.Length + 1), Gris);
            }
            catch (Exception ex)
            {
                analisis = null;
                MostrarEstadoAnim("No se pudo leer el proyecto de animación: " + ex.Message, Rojo);
            }
        }

        static AnalisisAnim CargarAnalisis(string dir)
        {
            string ruta = Path.Combine(dir, "piezas.json");
            if (!File.Exists(ruta)) return null;
            var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(File.ReadAllText(ruta, Encoding.UTF8)) as Dictionary<string, object>;
            var a = new AnalisisAnim { Ancho = Convert.ToInt32(d["ancho"]), Alto = Convert.ToInt32(d["alto"]) };
            foreach (var o in (object[])d["piezas"])
            {
                var p = (Dictionary<string, object>)o;
                var pz = new PiezaAnim { Nombre = Convert.ToString(p["nombre"]), Material = Convert.ToString(p["material"]), Triangulos = Convert.ToInt32(p["triangulos"]) };
                var mn = (object[])p["min"];
                var mx = (object[])p["max"];
                for (int k = 0; k < 3; k++)
                {
                    pz.Min[k] = Convert.ToDouble(mn[k], CultureInfo.InvariantCulture);
                    pz.Max[k] = Convert.ToDouble(mx[k], CultureInfo.InvariantCulture);
                }
                object rects;
                if (p.TryGetValue("rects", out rects) && rects is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)rects)
                        pz.Rects[kv.Key] = ((object[])kv.Value).Select(x => Convert.ToDouble(x, CultureInfo.InvariantCulture)).ToArray();
                a.Indice[pz.Nombre] = a.Piezas.Count;
                a.Piezas.Add(pz);
            }
            a.Vistas = CargarVistas(dir, ((object[])d["vistas"]).Select(o => Convert.ToString(o)), a);
            foreach (var v in a.Vistas)
                foreach (var id in v.Ids) if (id > 0 && id <= a.Piezas.Count) a.Piezas[id - 1].Pixeles++;
            return a;
        }

        // vista_X.png + vista_X.ids de una carpeta (las del análisis, o las redibujadas sin las piezas ocultas)
        static List<VistaAnim> CargarVistas(string dir, IEnumerable<string> nombres, AnalisisAnim a)
        {
            var lista = new List<VistaAnim>();
            foreach (var n in nombres)
            {
                var bytes = File.ReadAllBytes(Path.Combine(dir, "vista_" + n + ".ids"));
                var ids = new ushort[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, ids, 0, ids.Length * 2);
                if (ids.Length != a.Ancho * a.Alto) throw new InvalidDataException("vista " + n + " incompleta");
                var img = CargarImagen(Path.Combine(dir, "vista_" + n + ".png"));
                if (img.PixelFormat != PixelFormat.Format32bppArgb)
                {
                    var conv = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(conv)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                    img.Dispose();
                    img = conv;
                }
                lista.Add(new VistaAnim { Nombre = n, Imagen = img, Ids = ids });
            }
            return lista;
        }

        int AnchoOjo { get { return S(34); } }

        // ── Piezas ocultas (ojo 👁 de cada grupo y tecla H) ──
        // Al ocultar, las piezas se oscurecen y dejan de responder al clic al instante; enseguida Blender redibuja las
        // vistas sin ellas (vistas_sin) para que se vea, y se pueda elegir, lo que estaba detrás.
        readonly HashSet<string> ocultas = new HashSet<string>();
        List<VistaAnim> vistasSin;        // vistas redibujadas sin las ocultas
        string claveVistasSin;            // con qué conjunto de ocultas se redibujaron
        bool redibujando, redibujarOtraVez;
        System.Windows.Forms.Timer temporizadorOcultas;

        static string ClaveOcultas(IEnumerable<string> o) { return string.Join("|", o.OrderBy(x => x, StringComparer.Ordinal)); }

        // Vistas a mostrar: las redibujadas si corresponden a lo que está oculto ahora; si no, las completas
        List<VistaAnim> VistasActuales()
        {
            if (analisis == null) return null;
            if (ocultas.Count > 0 && vistasSin != null && claveVistasSin == ClaveOcultas(ocultas)) return vistasSin;
            return analisis.Vistas;
        }

        void CambiarOcultas(IEnumerable<string> nombres, bool ocultar)
        {
            foreach (var n in nombres.ToList()) { if (ocultar) ocultas.Add(n); else ocultas.Remove(n); }
            if (refrescarAnimador != null) refrescarAnimador();
            if (temporizadorOcultas == null)
            {
                temporizadorOcultas = new System.Windows.Forms.Timer { Interval = 600 };
                temporizadorOcultas.Tick += (s, e) => { temporizadorOcultas.Stop(); RedibujarSinOcultas(); };
            }
            temporizadorOcultas.Stop();
            temporizadorOcultas.Start();   // espera a que termines de hacer clic en los ojos
        }

        void RedibujarSinOcultas()
        {
            if (ocultas.Count == 0 || analisis == null || proyectoAnim == null || blenderExe == null)
            {
                if (refrescarAnimador != null) refrescarAnimador();
                return;
            }
            string clave = ClaveOcultas(ocultas);
            if (clave == claveVistasSin && vistasSin != null) { if (refrescarAnimador != null) refrescarAnimador(); return; }
            if (redibujando) { redibujarOtraVez = true; return; }
            redibujando = true;
            if (refrescarAnimador != null) refrescarAnimador();
            string dir = Path.Combine(proyectoAnim, "_animador", "ocultando");
            string proyecto = proyectoAnim;
            var a = analisis;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "ocultas.json"), "[" + string.Join(",", ocultas.Select(Colecciones.Json)) + "]", new UTF8Encoding(false));
            }
            catch { redibujando = false; return; }
            var psi = new ProcessStartInfo(blenderExe, ArgsBlender("vistas_sin", Path.Combine(proyecto, "02_IMPORTAR", receta.Pieza + ".glb"), dir, Path.Combine(dir, "ocultas.json")))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = raiz,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            new Thread(() =>
            {
                List<VistaAnim> nuevas = null;
                try
                {
                    using (var p = Process.Start(psi))
                    {
                        p.StandardOutput.ReadToEnd();
                        p.StandardError.ReadToEnd();
                        p.WaitForExit();
                        if (p.ExitCode == 0) nuevas = CargarVistas(dir, a.Vistas.Select(v => v.Nombre), a);
                    }
                }
                catch { }
                EnVentana(() =>
                {
                    redibujando = false;
                    if (nuevas != null && proyectoAnim == proyecto) { vistasSin = nuevas; claveVistasSin = clave; }
                    if (redibujarOtraVez) { redibujarOtraVez = false; RedibujarSinOcultas(); return; }
                    if (refrescarAnimador != null) refrescarAnimador();
                });
            }) { IsBackground = true }.Start();
        }

        // Nombre de carpeta para pruebas\: sin tildes ni espacios
        static string EnlacePrueba(string nombre)
        {
            string s = nombre.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in s)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            string r = Regex.Replace(sb.ToString(), "[^a-z0-9]+", "-").Trim('-');
            return r == "" ? "pieza" : r;
        }

        void AnalizarPieza()
        {
            string nombre = nombrePieza;
            string error = archivoEntrada == "" ? "Elige el archivo .glb exportado desde Rhino (Examinar…)."
                : !File.Exists(archivoEntrada) ? "El archivo ya no existe en esa ubicación."
                : ValidarNombre(nombre, "pieza") ?? (blenderExe == null ? "Falta Blender: haz clic en «Falta Blender» y elige blender.exe." : null);
            if (error != null) { MostrarEstadoAnim(error, Rojo); if (refrescarAnimador != null) refrescarAnimador(); return; }

            proyectoAnim = Path.Combine(CarpetaAnimacion(), nombre);
            string importar = Path.Combine(proyectoAnim, "02_IMPORTAR");
            string vistas = Path.Combine(proyectoAnim, "_animador");
            Directory.CreateDirectory(importar);
            Directory.CreateDirectory(vistas);
            string glb = Path.Combine(importar, nombre + ".glb");

            // 1. Mapeo de las copias reparado (el mismo arreglo del optimizador), 2. vistas en Blender
            EjecutarAnim("node", Comillas2(Path.Combine(raiz, "scripts", "reparar-uv.js")) + " " + Comillas2(archivoEntrada) + " " + Comillas2(glb),
                "Preparando la pieza", (ok, msg) =>
            {
                if (!ok) { MostrarEstadoAnim("No se pudo preparar la pieza: " + msg, Rojo); VolverAlAnimador(); return; }
                EjecutarAnim(blenderExe, ArgsBlender("analizar", glb, vistas), "Analizando en Blender", (ok2, msg2) =>
                {
                    if (!ok2) { MostrarEstadoAnim("No se pudo analizar: " + msg2, Rojo); VolverAlAnimador(); return; }
                    analisis = null;
                    ocultas.Clear(); vistasSin = null; claveVistasSin = null;   // las piezas pueden haber cambiado
                    CargarProyectoAnim(nombre, true);
                    if (analisis != null)
                        MostrarEstadoAnim("Listo: " + analisis.Piezas.Count + " piezas. Crea un grupo (+ Grupo), haz clic en sus piezas y abre ▶ Vista previa en vivo para ver el movimiento mientras lo configuras.", Verde);
                    VolverAlAnimador();
                });
            });
        }

        string ArgsBlender(params string[] args)
        {
            return "--background --factory-startup --python-exit-code 1 --python " + Comillas2(Path.Combine(raiz, "scripts", "animador.py"))
                + " -- " + string.Join(" ", args.Select(Comillas2));
        }

        // Vista previa en vivo: herramientas\vista-animador\ reproduce animacion.json en el navegador y se rearma sola
        // cuando la receta cambia. Necesita _animador\vista.glb (lo crea «Analizar»; en proyectos anteriores, se crea aquí)
        void AbrirVistaEnVivo()
        {
            if (proyectoAnim == null) return;
            string vista = Path.Combine(proyectoAnim, "_animador", "vista.glb");
            string relativo = proyectoAnim.Substring(raiz.Length).Trim('\\');
            string url = "herramientas/vista-animador/?p=" + string.Join("/", relativo.Split('\\').Select(Uri.EscapeDataString));
            if (File.Exists(vista)) { AbrirLocal(url); return; }
            string glb = Path.Combine(proyectoAnim, "02_IMPORTAR", receta.Pieza + ".glb");
            EjecutarAnim(blenderExe, ArgsBlender("vista", glb, Path.Combine(proyectoAnim, "_animador")), "Preparando la vista previa", (ok, msg) =>
            {
                if (ok) { MostrarEstadoAnim("Vista previa abierta en tu navegador: cada cambio aquí se ve allá al instante.", Verde); AbrirLocal(url); }
                else MostrarEstadoAnim("No se pudo preparar la vista previa: " + msg, Rojo);
                VolverAlAnimador();
            });
        }

        // ¿o viaja (directa o indirectamente) con g? Para no permitir círculos en «Se mueve con»
        bool DesciendeDe(GrupoAnim o, GrupoAnim g)
        {
            var visto = new HashSet<string>();
            for (var x = o; x != null && x.Padre != null && visto.Add(x.Id); x = receta.Grupos.FirstOrDefault(y => y.Id == x.Padre))
                if (x.Padre == g.Id) return true;
            return false;
        }

        // Pone o quita piezas de un grupo respetando los bloqueos. soloLibres: deja en paz las que ya tienen grupo.
        // Devuelve el resumen para mostrar; importante = algo no se pudo hacer.
        string CambiarPiezas(int gi, List<string> nombres, bool agregar, bool soloLibres, out bool importante)
        {
            importante = false;
            var g = receta.Grupos[gi];
            if (g.Bloqueado)
            {
                importante = true;
                return "«" + g.Nombre + "» está bloqueado (🔒): desbloquéalo para cambiar sus piezas.";
            }
            var asig = Asignacion();
            int cambiadas = 0, bloqueadas = 0, ocupadas = 0;
            foreach (var n in nombres)
            {
                int otro;
                bool tiene = asig.TryGetValue(n, out otro);
                if (agregar)
                {
                    if (tiene && otro == gi && g.Piezas.Contains(n)) continue;
                    if (tiene && otro != gi)
                    {
                        if (receta.Grupos[otro].Bloqueado) { bloqueadas++; continue; }
                        if (soloLibres) { ocupadas++; continue; }
                    }
                    foreach (var x in receta.Grupos) if (x != g) x.Piezas.Remove(n);   // una pieza está en un solo grupo
                    g.Piezas.Add(n);
                    g.Excluidas.Remove(n);
                    cambiadas++;
                }
                else if (tiene && otro == gi)
                {
                    g.Piezas.Remove(n);
                    g.Excluidas.Add(n);   // así no vuelve a incluirse sola
                    cambiadas++;
                }
            }
            var partes = new List<string>();
            if (cambiadas > 0 || (bloqueadas == 0 && ocupadas == 0))
                partes.Add(cambiadas + (cambiadas == 1 ? " pieza " : " piezas ") + (agregar ? "agregada" : "quitada") + (cambiadas == 1 ? "" : "s") + " en «" + g.Nombre + "»");
            if (ocupadas > 0) partes.Add(ocupadas + " ya estaban en otro grupo y no se tocaron (Shift + arrastrar para tomarlas)");
            if (bloqueadas > 0) { partes.Add(bloqueadas + " están en un grupo bloqueado 🔒"); importante = true; }
            return string.Join(" · ", partes) + ".";
        }

        // Ajuste enviado por el gumball de la vista previa (llega por el servidor local, en otro hilo).
        // Cuerpo: { "p": carpeta del proyecto, "grupo": n, y lo que cambió: "pivote"+"eje", "angulo", "desplazamiento",
        //          o "automatico": true para volver a lo automático }
        string RecibirAjusteVistaPrevia(string cuerpo)
        {
            if (IsDisposed) return "Flujo AR se cerró";
            try { return (string)Invoke((Func<string>)(() => AplicarAjuste(cuerpo))); }
            catch (Exception ex) { return ex.Message; }
        }

        string AplicarAjuste(string cuerpo)
        {
            var d = new JavaScriptSerializer().DeserializeObject(cuerpo) as Dictionary<string, object>;
            if (d == null) return "Datos inválidos";
            string relativo = proyectoAnim == null ? null : proyectoAnim.Substring(raiz.Length).Trim('\\').Replace('\\', '/');
            if (receta == null || analisis == null || relativo == null || !string.Equals(Convert.ToString(d["p"]).Trim('/'), relativo, StringComparison.OrdinalIgnoreCase))
                return "Abre esta pieza en el paso 3 de Flujo AR (Pieza animada) para guardar los ajustes.";
            int i = Convert.ToInt32(d["grupo"]);
            if (i < 0 || i >= receta.Grupos.Count) return "Ese grupo ya no existe.";
            var g = receta.Grupos[i];
            object v;
            if (d.TryGetValue("automatico", out v) && Convert.ToBoolean(v))
            {
                if (g.Tipo == "entrar") g.Desplazamiento = null;
                else { g.Pivote = null; g.Eje = null; }
            }
            var piv = RecetaAnim.Vec(d, "pivote");
            var eje = RecetaAnim.Vec(d, "eje");
            if (piv != null && eje != null) { g.Pivote = piv; g.Eje = eje; }
            var desp = RecetaAnim.Vec(d, "desplazamiento");
            if (desp != null) g.Desplazamiento = desp;
            if (d.TryGetValue("bloqueado", out v) && v != null) g.Bloqueado = Convert.ToBoolean(v);
            string resumen = null;
            object agregar, quitar;
            bool importante = false;
            if (d.TryGetValue("agregar", out agregar) && agregar is object[] && ((object[])agregar).Length > 0)
                resumen = CambiarPiezas(i, ((object[])agregar).Select(x => Convert.ToString(x)).ToList(), true,
                    d.ContainsKey("solo_libres") && Convert.ToBoolean(d["solo_libres"]), out importante);
            if (d.TryGetValue("quitar", out quitar) && quitar is object[] && ((object[])quitar).Length > 0)
                resumen = CambiarPiezas(i, ((object[])quitar).Select(x => Convert.ToString(x)).ToList(), false, false, out importante);
            if (d.TryGetValue("angulo", out v) && v != null) g.Angulo = Math.Max(-720, Math.Min(720, Convert.ToDouble(v, CultureInfo.InvariantCulture)));
            receta.Guardar(Path.Combine(proyectoAnim, "animacion.json"), Asignacion());
            bool otroGrupo = grupoActual != i;
            grupoActual = i;
            MostrarEstadoAnim(resumen != null ? "Desde la vista previa: " + resumen : "Ajuste del gumball guardado en «" + g.Nombre + "» · " + DateTime.Now.ToString("HH:mm:ss"), importante ? Naranja : Verde);
            if (otroGrupo) VolverAlAnimador();
            else if (paso == 2 && refrescarAnimador != null) refrescarAnimador();
            return importante ? resumen : null;   // la vista previa lo muestra como aviso
        }

        // El grupo elegido, para que la vista previa marque su bisagra y pueda repetir solo su paso
        string seleccionEscrita;
        void GuardarSeleccion()
        {
            if (proyectoAnim == null || analisis == null) return;
            string texto = "{\"grupo\": " + grupoActual + "}";
            string ruta = Path.Combine(proyectoAnim, "_animador", "seleccion.json");
            if (texto == seleccionEscrita && File.Exists(ruta)) return;
            try { File.WriteAllText(ruta, texto, new UTF8Encoding(false)); seleccionEscrita = texto; } catch { }
        }

        void GenerarAnimacion()
        {
            if (receta == null || proyectoAnim == null) return;
            try { receta.Guardar(Path.Combine(proyectoAnim, "animacion.json"), Asignacion()); }
            catch (Exception ex) { MostrarEstadoAnim("No se pudo guardar la receta: " + ex.Message, Rojo); return; }
            EjecutarAnim(blenderExe, ArgsBlender("generar", proyectoAnim), "Generando la animación", (ok, msg) =>
            {
                if (ok)
                {
                    paginaPrueba = null;
                    MostrarEstadoAnim("Animación generada (" + msg + "). Revisa las imágenes de cada paso y, para el AR, pulsa «Preparar prueba en celulares».", Verde);
                }
                else MostrarEstadoAnim("No se pudo generar: " + msg, Rojo);
                VolverAlAnimador();
            });
        }

        void PrepararPrueba()
        {
            if (receta == null || proyectoAnim == null) return;
            string enlace = EnlacePrueba(receta.Pieza);
            string script = Path.Combine(raiz, "scripts", "preparar-prueba-animacion.ps1");
            string args = "-NoProfile -ExecutionPolicy Bypass -File " + Comillas2(script) + " -Proyecto " + Comillas2(proyectoAnim)
                + " -Enlace " + enlace + " -Titulo " + Comillas2(receta.Pieza.Replace("_", " ").Replace("\"", ""));
            EjecutarAnim("powershell.exe", args, "Preparando la página de prueba (la primera vez puede tardar)", (ok, msg) =>
            {
                if (!ok) { MostrarEstadoAnim("No se pudo preparar la prueba: " + msg, Rojo); VolverAlAnimador(); return; }
                paginaPrueba = "pruebas/" + enlace + "/";
                MostrarEstadoAnim("Abierta en tu navegador. Para el AR en los celulares: publica (paso 6) y escanea el QR de la derecha → " + UrlBase + paginaPrueba, Verde);
                AbrirLocal(paginaPrueba);
                VolverAlAnimador();
            });
        }

        // Ejecuta un proceso en segundo plano. Las líneas @@PROGRESO actualizan el estado; @@ERROR es el mensaje de error;
        // @@OK el resultado. fin(ok, mensaje) se llama en la ventana.
        void EjecutarAnim(string exe, string args, string que, Action<bool, string> fin)
        {
            trabajandoAnim = true;
            MostrarEstadoAnim(que + "…", Gris);
            if (refrescarAnimador != null) refrescarAnimador();
            Cursor = Cursors.WaitCursor;
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = raiz,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            new Thread(() =>
            {
                string error = null, resultado = "", ultimoError = null;
                int codigo = -1;
                try
                {
                    using (var p = new Process { StartInfo = psi })
                    {
                        p.OutputDataReceived += (s, e) =>
                        {
                            if (e.Data == null) return;
                            string l = Ansi.Replace(e.Data, "").Trim();
                            if (l.StartsWith("@@PROGRESO ")) { string t = que + ": " + l.Substring(11); EnVentana(() => MostrarEstadoAnim(t + "…", Gris)); }
                            else if (l.StartsWith("@@ERROR ")) error = l.Substring(8);
                            else if (l.StartsWith("@@OK")) resultado = l.Substring(4).Trim();
                        };
                        p.ErrorDataReceived += (s, e) =>
                        {
                            if (e.Data == null) return;
                            string l = Ansi.Replace(e.Data, "").Trim();
                            if (l != "" && !l.StartsWith("info:") && !l.StartsWith("npm") && !l.StartsWith("Warning") && !l.Contains("DeprecationWarning")) ultimoError = l;
                        };
                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        p.WaitForExit();
                        codigo = p.ExitCode;
                    }
                }
                catch (Exception ex) { error = "No se pudo ejecutar " + Path.GetFileName(exe) + ": " + ex.Message; }
                bool ok = codigo == 0 && error == null;
                string msg = ok ? resultado : (error ?? ultimoError ?? "terminó con código " + codigo);
                EnVentana(() =>
                {
                    trabajandoAnim = false;
                    Cursor = Cursors.Default;
                    fin(ok, msg);
                });
            }) { IsBackground = true }.Start();
        }
    }
}

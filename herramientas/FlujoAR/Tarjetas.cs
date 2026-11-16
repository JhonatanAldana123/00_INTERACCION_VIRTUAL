using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FlujoAR
{
    // Paso 8: Tarjetas AR.
    //
    // Las tarjetas impresas llevan un QR fijo (innerciaar.com/t/1 … t/4). Aquí se decide qué abre cada una:
    // la campaña y la pieza (las mismas del paso 7), el nombre que ve el cliente, la dirección que queda en la
    // barra del navegador y la presentación que se reproduce sola antes de la pieza (un video o un PDF).
    //
    // Todo queda en tarjetas.json (raíz del proyecto). Los archivos de la presentación de cada tarjeta van en
    // tarjetas\<n>\ (video.mp4, o pagina-01.jpg… si es un PDF: scripts\pdf-a-imagenes.ps1 lo convierte).
    // Al guardar se ejecuta scripts\generar-tarjetas.js, que valida y crea la página de cada dirección.
    // La experiencia que lee todo esto es pruebas\tarjeta\index.html.
    partial class Ventana
    {
        class TarjetaAr
        {
            public string Numero;
            public string Coleccion = "", Pieza = "", Nombre = "", Direccion = "";
            // Presentación guardada: "" = video general, "video" o "pdf"
            public string PresTipo = "", PresArchivo = "", PresVersion = "", PresOrigen = "";
            public List<string> PresPaginas = new List<string>();
            public int PresSegundos = 5;
            // Cambio elegido que todavía no se guarda: null = ninguno, "video" / "pdf" (PendArchivo) o "" = quitar
            public string PendTipo, PendArchivo;
        }

        const string MarcaPaginaTarjeta = "tarjeta-ar: página generada";
        const string NotaTarjetas = "Qué abre cada tarjeta impresa. El QR de la tarjeta 1 siempre dice innerciaar.com/t/1; aquí se decide la pieza (coleccion y pieza, como en colecciones.json), el nombre que ve el cliente, la dirección que queda en la barra del navegador y la presentación (video o PDF). Se edita desde el paso 8 de Flujo AR (o a mano, y luego doble clic en ActualizarTarjetas.bat). Después se publica. No hay que reimprimir.";
        static readonly Regex ParteDireccion = new Regex(@"^[A-Za-z0-9_-]+$");

        string selTarjeta = "1";
        Image miniTarjeta;

        void PasoTarjetas(FlowLayoutPanel col)
        {
            col.Controls.Add(Texto("Elige una tarjeta y decide qué campaña y pieza abre (las del paso 7) y qué se reproduce antes: un video o un PDF. El QR impreso no cambia. «Probar» la muestra sin cámara; con la tarjeta real se prueba en el celular, después de publicar."));
            if (!CargarCampanas(col)) return;

            string errorLectura;
            var tarjetas = LeerTarjetas(out errorLectura);
            if (errorLectura != null || tarjetas.Count == 0)
            {
                var l = Estado();
                l.ForeColor = Rojo;
                l.Text = errorLectura ?? "No hay tarjetas: faltan las páginas fijas t\\1, t\\2… del proyecto.";
                col.Controls.Add(l);
                return;
            }
            if (!tarjetas.Any(x => x.Numero == selTarjeta)) selTarjeta = tarjetas[0].Numero;
            CargarMiniTarjeta();

            // ── Las tarjetas ──
            var fNegrita = new Font(Font, FontStyle.Bold);
            var fChica = new Font(Font.FontFamily, 8.5f);
            var fila = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, MaximumSize = new Size(S(670), 0), Margin = new Padding(0, 0, 0, S(8)),
            };
            var mosaicos = new List<Panel>();
            Action<TarjetaAr> mostrar = null;

            foreach (var tarjeta in tarjetas)
            {
                var t = tarjeta;
                var mosaico = new Panel { Width = S(152), Height = S(158), Margin = new Padding(0, 0, S(10), S(6)), Cursor = Cursors.Hand };
                mosaico.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    bool elegida = t.Numero == selTarjeta;
                    g.Clear(elegida ? Color.FromArgb(228, 249, 246) : Color.FromArgb(247, 248, 250));
                    int grosor = elegida ? Math.Max(2, S(2)) : 1;
                    using (var p = new Pen(elegida ? Acento : Color.FromArgb(222, 225, 231), grosor))
                        g.DrawRectangle(p, grosor / 2, grosor / 2, mosaico.Width - grosor, mosaico.Height - grosor);

                    int alto = S(84), ancho = alto * 45 / 80, x = (mosaico.Width - ancho) / 2, y = S(10);
                    if (miniTarjeta != null) g.DrawImage(miniTarjeta, x, y, ancho, alto);
                    else using (var b = new SolidBrush(Color.FromArgb(0, 90, 124))) g.FillRectangle(b, x, y, ancho, alto);

                    // Número de la tarjeta
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int d = S(26);
                    var circulo = new Rectangle(x + ancho - d / 2, y - S(6), d, d);
                    using (var b = new SolidBrush(elegida ? Acento : Oscuro)) g.FillEllipse(b, circulo);
                    TextRenderer.DrawText(g, t.Numero, fNegrita, circulo, elegida ? Oscuro : Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                    string detalle = t.Coleccion == "" ? "Sin asignar" : t.Nombre != "" ? t.Nombre : t.Coleccion;
                    string pres = PresentacionCorta(t);
                    TextRenderer.DrawText(g, detalle, fChica, new Rectangle(S(6), y + alto + S(6), mosaico.Width - S(12), S(34)), Oscuro,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, pres, fChica, new Rectangle(S(6), y + alto + S(40), mosaico.Width - S(12), S(18)), Gris,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                };
                mosaico.Click += (s, e) =>
                {
                    selTarjeta = t.Numero;
                    foreach (var m in mosaicos) m.Invalidate();
                    mostrar(t);
                };
                mosaicos.Add(mosaico);
                fila.Controls.Add(mosaico);
            }
            col.Controls.Add(fila);

            // ── Editor de la tarjeta elegida ──
            var lblTarjeta = new Label { AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 12f), ForeColor = Oscuro, Margin = new Padding(0, 0, 0, S(6)) };
            var cmbCampana = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(300), Margin = new Padding(0, S(2), S(8), S(4)) };
            var cmbPieza = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(300), Margin = new Padding(0, S(2), S(8), S(4)) };
            foreach (var c in campanas) cmbCampana.Items.Add(c.Titulo + "   (" + c.Modelos.Count + (c.Modelos.Count == 1 ? " pieza)" : " piezas)"));
            var txtNombre = new TextBox { Width = S(300), Margin = new Padding(0, S(2), S(8), 0) };
            var lblSitio = new Label { Text = UrlBase.Replace("https://", "").Replace("http://", ""), AutoSize = true, UseMnemonic = false, ForeColor = Gris, Margin = new Padding(0, S(6), S(2), 0) };
            var txtDireccion = new TextBox { Width = S(204), Margin = new Padding(0, S(2), 0, 0) };
            var lblPres = new Label { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(S(640), 0), Margin = new Padding(0, 0, 0, S(6)) };
            var btnVideo = Boton("Cargar video…", false);
            var btnPdf = Boton("Cargar PDF…", false);
            var btnQuitar = Boton("Quitar", false);
            var lblSeg = new Label { Text = "Segundos por página", AutoSize = true, Margin = new Padding(S(8), S(7), S(6), 0) };
            var numSeg = new NumericUpDown { Minimum = 2, Maximum = 60, Value = 5, Width = S(56), Margin = new Padding(0, S(4), 0, 0) };
            var btnGuardar = Boton("Guardar tarjeta", true);
            var btnProbar = Boton("Guardar y probar en el navegador", false);
            var btnPublicada = Boton("Abrir la publicada", false);
            var lblEstado = Estado();

            TarjetaAr actual = null;
            bool cargando = false;

            Func<Coleccion> campana = () => CampanaElegida(cmbCampana);
            Func<Modelo> pieza = () =>
            {
                var c = campana();
                return c != null && cmbPieza.SelectedIndex >= 0 && cmbPieza.SelectedIndex < c.Modelos.Count ? c.Modelos[cmbPieza.SelectedIndex] : null;
            };
            Action mostrarPres = () =>
            {
                if (actual == null) return;
                lblPres.Text = PresentacionLarga(actual, (int)numSeg.Value);
                string tipo = actual.PendTipo ?? actual.PresTipo;
                lblSeg.Visible = numSeg.Visible = tipo == "pdf";
                btnQuitar.Enabled = tipo != "";
            };
            Action llenarPiezas = () =>
            {
                cmbPieza.Items.Clear();
                var c = campana();
                if (c == null) return;
                foreach (var m in c.Modelos) cmbPieza.Items.Add(m.Nombre);
            };
            // Al cambiar de campaña o pieza se proponen el nombre y la dirección (se pueden editar)
            Action sugerir = () =>
            {
                var c = campana();
                var m = pieza();
                if (cargando || actual == null || c == null || m == null) return;
                txtNombre.Text = c.Titulo + " · " + NombreLimpio(m.Nombre);
                txtDireccion.Text = SugerirDireccion(c, m, actual, tarjetas);
            };

            mostrar = t =>
            {
                cargando = true;
                actual = t;
                lblTarjeta.Text = "Tarjeta " + t.Numero + "   ·   su QR impreso abre " + lblSitio.Text + "t/" + t.Numero;
                int ic = campanas.FindIndex(c => c.Clave == t.Coleccion);
                cmbCampana.SelectedIndex = -1;
                cmbCampana.SelectedIndex = ic;
                llenarPiezas();
                var col2 = campana();
                if (col2 != null && col2.Modelos.Count > 0)
                {
                    int ip = col2.Modelos.FindIndex(m => m.Id == t.Pieza);
                    cmbPieza.SelectedIndex = ip >= 0 ? ip : 0;
                }
                txtNombre.Text = t.Nombre;
                txtDireccion.Text = t.Direccion;
                numSeg.Value = Math.Max(numSeg.Minimum, Math.Min(numSeg.Maximum, t.PresSegundos));
                lblEstado.Text = "";
                cargando = false;
                mostrarPres();
            };

            cmbCampana.SelectedIndexChanged += (s, e) =>
            {
                if (cargando) return;
                llenarPiezas();
                if (cmbPieza.Items.Count > 0) cmbPieza.SelectedIndex = 0;      // dispara «sugerir»
            };
            cmbPieza.SelectedIndexChanged += (s, e) => sugerir();
            numSeg.ValueChanged += (s, e) => { if (!cargando) mostrarPres(); };

            btnVideo.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Title = "Video de la presentación", Filter = "Video MP4 (*.mp4)|*.mp4" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    long peso = new FileInfo(dlg.FileName).Length;
                    if (peso > 95L * 1048576)
                    {
                        lblEstado.ForeColor = Rojo;
                        lblEstado.Text = "Ese video pesa " + MB(peso) + ": GitHub no recibe archivos de más de 100 MB. Expórtalo más liviano (lo ideal para celular: menos de 15 MB).";
                        return;
                    }
                    actual.PendTipo = "video";
                    actual.PendArchivo = dlg.FileName;
                    lblEstado.ForeColor = peso > 25L * 1048576 ? Naranja : Gris;
                    lblEstado.Text = peso > 25L * 1048576
                        ? "Pesa " + MB(peso) + ": con datos móviles tardará en empezar. Lo ideal es menos de 15 MB. Pulsa «Guardar tarjeta» para dejarlo."
                        : "Pulsa «Guardar tarjeta» para dejarlo.";
                    mostrarPres();
                }
            };
            btnPdf.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Title = "PDF de la presentación", Filter = "PDF (*.pdf)|*.pdf" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    actual.PendTipo = "pdf";
                    actual.PendArchivo = dlg.FileName;
                    lblEstado.ForeColor = Gris;
                    lblEstado.Text = "Pulsa «Guardar tarjeta»: cada página se convierte en una imagen y pasan solas, una tras otra.";
                    mostrarPres();
                }
            };
            btnQuitar.Click += (s, e) =>
            {
                actual.PendTipo = actual.PresTipo == "" ? null : "";
                actual.PendArchivo = null;
                lblEstado.Text = "";
                mostrarPres();
            };

            Func<bool> guardar = () =>
            {
                var c = campana();
                var m = pieza();
                if (c == null || m == null) { lblEstado.ForeColor = Rojo; lblEstado.Text = "Elige la campaña y la pieza que abre esta tarjeta."; return false; }
                string nombre = txtNombre.Text.Trim(), direccion = txtDireccion.Text.Trim().Trim('/');
                if (nombre == "") { lblEstado.ForeColor = Rojo; lblEstado.Text = "Escribe el nombre que verá el cliente."; return false; }
                if (direccion != "" && direccion.Split('/').Any(p => !ParteDireccion.IsMatch(p.Trim())))
                {
                    lblEstado.ForeColor = Rojo;
                    lblEstado.Text = "La dirección solo puede llevar letras sin tilde, números, guion (-) y guion bajo (_), separados por /. Ejemplo: showroom/isla-axe";
                    return false;
                }

                lblEstado.ForeColor = Gris;
                lblEstado.Text = actual.PendTipo == "pdf" ? "Guardando y convirtiendo el PDF…" : "Guardando…";
                lblEstado.Refresh();
                Cursor = Cursors.WaitCursor;
                string error;
                bool ok;
                try { ok = GuardarTarjeta(actual, tarjetas, c.Clave, m.Id, nombre, direccion, (int)numSeg.Value, out error); }
                finally { Cursor = Cursors.Default; }

                foreach (var mo in mosaicos) mo.Invalidate();
                mostrarPres();
                lblEstado.ForeColor = ok ? Verde : Rojo;
                lblEstado.Text = ok
                    ? "Tarjeta " + actual.Numero + " guardada · " + DateTime.Now.ToString("HH:mm:ss") + ". Para que el QR impreso la muestre, publica (paso 6: Commit to main y Push origin)."
                    : error;
                return ok;
            };
            btnGuardar.Click += (s, e) => guardar();
            btnProbar.Click += (s, e) => { if (guardar()) AbrirLocal("pruebas/tarjeta/?t=" + actual.Numero + "&demo=1"); };
            btnPublicada.Click += (s, e) => Abrir(UrlBase + "t/" + actual.Numero + "/");

            // Dos columnas, para que todo quepa sin desplazar la ventana
            Func<string, Control, Control> bloque = (titulo, control) =>
            {
                var b = Columna();
                b.Margin = new Padding(0, 0, S(8), 0);
                var e = Etiqueta(titulo);
                e.Margin = new Padding(0, 0, 0, S(2));
                b.Controls.Add(e);
                b.Controls.Add(control);
                return b;
            };
            var filaDireccion = Fila(lblSitio, txtDireccion);
            filaDireccion.Margin = Padding.Empty;
            col.Controls.Add(lblTarjeta);
            col.Controls.Add(Fila(bloque("Campaña", cmbCampana), bloque("Pieza que abre", cmbPieza)));
            col.Controls.Add(Fila(bloque("Nombre que ve el cliente", txtNombre), bloque("Dirección en la barra del navegador", filaDireccion)));
            var etiquetaPres = Etiqueta("Presentación (se reproduce sola al reconocer la tarjeta; después aparece la pieza)");
            etiquetaPres.Margin = new Padding(0, 0, 0, S(2));
            col.Controls.Add(etiquetaPres);
            col.Controls.Add(lblPres);
            col.Controls.Add(Fila(btnVideo, btnPdf, btnQuitar, lblSeg, numSeg));
            col.Controls.Add(Fila(btnGuardar, btnProbar, btnPublicada));
            col.Controls.Add(lblEstado);

            mostrar(tarjetas.First(x => x.Numero == selTarjeta));
        }

        // La ventana gana alto solo en este paso (al salir, AjustarTamano la devuelve a su tamaño)
        void CrecerAlto(int alto)
        {
            var area = Screen.FromControl(this).WorkingArea;
            alto = Math.Min(alto, area.Height - (Height - ClientSize.Height));
            if (ClientSize.Height >= alto) return;
            ClientSize = new Size(ClientSize.Width, alto);
            Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
        }

        // ── Textos ─────────────────────────────────────────────────

        static string PresentacionCorta(TarjetaAr t)
        {
            string tipo = t.PendTipo ?? t.PresTipo;
            string texto = tipo == "video" ? "Video propio" : tipo == "pdf" ? "PDF" + (t.PendTipo == null ? " · " + t.PresPaginas.Count + " pág." : "") : "Video general";
            return t.PendTipo != null ? texto + " (sin guardar)" : texto;
        }

        string PresentacionLarga(TarjetaAr t, int segundos)
        {
            if (t.PendTipo == "video") return "Video nuevo: " + Path.GetFileName(t.PendArchivo) + " (" + MB(new FileInfo(t.PendArchivo).Length) + ") · se copia al guardar.";
            if (t.PendTipo == "pdf") return "PDF nuevo: " + Path.GetFileName(t.PendArchivo) + " · se convierte en imágenes al guardar.";
            if (t.PendTipo == "") return "Al guardar se quita la presentación propia y queda el video general.";
            if (t.PresTipo == "video")
            {
                string ruta = Path.Combine(raiz, t.PresArchivo.Replace('/', '\\'));
                return "Video: " + (t.PresOrigen != "" ? t.PresOrigen : Path.GetFileName(ruta)) + (File.Exists(ruta) ? " (" + MB(new FileInfo(ruta).Length) + ")" : " (falta el archivo: vuelve a cargarlo)");
            }
            if (t.PresTipo == "pdf")
                return "PDF: " + (t.PresOrigen != "" ? t.PresOrigen : "presentación") + " · " + t.PresPaginas.Count + (t.PresPaginas.Count == 1 ? " página" : " páginas")
                    + " · dura " + (t.PresPaginas.Count * segundos) + " s en total.";
            return "Video general de Innercia (el mismo en todas las tarjetas que no tienen presentación propia). Carga un video o un PDF para esta tarjeta.";
        }

        // «001_ISLA_AXE» → «ISLA AXE»
        static string NombreLimpio(string nombre)
        {
            string limpio = Regex.Replace(nombre ?? "", @"^\d+[_\-\s]+", "").Replace('_', ' ').Trim();
            return limpio != "" ? limpio : nombre;
        }

        // Dirección visible propuesta: <campaña>/<pieza>, sin chocar con las páginas del visor ni con otra tarjeta
        string SugerirDireccion(Coleccion c, Modelo m, TarjetaAr esta, List<TarjetaAr> todas)
        {
            Func<string, string> soloValidos = s => Regex.Replace(s ?? "", @"[^A-Za-z0-9_-]+", "-").Trim('-');
            string campana = soloValidos(c.Clave), id = soloValidos(Regex.Replace(m.Id ?? "", @"^\d+-", ""));
            if (campana == "") campana = "tarjeta";
            if (id == "") id = "pieza";
            string pagina = Path.Combine(raiz, campana, id, "index.html");
            try { if (File.Exists(pagina) && !File.ReadAllText(pagina).Contains(MarcaPaginaTarjeta)) id += "-ar"; } catch { id += "-ar"; }
            string direccion = campana + "/" + id;
            if (todas.Any(o => o != esta && string.Equals(o.Direccion, direccion, StringComparison.OrdinalIgnoreCase))) direccion += "-" + esta.Numero;
            return direccion;
        }

        void CargarMiniTarjeta()
        {
            if (miniTarjeta != null) return;
            try
            {
                using (var original = Image.FromFile(Path.Combine(raiz, "pruebas", "tarjeta", "tarjeta.png")))
                {
                    int alto = S(104) * 2, ancho = alto * 45 / 80;
                    var mini = new Bitmap(ancho, alto);
                    using (var g = Graphics.FromImage(mini))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(original, 0, 0, ancho, alto);
                    }
                    miniTarjeta = mini;
                }
            }
            catch { /* sin imagen se dibuja un rectángulo azul */ }
        }

        // ── tarjetas.json ──────────────────────────────────────────

        static string Campo(Dictionary<string, object> d, string clave)
        {
            object v;
            return d != null && d.TryGetValue(clave, out v) && v != null ? Convert.ToString(v) : "";
        }

        // Una tarjeta por cada página fija t\<n>\ (las que llevan QR impreso), con lo que diga tarjetas.json
        List<TarjetaAr> LeerTarjetas(out string error)
        {
            error = null;
            var lista = new List<TarjetaAr>();
            Dictionary<string, object> json = null;
            string ruta = Path.Combine(raiz, "tarjetas.json");
            if (File.Exists(ruta))
            {
                try { json = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ruta, Encoding.UTF8)) as Dictionary<string, object>; }
                catch (Exception ex)
                {
                    error = "No se pudo leer tarjetas.json (revisa comas y comillas): " + ex.Message;
                    return lista;
                }
            }

            var numeros = new List<string>();
            string carpetaT = Path.Combine(raiz, "t");
            if (Directory.Exists(carpetaT))
                numeros.AddRange(Directory.GetDirectories(carpetaT).Select(Path.GetFileName)
                    .Where(n => Regex.IsMatch(n, @"^\d+$") && File.Exists(Path.Combine(carpetaT, n, "index.html"))));
            if (json != null) numeros.AddRange(json.Keys.Where(k => Regex.IsMatch(k, @"^\d+$")));

            foreach (string n in numeros.Distinct().OrderBy(x => long.Parse(x)))
            {
                var t = new TarjetaAr { Numero = n };
                object valor;
                var def = json != null && json.TryGetValue(n, out valor) ? valor as Dictionary<string, object> : null;
                if (def != null)
                {
                    t.Coleccion = Campo(def, "coleccion");
                    t.Pieza = Campo(def, "pieza");
                    t.Nombre = Campo(def, "nombre");
                    t.Direccion = Campo(def, "direccion");
                    var pres = def.TryGetValue("presentacion", out valor) ? valor as Dictionary<string, object> : null;
                    if (pres != null)
                    {
                        t.PresTipo = Campo(pres, "tipo");
                        t.PresArchivo = Campo(pres, "archivo");
                        t.PresVersion = Campo(pres, "version");
                        t.PresOrigen = Campo(pres, "origen");
                        int seg;
                        if (int.TryParse(Campo(pres, "segundos"), out seg) && seg > 0) t.PresSegundos = seg;
                        if (pres.TryGetValue("paginas", out valor) && valor is object[])
                            t.PresPaginas = ((object[])valor).Select(p => Convert.ToString(p)).ToList();
                        if (t.PresTipo != "video" && t.PresTipo != "pdf") t.PresTipo = "";
                    }
                }
                lista.Add(t);
            }
            return lista;
        }

        static string JsonTarjetas(List<TarjetaAr> tarjetas)
        {
            var sb = new StringBuilder("{\n  \"_nota\": ").Append(Colecciones.Json(NotaTarjetas));
            foreach (var t in tarjetas.Where(x => x.Coleccion != ""))
            {
                sb.Append(",\n  ").Append(Colecciones.Json(t.Numero)).Append(": { \"coleccion\": ").Append(Colecciones.Json(t.Coleccion))
                  .Append(", \"pieza\": ").Append(Colecciones.Json(t.Pieza))
                  .Append(", \"nombre\": ").Append(Colecciones.Json(t.Nombre))
                  .Append(", \"direccion\": ").Append(Colecciones.Json(t.Direccion));
                if (t.PresTipo == "video")
                    sb.Append(",\n         \"presentacion\": { \"tipo\": \"video\", \"archivo\": ").Append(Colecciones.Json(t.PresArchivo))
                      .Append(", \"version\": ").Append(Colecciones.Json(t.PresVersion)).Append(", \"origen\": ").Append(Colecciones.Json(t.PresOrigen)).Append(" }");
                else if (t.PresTipo == "pdf")
                    sb.Append(",\n         \"presentacion\": { \"tipo\": \"pdf\", \"segundos\": ").Append(t.PresSegundos)
                      .Append(", \"version\": ").Append(Colecciones.Json(t.PresVersion)).Append(", \"origen\": ").Append(Colecciones.Json(t.PresOrigen))
                      .Append(",\n           \"paginas\": [").Append(string.Join(", ", t.PresPaginas.Select(Colecciones.Json))).Append("] }");
                sb.Append(" }");
            }
            return sb.Append("\n}\n").ToString();
        }

        // Guarda la tarjeta en dos tiempos, para que un error nunca deje tarjetas.json apuntando a archivos que no están:
        //   1) la asignación (campaña, pieza, nombre, dirección) → se valida con generar-tarjetas.js; si falla, se deshace;
        //   2) la presentación nueva (copiar el video o convertir el PDF) → y se vuelve a validar.
        bool GuardarTarjeta(TarjetaAr t, List<TarjetaAr> todas, string coleccion, string pieza, string nombre, string direccion, int segundos, out string error)
        {
            string ruta = Path.Combine(raiz, "tarjetas.json");
            string anterior = File.Exists(ruta) ? File.ReadAllText(ruta, Encoding.UTF8) : null;
            var copia = new[] { t.Coleccion, t.Pieza, t.Nombre, t.Direccion };
            int segAntes = t.PresSegundos;

            t.Coleccion = coleccion; t.Pieza = pieza; t.Nombre = nombre; t.Direccion = direccion; t.PresSegundos = segundos;
            if (!EscribirYValidarTarjetas(todas, out error))
            {
                t.Coleccion = copia[0]; t.Pieza = copia[1]; t.Nombre = copia[2]; t.Direccion = copia[3]; t.PresSegundos = segAntes;
                try
                {
                    if (anterior != null) { File.WriteAllText(ruta, anterior, new UTF8Encoding(false)); string e2; ValidarTarjetas(out e2); }
                }
                catch { }
                return false;
            }
            if (t.PendTipo == null) return true;

            string carpeta = Path.Combine(raiz, "tarjetas", t.Numero), web = "tarjetas/" + t.Numero + "/";
            string version = DateTime.Now.ToString("yyyyMMddHHmmss");
            try
            {
                Directory.CreateDirectory(carpeta);
                string video = Path.Combine(carpeta, "video.mp4");
                if (t.PendTipo == "video")
                {
                    File.Copy(t.PendArchivo, video, true);
                    t.PresTipo = "video"; t.PresArchivo = web + "video.mp4"; t.PresPaginas = new List<string>();
                    t.PresOrigen = Path.GetFileName(t.PendArchivo); t.PresVersion = version;
                }
                else if (t.PendTipo == "pdf")
                {
                    int paginas;
                    if (!ConvertirPdf(t.PendArchivo, carpeta, out paginas, out error)) return false;      // la presentación anterior sigue como estaba
                    t.PresTipo = "pdf"; t.PresArchivo = "";
                    t.PresPaginas = Enumerable.Range(1, paginas).Select(i => web + "pagina-" + i.ToString("00") + ".jpg").ToList();
                    t.PresOrigen = Path.GetFileName(t.PendArchivo); t.PresVersion = version;
                }
                else
                {
                    t.PresTipo = ""; t.PresArchivo = ""; t.PresPaginas = new List<string>(); t.PresOrigen = ""; t.PresVersion = "";
                }
                t.PendTipo = null; t.PendArchivo = null;

                if (!EscribirYValidarTarjetas(todas, out error)) return false;

                // Lo que ya no usa esta tarjeta (el video anterior, o las páginas del PDF anterior)
                if (t.PresTipo != "video" && File.Exists(video)) File.Delete(video);
                if (t.PresTipo != "pdf") foreach (string f in Directory.GetFiles(carpeta, "pagina-*.jpg")) File.Delete(f);
                if (Directory.GetFileSystemEntries(carpeta).Length == 0) Directory.Delete(carpeta);
                return true;
            }
            catch (Exception ex)
            {
                error = "No se pudo guardar la presentación: " + ex.Message;
                return false;
            }
        }

        bool EscribirYValidarTarjetas(List<TarjetaAr> todas, out string error)
        {
            try { File.WriteAllText(Path.Combine(raiz, "tarjetas.json"), JsonTarjetas(todas), new UTF8Encoding(false)); }
            catch (Exception ex) { error = "No se pudo escribir tarjetas.json: " + ex.Message; return false; }
            return ValidarTarjetas(out error);
        }

        // scripts\generar-tarjetas.js: valida tarjetas.json y crea la página de cada dirección visible
        bool ValidarTarjetas(out string error)
        {
            string salida, errores;
            int codigo;
            if (!Ejecutar("node", Comillas2(Path.Combine(raiz, "scripts", "generar-tarjetas.js")), out codigo, out salida, out errores))
            {
                error = errores;
                return false;
            }
            if (codigo == 0) { error = null; return true; }
            var lineas = errores.Split('\n').Select(l => l.Trim().TrimStart('✗').Trim()).Where(l => l != "").ToList();
            error = lineas.Count > 0 ? string.Join("\n", lineas.Select(l => char.ToUpper(l[0]) + l.Substring(1))) : "No se pudo validar tarjetas.json.";
            return false;
        }

        bool ConvertirPdf(string pdf, string destino, out int paginas, out string error)
        {
            paginas = 0;
            string salida, errores;
            int codigo;
            string argumentos = "-NoProfile -ExecutionPolicy Bypass -File " + Comillas2(Path.Combine(raiz, "scripts", "pdf-a-imagenes.ps1"))
                + " -Pdf " + Comillas2(pdf) + " -Destino " + Comillas2(destino);
            if (!Ejecutar("powershell.exe", argumentos, out codigo, out salida, out errores)) { error = errores; return false; }
            var m = Regex.Match(salida, @"PAGINAS=(\d+)");
            if (codigo != 0 || !m.Success)
            {
                error = "No se pudo convertir el PDF: " + (errores.Trim() != "" ? errores.Trim() : "revisa que el archivo abra bien y no tenga contraseña.");
                return false;
            }
            paginas = int.Parse(m.Groups[1].Value);
            error = null;
            return true;
        }

        // Ejecuta un programa sin ventana y espera a que termine. false = no se pudo ni iniciar (el motivo queda en «errores»)
        bool Ejecutar(string programa, string argumentos, out int codigo, out string salida, out string errores)
        {
            codigo = -1; salida = ""; errores = "";
            try
            {
                var psi = new ProcessStartInfo(programa, argumentos)
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = raiz,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var leerErrores = p.StandardError.ReadToEndAsync();
                    salida = p.StandardOutput.ReadToEnd();
                    errores = leerErrores.Result;
                    p.WaitForExit();
                    codigo = p.ExitCode;
                }
                return true;
            }
            catch (Win32Exception)
            {
                errores = programa == "node"
                    ? "No se encontró Node.js, que se necesita para guardar las tarjetas. Instálalo desde https://nodejs.org (versión LTS)."
                    : "No se pudo ejecutar " + programa + ".";
                return false;
            }
            catch (Exception ex)
            {
                errores = "No se pudo ejecutar " + programa + ": " + ex.Message;
                return false;
            }
        }
    }
}

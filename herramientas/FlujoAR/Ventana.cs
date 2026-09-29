using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FlujoAR
{
    // Ventana del asistente: una columna de pasos a la izquierda y el contenido del paso a la derecha.
    //
    // Estructura de models\ (la carpeta manda):
    //   models\<Campaña>\<Pieza>\<Pieza>.glb
    // Cada carpeta raíz es una campaña. Con varias piezas, su QR es una colección; con una, muestra esa pieza.
    class Ventana : Form
    {
        const string UrlBase = "https://jhonatanaldana123.github.io/00_INTERACCION_VIRTUAL/";
        static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;]*[A-Za-z]");

        static readonly Color Oscuro = Color.FromArgb(16, 19, 26);
        static readonly Color OscuroActivo = Color.FromArgb(32, 37, 48);
        static readonly Color Acento = Color.FromArgb(0, 201, 176);
        static readonly Color TextoClaro = Color.FromArgb(220, 224, 234);
        static readonly Color TextoTenue = Color.FromArgb(130, 136, 150);
        static readonly Color Gris = Color.FromArgb(95, 100, 112);
        static readonly Color Rojo = Color.FromArgb(196, 43, 28);
        static readonly Color Naranja = Color.FromArgb(176, 96, 0);
        static readonly Color Verde = Color.FromArgb(16, 124, 80);

        static readonly string[] Titulos =
        {
            "Preparar en Rhino", "Exportar .glb", "Optimizar", "Revisar",
            "Campañas", "Publicar", "Generar QR",
        };

        readonly string raiz, carpetaModelos, rutaColecciones, carpetaQrs;
        readonly Servidor servidor = new Servidor();
        readonly float escala;
        readonly Panel contenido;
        readonly List<Button> botonesPaso = new List<Button>();
        readonly Button btnAnterior, btnSiguiente;
        int paso;

        // Estado que se conserva al cambiar de paso
        readonly bool[] checklist = new bool[7];
        string archivoEntrada = "", campanaEntrada = "", nombrePieza = "";
        string ultimaCampana, ultimaPieza;          // carpeta y nombre de la última pieza optimizada
        bool optimizando;
        readonly StringBuilder registro = new StringBuilder();
        string resultadoOptimizacion = "";
        Color colorResultado = Gris;
        TextBox registroVisible;
        Action refrescarOptimizar;
        List<Coleccion> campanas = new List<Coleccion>();
        List<string> avisosCampanas = new List<string>();
        string selCampana, selPieza;                // selección compartida por los pasos 4 a 7 (enlaces)

        public Ventana(string raiz)
        {
            this.raiz = raiz;
            carpetaModelos = Path.Combine(raiz, "models");
            rutaColecciones = Path.Combine(raiz, "colecciones.json");
            carpetaQrs = Path.Combine(raiz, "qrs");

            // TLS 1.2 para verificar la publicación en GitHub Pages
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;

            using (var g = CreateGraphics()) escala = g.DpiX / 96f;
            Font = new Font("Segoe UI", 10f);
            Text = "Flujo AR · De Rhino al QR";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(960), S(660));
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // Columna de pasos
            var nav = new Panel { Dock = DockStyle.Left, Width = S(236), BackColor = Oscuro, Padding = new Padding(S(14), S(22), S(14), S(14)) };
            var lista = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            lista.Controls.Add(new Label
            {
                Text = "FLUJO AR", AutoSize = true, ForeColor = TextoTenue,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(S(8), 0, 0, S(18)),
            });
            for (int i = 0; i < Titulos.Length; i++)
            {
                var b = new Button
                {
                    Text = (i + 1) + "    " + Titulos[i], Tag = i, Width = S(208), Height = S(42),
                    FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand,
                    BackColor = Oscuro, ForeColor = TextoClaro, Margin = new Padding(0, 0, 0, S(4)),
                    Padding = new Padding(S(6), 0, 0, 0), UseVisualStyleBackColor = false,
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = OscuroActivo;
                b.Click += (s, e) => IrA((int)((Button)s).Tag);
                lista.Controls.Add(b);
                botonesPaso.Add(b);
            }
            nav.Controls.Add(lista);

            // Barra inferior con Anterior / Siguiente
            var pie = new Panel { Dock = DockStyle.Bottom, Height = S(62), BackColor = Color.FromArgb(245, 246, 248), Padding = new Padding(S(24), S(13), S(24), S(10)) };
            var botones = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            btnSiguiente = Boton("Siguiente  →", true);
            btnAnterior = Boton("←  Anterior", false);
            btnSiguiente.Click += (s, e) => IrA(paso + 1);
            btnAnterior.Click += (s, e) => IrA(paso - 1);
            botones.Controls.Add(btnSiguiente);
            botones.Controls.Add(btnAnterior);
            pie.Controls.Add(botones);

            contenido = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            Controls.Add(contenido);
            Controls.Add(pie);
            Controls.Add(nav);

            FormClosing += (s, e) => servidor.Detener();
            IrA(0);
        }

        // ── Navegación ─────────────────────────────────────────────
        void IrA(int i)
        {
            if (i < 0 || i >= Titulos.Length) return;
            paso = i;
            registroVisible = null;
            refrescarOptimizar = null;

            contenido.SuspendLayout();
            foreach (var c in contenido.Controls.Cast<Control>().ToList()) c.Dispose();
            contenido.AutoScrollPosition = Point.Empty;

            var col = Columna();
            col.Controls.Add(Titulo((i + 1) + ". " + Titulos[i]));
            switch (i)
            {
                case 0: PasoRhino(col); break;
                case 1: PasoExportar(col); break;
                case 2: PasoOptimizar(col); break;
                case 3: PasoRevisar(col); break;
                case 4: PasoCampanas(col); break;
                case 5: PasoPublicar(col); break;
                case 6: PasoQr(col); break;
            }
            // El Padding del panel no se aplica a hijos sin Dock: el margen va en la posición
            col.Location = new Point(S(34), S(26));
            contenido.Controls.Add(col);
            contenido.ResumeLayout();

            for (int k = 0; k < botonesPaso.Count; k++)
            {
                botonesPaso[k].BackColor = k == i ? OscuroActivo : Oscuro;
                botonesPaso[k].ForeColor = k == i ? Acento : TextoClaro;
                botonesPaso[k].Font = new Font(Font, k == i ? FontStyle.Bold : FontStyle.Regular);
            }
            btnAnterior.Visible = i > 0;
            btnSiguiente.Visible = i < Titulos.Length - 1;
        }

        // ── Paso 1: Rhino ──────────────────────────────────────────
        void PasoRhino(FlowLayoutPanel col)
        {
            col.Controls.Add(Texto("Esto es lo que más reduce el peso. Revisa cada punto en Rhino antes de exportar (las casillas son solo una ayuda para no olvidar nada)."));
            string[] items =
            {
                "Piezas repetidas (botellas, tapas) con malla liviana: 1.000 a 1.500 triángulos",
                "Piezas repetidas convertidas en Bloque",
                "Borradas las caras y piezas que no se ven",
                "Materiales PBR simples: sin vidrio, barniz ni transparencia",
                "Purge ejecutado (sin materiales, bloques ni capas sin usar)",
                "Escala real y unidades correctas",
                "Base apoyada en Z = 0 y centrada en el origen",
            };
            for (int k = 0; k < items.Length; k++)
            {
                int idx = k;
                var cb = new CheckBox { Text = items[k], AutoSize = true, Checked = checklist[k], Margin = new Padding(0, 0, 0, S(8)) };
                cb.CheckedChanged += (s, e) => checklist[idx] = ((CheckBox)s).Checked;
                col.Controls.Add(cb);
            }
            var meta = Texto("Meta: menos de 100.000 triángulos en total y menos de 5 MB después de optimizar.");
            meta.Margin = new Padding(0, S(10), 0, S(12));
            col.Controls.Add(meta);

            col.Controls.Add(Texto("Para bajar la malla de una pieza: comando Mesh → Opciones detalladas → ángulo máximo 15–20°, longitud mínima de arista 1–2 mm, sin «Refinar malla». Si ya es malla, usa ReduceMesh."));
            var guia = Boton("Abrir guía completa", false);
            guia.Click += (s, e) => Abrir(Path.Combine(raiz, "docs", "OPTIMIZAR_MODELOS.md"));
            col.Controls.Add(Fila(guia));
        }

        // ── Paso 2: Exportar ───────────────────────────────────────
        void PasoExportar(FlowLayoutPanel col)
        {
            col.Controls.Add(Texto("Cada pieza se exporta por separado (por ejemplo, el exhibidor y la bandeja son dos archivos). En Rhino:"));
            col.Controls.Add(Texto("1.   Selecciona solo la pieza."));
            col.Controls.Add(Texto("2.   Archivo → Exportar selección → tipo glTF Binary (.glb)."));
            col.Controls.Add(Texto("3.   Ponle al archivo el nombre de la pieza. Ejemplos: Exhibidor01.glb, Bandeja01.glb"));
            col.Controls.Add(Texto("4.   Guárdalo en cualquier carpeta (por ejemplo, Descargas). En el siguiente paso eliges el archivo y la campaña a la que pertenece."));
            var nota = Texto("No hace falta exportar .usdz: en iPhone el visor lo genera automáticamente a partir del .glb.");
            nota.ForeColor = Gris;
            nota.Margin = new Padding(0, S(8), 0, S(12));
            col.Controls.Add(nota);
        }

        // ── Paso 3: Optimizar ──────────────────────────────────────
        void PasoOptimizar(FlowLayoutPanel col)
        {
            col.Controls.Add(Texto("Elige la pieza exportada y la campaña a la que pertenece. La pieza queda en models\\Campaña\\Pieza\\, con una copia del original de Rhino en _original\\."));

            var txtArchivo = new TextBox { Width = S(470), ReadOnly = true, Text = archivoEntrada, Margin = new Padding(0, S(2), S(8), 0) };
            var btnExaminar = Boton("Examinar…", false);
            var cmbCampana = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = S(300), Margin = new Padding(0, S(2), S(8), S(6)) };
            foreach (var c in CarpetasCampana()) cmbCampana.Items.Add(c);
            cmbCampana.Text = campanaEntrada;
            var txtPieza = new TextBox { Width = S(300), Text = nombrePieza, Margin = new Padding(0, S(2), S(8), 0) };
            var lblValidacion = Estado();
            var btnOptimizar = Boton("Optimizar", true);
            var txtRegistro = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Width = S(640), Height = S(150), Font = new Font("Consolas", 9f),
                BackColor = Color.FromArgb(246, 247, 249), Text = registro.ToString(), Margin = new Padding(0, S(6), 0, S(8)),
            };
            var lblResultado = Estado();
            lblResultado.Font = new Font(Font, FontStyle.Bold);

            Action refrescar = () =>
            {
                // Lo que se valida es exactamente lo que se envía al optimizar
                string campana = campanaEntrada = cmbCampana.Text.Trim().TrimEnd('.');
                string pieza = nombrePieza = txtPieza.Text.Trim().TrimEnd('.');
                string error = null;
                if (archivoEntrada == "") error = "Elige el archivo .glb exportado desde Rhino.";
                else if (!File.Exists(archivoEntrada)) error = "El archivo ya no existe en esa ubicación.";
                else error = ValidarNombre(campana, "campaña") ?? ValidarNombre(pieza, "pieza");

                if (error != null)
                {
                    lblValidacion.ForeColor = Rojo;
                    lblValidacion.Text = error;
                }
                else
                {
                    bool campanaNueva = !Directory.Exists(Path.Combine(carpetaModelos, campana));
                    bool existe = File.Exists(RutaPieza(campana, pieza));
                    int total = ContarPiezas(campana) + (existe ? 0 : 1);
                    lblValidacion.ForeColor = Gris;
                    lblValidacion.Text =
                        (existe ? "Ya existe esta pieza: se reemplazará por la versión nueva (el QR no cambia)."
                                : "Se guardará en models\\" + campana + "\\" + pieza + "\\")
                        + "\n" + (campanaNueva ? "Campaña nueva «" + campana + "»" : "La campaña «" + campana + "»")
                        + " quedará con " + total + (total == 1 ? " pieza → su QR muestra esa pieza (si agregas más, el mismo QR las mostrará)."
                                                              : " piezas → su QR es una colección: el cliente desliza entre piezas.");
                }
                btnOptimizar.Enabled = error == null && !optimizando;
                btnOptimizar.Text = optimizando ? "Optimizando…" : "Optimizar";
                lblResultado.Text = resultadoOptimizacion;
                lblResultado.ForeColor = colorResultado;
            };

            btnExaminar.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Filter = "Modelo glTF binario (*.glb)|*.glb", Title = "Elige el .glb exportado desde Rhino" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    archivoEntrada = dlg.FileName;
                    txtArchivo.Text = archivoEntrada;
                    txtPieza.Text = Path.GetFileNameWithoutExtension(archivoEntrada);
                    resultadoOptimizacion = "";
                    refrescar();
                }
            };
            cmbCampana.TextChanged += (s, e) => refrescar();
            cmbCampana.SelectedIndexChanged += (s, e) => refrescar();
            txtPieza.TextChanged += (s, e) => refrescar();
            btnOptimizar.Click += (s, e) =>
            {
                refrescar();
                if (btnOptimizar.Enabled) Optimizar();
            };

            col.Controls.Add(Etiqueta("Pieza exportada desde Rhino (.glb)"));
            col.Controls.Add(Fila(txtArchivo, btnExaminar));
            col.Controls.Add(Etiqueta("Campaña (elige una de la lista o escribe una nueva)"));
            col.Controls.Add(Fila(cmbCampana));
            col.Controls.Add(Etiqueta("Nombre de la pieza"));
            col.Controls.Add(Fila(txtPieza));
            col.Controls.Add(lblValidacion);
            col.Controls.Add(Fila(btnOptimizar));
            col.Controls.Add(txtRegistro);
            col.Controls.Add(lblResultado);

            registroVisible = txtRegistro;
            refrescarOptimizar = refrescar;
            refrescar();
        }

        void Optimizar()
        {
            optimizando = true;
            lock (registro) registro.Clear();
            if (registroVisible != null) registroVisible.Clear();
            resultadoOptimizacion = "";
            if (refrescarOptimizar != null) refrescarOptimizar();

            string campana = campanaEntrada, pieza = nombrePieza, entrada = archivoEntrada;
            string script = Path.Combine(raiz, "scripts", "optimizar-modelo.ps1");
            // try/catch para mostrar solo el mensaje del error, sin el detalle técnico de PowerShell
            string comando = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; try { & " + Comillas(script)
                + " -Entrada " + Comillas(entrada) + " -Campana " + Comillas(campana) + " -Nombre " + Comillas(pieza) + " -SinPreguntas"
                + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + comando.Replace("\"", "\\\"") + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = raiz,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };

            AgregarRegistro("Optimizando " + Path.GetFileName(entrada) + "…");
            AgregarRegistro("(la primera vez descarga gltf-transform y puede tardar un poco)");
            AgregarRegistro("");

            new Thread(() =>
            {
                int codigo = -1;
                string error = null;
                try
                {
                    using (var p = new Process { StartInfo = psi })
                    {
                        p.OutputDataReceived += (s, e) => { if (e.Data != null) AgregarRegistro(Ansi.Replace(e.Data, "")); };
                        p.ErrorDataReceived += (s, e) =>
                        {
                            if (e.Data == null) return;
                            string linea = Ansi.Replace(e.Data, "");
                            AgregarRegistro(linea);
                            // gltf-transform escribe su progreso (info:) y npm sus avisos por el canal de error
                            if (linea.Trim() != "" && !linea.StartsWith("info:") && !linea.StartsWith("npm")) error = linea;
                        };
                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        p.WaitForExit();
                        codigo = p.ExitCode;
                    }
                }
                catch (Exception ex) { error = "No se pudo ejecutar PowerShell: " + ex.Message; }
                EnVentana(() => TerminarOptimizacion(codigo, campana, pieza, error));
            }) { IsBackground = true }.Start();
        }

        void TerminarOptimizacion(int codigo, string campana, string pieza, string error)
        {
            optimizando = false;
            string salida = RutaPieza(campana, pieza);
            if (codigo == 0 && File.Exists(salida))
            {
                ultimaCampana = campana;
                ultimaPieza = pieza;
                selCampana = null;   // el paso 4 se abre en la campaña recién optimizada
                selPieza = null;
                int total = ContarPiezas(campana);
                resultadoOptimizacion = "Listo: " + pieza + " (" + MB(new FileInfo(salida).Length) + ") guardada en la campaña «" + campana + "», que ahora tiene "
                    + total + (total == 1 ? " pieza." : " piezas.") + " Siguiente paso: revisarla.";
                colorResultado = Verde;

                // Los avisos del optimizador (material sin asignar, malla pesada…) se repiten aquí para que no pasen desapercibidos
                string texto;
                lock (registro) texto = registro.ToString();
                var avisos = texto.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(l => l.StartsWith("AVISO:")).Select(l => "⚠ " + l.Substring(6).Trim()).Distinct().ToList();
                if (avisos.Count > 0)
                {
                    resultadoOptimizacion += "\n\n" + string.Join("\n", avisos);
                    colorResultado = Naranja;
                }
            }
            else
            {
                resultadoOptimizacion = "No se pudo optimizar: " + (error ?? "revisa el registro de arriba.");
                colorResultado = Rojo;
            }
            if (refrescarOptimizar != null) refrescarOptimizar();
        }

        void AgregarRegistro(string linea)
        {
            lock (registro) registro.Append(linea).Append("\r\n");
            EnVentana(() =>
            {
                if (registroVisible != null && !registroVisible.IsDisposed) registroVisible.AppendText(linea + "\r\n");
            });
        }

        // ── Paso 4: Revisar ────────────────────────────────────────
        void PasoRevisar(FlowLayoutPanel col)
        {
            if (!CargarCampanas(col)) return;
            col.Controls.Add(Texto("Abre la campaña o una pieza en el visor, en tu navegador, antes de publicar. Revisa:"));
            col.Controls.Add(Texto("•   Texturas: que se vean nítidas y en su lugar.\n•   Escala: que las proporciones sean las reales.\n•   Orientación: que el frente mire hacia la cámara al abrir."));

            // Luz de la escena: se prueba en la vista previa y se guarda por campaña
            var barraLuz = new TrackBar { Minimum = 40, Maximum = 150, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = S(300), Margin = new Padding(0, 0, S(8), 0) };
            var lblLuz = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(0, S(6), S(12), 0) };
            var btnGuardarLuz = Boton("Guardar luz para esta campaña", false);
            var lblEstadoLuz = Estado();
            Func<Coleccion> campanaActual = () => campanas.FirstOrDefault(c => c.Clave == selCampana);
            Func<double> luz = () => barraLuz.Value / 100.0;
            Action mostrarLuz = () =>
            {
                var c = campanaActual();
                bool guardada = c != null && c.Exposicion.HasValue && Math.Abs(c.Exposicion.Value - luz()) < 0.001;
                lblLuz.Text = luz().ToString("0.00") + (guardada ? "  (guardada)" : Math.Abs(luz() - ExposicionPorDefecto) < 0.001 ? "  (por defecto)" : "  (sin guardar)");
            };
            Action cargarLuz = () =>
            {
                var c = campanaActual();
                double v = c != null && c.Exposicion.HasValue ? c.Exposicion.Value : ExposicionPorDefecto;
                barraLuz.Value = Math.Max(barraLuz.Minimum, Math.Min(barraLuz.Maximum, (int)Math.Round(v * 100)));
                lblEstadoLuz.Text = "";
                mostrarLuz();
            };
            barraLuz.ValueChanged += (s, e) => mostrarLuz();

            ComboBox cmbCampana, cmbPieza;
            AgregarSelectores(col, true, cargarLuz, out cmbCampana, out cmbPieza);

            var abrir = Boton("Abrir en el navegador", true);
            abrir.Click += (s, e) =>
            {
                var c = CampanaElegida(cmbCampana);
                if (c != null) AbrirLocal(Consulta(c, PiezaElegida(c, cmbPieza)) + "&exposicion=" + luz().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            };
            btnGuardarLuz.Click += (s, e) =>
            {
                var c = campanaActual();
                if (c == null) return;
                string error;
                if (!GuardarConfig(c, c.Modelos.Select(m => m.Nombre).ToList(), luz(), out error) || !Escanear(out error))
                {
                    lblEstadoLuz.ForeColor = Rojo;
                    lblEstadoLuz.Text = error;
                    return;
                }
                lblEstadoLuz.ForeColor = Verde;
                lblEstadoLuz.Text = "Luz guardada para «" + c.Titulo + "». Se verá así en el QR después de publicar (paso 6).";
                mostrarLuz();
            };

            col.Controls.Add(Etiqueta("Luz de la escena (a la izquierda, más oscuro)"));
            col.Controls.Add(Fila(barraLuz, lblLuz));
            col.Controls.Add(Fila(abrir, btnGuardarLuz));
            col.Controls.Add(lblEstadoLuz);
            var nota = Texto("Mueve la luz y pulsa «Abrir en el navegador» para ver el cambio. Cuando te guste, guárdala: queda para toda la campaña. La vista previa funciona antes de publicar; para probar el AR hay que publicar (paso 6) y escanear el QR.");
            nota.ForeColor = Gris;
            col.Controls.Add(nota);
            cargarLuz();
        }

        // Debe coincidir con exposure="…" de model-viewer en index.html
        const double ExposicionPorDefecto = 0.8;

        // ── Paso 5: Campañas ───────────────────────────────────────
        void PasoCampanas(FlowLayoutPanel col)
        {
            col.Controls.Add(Texto("Las campañas salen de las carpetas: cada carpeta dentro de models\\ es una campaña, y cada subcarpeta con su .glb es una pieza. Aquí ves lo que se detectó y el orden en que el cliente verá las piezas."));
            if (!CargarCampanas(col)) return;

            var cmbCampana = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(300), Margin = new Padding(0, S(2), S(8), S(6)) };
            foreach (var c in campanas) cmbCampana.Items.Add(c.Titulo);
            var btnCarpeta = Boton("Abrir carpeta", false);
            var lblInfo = Estado();
            var lv = new ListView
            {
                View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true,
                Width = S(640), Height = S(130), Margin = new Padding(0, 0, 0, S(8)),
            };
            lv.Columns.Add("#", S(40));
            lv.Columns.Add("Pieza", S(300));
            lv.Columns.Add("Peso", S(100));
            lv.Columns.Add("Enlace de la pieza", S(170));
            var btnSubir = Boton("↑ Subir", false);
            var btnBajar = Boton("↓ Bajar", false);
            var btnVer = Boton("Ver en el navegador", false);
            var btnActualizar = Boton("Volver a leer carpetas", false);
            var lblEstado = Estado();

            Func<Coleccion> actual = () => CampanaElegida(cmbCampana);
            Func<int> seleccion = () => lv.SelectedIndices.Count > 0 ? lv.SelectedIndices[0] : -1;

            Action<int> rellenar = sel =>
            {
                var c = actual();
                lv.Items.Clear();
                if (c == null) return;
                selCampana = c.Clave;
                int n = c.Modelos.Count;
                lblInfo.Text = "Enlace de la campaña: " + c.Clave + "   ·   " + n + (n == 1
                    ? " pieza → el QR muestra esa pieza (si agregas más, el mismo QR las mostrará)."
                    : " piezas → el QR es una colección: el cliente desliza entre piezas.");
                for (int k = 0; k < n; k++)
                {
                    var m = c.Modelos[k];
                    string ruta = RutaLocal(m);
                    lv.Items.Add(new ListViewItem(new[] { (k + 1).ToString(), m.Nombre, File.Exists(ruta) ? MB(new FileInfo(ruta).Length) : "—", m.Id }));
                }
                if (sel >= 0 && sel < lv.Items.Count) { lv.Items[sel].Selected = true; lv.EnsureVisible(sel); }
            };

            Action<int> mover = delta =>
            {
                var c = actual();
                int i = seleccion(), j = i + delta;
                if (c == null || i < 0) { lblEstado.ForeColor = Rojo; lblEstado.Text = "Selecciona una pieza de la lista."; return; }
                if (j < 0 || j >= c.Modelos.Count) return;
                var orden = c.Modelos.Select(m => m.Nombre).ToList();
                var tmp = orden[i]; orden[i] = orden[j]; orden[j] = tmp;
                string error;
                if (!GuardarConfig(c, orden, c.Exposicion, out error) || !Escanear(out error))
                {
                    lblEstado.ForeColor = Rojo;
                    lblEstado.Text = error;
                    return;
                }
                lblEstado.ForeColor = Verde;
                lblEstado.Text = "Orden guardado · " + DateTime.Now.ToString("HH:mm:ss");
                rellenar(j);
            };

            cmbCampana.SelectedIndexChanged += (s, e) => rellenar(-1);
            btnSubir.Click += (s, e) => mover(-1);
            btnBajar.Click += (s, e) => mover(+1);
            btnVer.Click += (s, e) => { var c = actual(); if (c != null) AbrirLocal(Consulta(c, null)); };
            btnCarpeta.Click += (s, e) =>
            {
                var c = actual();
                Abrir(c != null ? Path.Combine(carpetaModelos, c.Carpeta) : carpetaModelos);
            };
            btnActualizar.Click += (s, e) => IrA(paso);

            col.Controls.Add(Etiqueta("Campaña"));
            col.Controls.Add(Fila(cmbCampana, btnCarpeta));
            col.Controls.Add(lblInfo);
            col.Controls.Add(Etiqueta("Piezas, en el orden en que se ven"));
            col.Controls.Add(lv);
            col.Controls.Add(Fila(btnSubir, btnBajar, btnVer, btnActualizar));
            col.Controls.Add(lblEstado);
            AgregarAvisos(col);

            int inicial = IndiceCampanaPreferida();
            if (inicial >= 0) cmbCampana.SelectedIndex = inicial;
        }

        // Reescribe models\<Campaña>\_campana.json (enlace fijo, orden de piezas y luz)
        bool GuardarConfig(Coleccion c, List<string> orden, double? exposicion, out string error)
        {
            error = null;
            try
            {
                var sb = new StringBuilder("{\n  \"enlace\": ").Append(Colecciones.Json(c.Clave)).Append(",\n  \"orden\": [\n");
                for (int i = 0; i < orden.Count; i++)
                    sb.Append("    ").Append(Colecciones.Json(orden[i])).Append(i < orden.Count - 1 ? ",\n" : "\n");
                sb.Append("  ]");
                if (exposicion.HasValue)
                    sb.Append(",\n  \"exposicion\": ").Append(exposicion.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append("\n}\n");
                File.WriteAllText(Path.Combine(carpetaModelos, c.Carpeta, "_campana.json"), sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                error = "No se pudo guardar la configuración de la campaña: " + ex.Message;
                return false;
            }
        }

        // ── Paso 6: Publicar ───────────────────────────────────────
        void PasoPublicar(FlowLayoutPanel col)
        {
            col.Controls.Add(Texto("GitHub Pages solo publica lo que subes al repositorio. En GitHub Desktop:"));
            col.Controls.Add(Texto("1.   Ábrelo con el botón de abajo.\n2.   Verás los cambios: las carpetas de las piezas nuevas (models\\…) y colecciones.json.\n3.   Escribe un mensaje abajo a la izquierda, por ejemplo: Campaña Alpina.\n4.   Haz clic en Commit to main y luego en Push origin.\n5.   Espera 1–2 minutos y pulsa «Verificar publicación»."));

            var btnDesktop = Boton("Abrir GitHub Desktop", true);
            btnDesktop.Click += (s, e) => AbrirGitHubDesktop();
            col.Controls.Add(Fila(btnDesktop));

            if (!CargarCampanas(col)) return;
            var titulo = Etiqueta("Comprobar que ya está en línea");
            titulo.Margin = new Padding(0, S(14), 0, S(4));
            col.Controls.Add(titulo);

            ComboBox cmbCampana, cmbPieza;
            AgregarSelectores(col, false, null, out cmbCampana, out cmbPieza);
            var btnVerificar = Boton("Verificar publicación", false);
            var lblResultado = Estado();
            btnVerificar.Click += (s, e) =>
            {
                var c = CampanaElegida(cmbCampana);
                if (c != null) Verificar(c, btnVerificar, lblResultado);
            };
            col.Controls.Add(Fila(btnVerificar));
            col.Controls.Add(lblResultado);
        }

        void Verificar(Coleccion c, Button boton, Label resultado)
        {
            boton.Enabled = false;
            resultado.ForeColor = Gris;
            resultado.Text = "Verificando en " + UrlBase + " …";
            var piezas = c.Modelos.Select(m => new { m.Nombre, Local = RutaLocal(m), Web = UrlArchivo(m) }).ToList();

            new Thread(() =>
            {
                var lineas = new List<string>();
                bool todoBien = true;
                long remoto;
                string texto;

                foreach (var p in piezas)
                {
                    long local = File.Exists(p.Local) ? new FileInfo(p.Local).Length : -1;
                    int codigo = Consultar(UrlBase + p.Web, "HEAD", out remoto, out texto);
                    if (codigo == 200 && remoto == local) lineas.Add("✓  " + p.Nombre + " está publicada (" + MB(local) + ").");
                    else if (codigo == 200)
                    {
                        todoBien = false;
                        lineas.Add("⚠  " + p.Nombre + " está publicada, pero es otra versión. ¿Hiciste Push? Si ya lo hiciste, espera unos minutos.");
                    }
                    else
                    {
                        todoBien = false;
                        lineas.Add(codigo == 404
                            ? "✗  " + p.Nombre + " todavía no está publicada. Haz Commit y Push, y espera 1–2 minutos."
                            : "✗  No se pudo consultar GitHub Pages (" + (codigo < 0 ? "sin conexión" : "código " + codigo) + ").");
                    }
                }

                long ignorar;
                int cod = Consultar(UrlBase + "colecciones.json", "GET", out ignorar, out texto);
                string localJson = File.Exists(rutaColecciones) ? File.ReadAllText(rutaColecciones, Encoding.UTF8) : "";
                if (cod == 200 && Normalizar(texto) == Normalizar(localJson)) lineas.Add("✓  La lista de campañas (colecciones.json) está al día.");
                else
                {
                    todoBien = false;
                    lineas.Add("⚠  La lista de campañas (colecciones.json) en línea es distinta a la de tu computador. Haz Commit y Push, y espera 1–2 minutos.");
                }

                if (todoBien) lineas.Add("\nTodo listo: ya puedes generar el QR (paso 7).");
                EnVentana(() =>
                {
                    if (boton.IsDisposed) return;
                    boton.Enabled = true;
                    resultado.ForeColor = todoBien ? Verde : Rojo;
                    resultado.Text = string.Join("\n", lineas);
                });
            }) { IsBackground = true }.Start();
        }

        // Devuelve el código HTTP (-1 sin conexión). El parámetro ?t= evita respuestas en caché.
        static int Consultar(string url, string metodo, out long largo, out string texto)
        {
            largo = -1;
            texto = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url + "?t=" + DateTime.UtcNow.Ticks);
                req.Method = metodo;
                req.Timeout = 15000;
                req.Headers.Add("Cache-Control", "no-cache");
                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    largo = res.ContentLength;
                    if (metodo == "GET")
                        using (var r = new StreamReader(res.GetResponseStream(), Encoding.UTF8)) texto = r.ReadToEnd();
                    return (int)res.StatusCode;
                }
            }
            catch (WebException ex)
            {
                var res = ex.Response as HttpWebResponse;
                return res != null ? (int)res.StatusCode : -1;
            }
            catch { return -1; }
        }

        static string Normalizar(string s)
        {
            return (s ?? "").Replace("\r\n", "\n").Trim();
        }

        void AbrirGitHubDesktop()
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitHubDesktop", "GitHubDesktop.exe");
            try
            {
                if (File.Exists(exe)) Process.Start(exe);
                else Process.Start("x-github-client://");
            }
            catch
            {
                MessageBox.Show(this, "No encontré GitHub Desktop. Ábrelo desde el menú Inicio.", "Flujo AR", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ── Paso 7: QR ─────────────────────────────────────────────
        void PasoQr(FlowLayoutPanel col)
        {
            col.Controls.Add(Texto("Elige la campaña. Con varias piezas, el QR de la campaña completa deja deslizar entre ellas; también puedes sacar el QR de una sola pieza. El QR funciona cuando ya está publicado (paso 6)."));
            if (!CargarCampanas(col)) return;

            var enlace = new LinkLabel { AutoSize = true, MaximumSize = new Size(S(640), 0), Margin = new Padding(0, S(2), 0, S(8)), LinkBehavior = LinkBehavior.HoverUnderline, UseMnemonic = false };
            var imagen = new PictureBox { Width = S(200), Height = S(200), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, S(10)) };
            var btnPng = Boton("Guardar PNG (impresión)", true);
            var btnSvg = Boton("Guardar SVG (PDF, Illustrator)", false);
            var btnCarpeta = Boton("Abrir carpeta de QRs", false);
            var lblEstado = Estado();

            bool[,] matriz = null;
            string url = null, nombreArchivo = null;
            ComboBox cmbCampana = null, cmbPieza = null;

            Action generar = () =>
            {
                var c = CampanaElegida(cmbCampana);
                if (c == null) return;
                var m = PiezaElegida(c, cmbPieza);
                url = UrlBase + Consulta(c, m);
                nombreArchivo = "QR_" + c.Clave + (m != null ? "_" + m.Id : "");
                matriz = Qr.Codificar(url);
                int ppm = Math.Max(1, (imagen.Width - 4) / (matriz.GetLength(0) + 8));
                var anterior = imagen.Image;
                imagen.Image = Qr.ABitmap(matriz, ppm);
                if (anterior != null) anterior.Dispose();
                enlace.Text = url;
                lblEstado.Text = "";
            };

            AgregarSelectores(col, true, generar, out cmbCampana, out cmbPieza);
            enlace.LinkClicked += (s, e) => { if (url != null) Abrir(url); };
            btnPng.Click += (s, e) => GuardarQr(matriz, nombreArchivo + ".png", lblEstado, true);
            btnSvg.Click += (s, e) => GuardarQr(matriz, nombreArchivo + ".svg", lblEstado, false);
            btnCarpeta.Click += (s, e) => { Directory.CreateDirectory(carpetaQrs); Abrir(carpetaQrs); };

            col.Controls.Add(enlace);
            col.Controls.Add(imagen);
            col.Controls.Add(Fila(btnPng, btnSvg, btnCarpeta));
            col.Controls.Add(lblEstado);
            generar();
        }

        void GuardarQr(bool[,] matriz, string archivo, Label estado, bool png)
        {
            if (matriz == null) return;
            try
            {
                Directory.CreateDirectory(carpetaQrs);
                string ruta = Path.Combine(carpetaQrs, archivo);
                if (png) Qr.GuardarPng(matriz, ruta);
                else Qr.GuardarSvg(matriz, ruta);
                estado.ForeColor = Verde;
                estado.Text = "Guardado: qrs\\" + archivo;
            }
            catch (Exception ex)
            {
                estado.ForeColor = Rojo;
                estado.Text = "No se pudo guardar: " + ex.Message;
            }
        }

        // ── Campañas: lectura de carpetas y selección ──────────────

        // Relee las carpetas (scripts\generar-colecciones.js actualiza colecciones.json) y muestra
        // un mensaje en el paso si algo falla o si todavía no hay campañas.
        bool CargarCampanas(FlowLayoutPanel col)
        {
            string error;
            bool ok = Escanear(out error);
            if (ok && campanas.Count > 0) return true;

            var l = Estado();
            l.ForeColor = ok ? Gris : Rojo;
            l.Text = ok ? "Todavía no hay campañas: optimiza la primera pieza en el paso 3 y elige o escribe el nombre de su campaña." : error;
            col.Controls.Add(l);
            if (ok) AgregarAvisos(col);
            return false;
        }

        bool Escanear(out string error)
        {
            error = null;
            avisosCampanas = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("node", Comillas2(Path.Combine(raiz, "scripts", "generar-colecciones.js")))
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = raiz,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    string salida = p.StandardOutput.ReadToEnd();
                    string errores = p.StandardError.ReadToEnd();
                    p.WaitForExit(20000);
                    if (p.ExitCode != 0)
                    {
                        error = "No se pudieron leer las carpetas de campañas: " + errores.Trim();
                        return false;
                    }
                    var resumen = new JavaScriptSerializer().DeserializeObject(salida) as Dictionary<string, object>;
                    object avisos;
                    if (resumen != null && resumen.TryGetValue("avisos", out avisos) && avisos is object[])
                        avisosCampanas = ((object[])avisos).Select(a => Convert.ToString(a)).ToList();
                }
            }
            catch (Win32Exception)
            {
                error = "No se encontró Node.js, que se necesita para leer las campañas. Instálalo desde https://nodejs.org (versión LTS).";
                return false;
            }
            catch (Exception ex)
            {
                error = "No se pudieron leer las carpetas de campañas: " + ex.Message;
                return false;
            }

            try
            {
                campanas = Colecciones.Cargar(rutaColecciones);
                return true;
            }
            catch (Exception ex)
            {
                error = "No se pudo leer colecciones.json: " + ex.Message;
                return false;
            }
        }

        void AgregarAvisos(FlowLayoutPanel col)
        {
            if (avisosCampanas.Count == 0) return;
            var l = Estado();
            l.ForeColor = Naranja;
            l.Text = "Revisa en las carpetas:\n" + string.Join("\n", avisosCampanas.Select(a => "•  " + a));
            col.Controls.Add(l);
        }

        // Dos listas: campaña y (opcional) pieza. La elección se recuerda entre pasos.
        void AgregarSelectores(FlowLayoutPanel col, bool conPiezas, Action alCambiar, out ComboBox cmbCampana, out ComboBox cmbPieza)
        {
            var cCampana = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(300), Margin = new Padding(0, S(2), S(8), S(6)) };
            var cPieza = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(300), Margin = new Padding(0, S(2), S(8), S(6)) };
            foreach (var c in campanas) cCampana.Items.Add(c.Titulo + "   (" + c.Modelos.Count + (c.Modelos.Count == 1 ? " pieza)" : " piezas)"));

            Action llenarPiezas = () =>
            {
                cPieza.Items.Clear();
                var c = CampanaElegida(cCampana);
                if (c == null) return;
                cPieza.Items.Add(c.Modelos.Count == 1 ? "Campaña completa (1 pieza)" : "Campaña completa (" + c.Modelos.Count + " piezas)");
                if (c.Modelos.Count > 1) foreach (var m in c.Modelos) cPieza.Items.Add("Solo: " + m.Nombre);
                int idx = selPieza == null ? 0 : c.Modelos.FindIndex(m => m.Id == selPieza) + 1;
                cPieza.SelectedIndex = idx > 0 && idx < cPieza.Items.Count ? idx : 0;
                cPieza.Enabled = cPieza.Items.Count > 1;
            };

            cCampana.SelectedIndexChanged += (s, e) =>
            {
                var c = CampanaElegida(cCampana);
                if (c != null && c.Clave != selCampana) { selCampana = c.Clave; selPieza = null; }
                if (conPiezas) llenarPiezas();
                if (alCambiar != null) alCambiar();
            };
            cPieza.SelectedIndexChanged += (s, e) =>
            {
                var c = CampanaElegida(cCampana);
                var m = c != null ? PiezaElegida(c, cPieza) : null;
                selPieza = m != null ? m.Id : null;
                if (alCambiar != null) alCambiar();
            };

            col.Controls.Add(Etiqueta("Campaña"));
            col.Controls.Add(Fila(cCampana));
            if (conPiezas)
            {
                col.Controls.Add(Etiqueta("Qué mostrar"));
                col.Controls.Add(Fila(cPieza));
            }

            int inicial = IndiceCampanaPreferida();
            if (inicial >= 0) cCampana.SelectedIndex = inicial;
            cmbCampana = cCampana;
            cmbPieza = cPieza;
        }

        // Campaña a mostrar al entrar a un paso: la elegida antes, o la de la última pieza optimizada
        int IndiceCampanaPreferida()
        {
            if (campanas.Count == 0) return -1;
            int i = campanas.FindIndex(c => c.Clave == selCampana);
            if (i < 0 && ultimaCampana != null) i = campanas.FindIndex(c => string.Equals(c.Carpeta, ultimaCampana, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? i : 0;
        }

        Coleccion CampanaElegida(ComboBox cmb)
        {
            return cmb != null && cmb.SelectedIndex >= 0 && cmb.SelectedIndex < campanas.Count ? campanas[cmb.SelectedIndex] : null;
        }

        // null = campaña completa
        static Modelo PiezaElegida(Coleccion c, ComboBox cmb)
        {
            if (cmb == null || c.Modelos.Count < 2) return null;
            int i = cmb.SelectedIndex - 1;
            return i >= 0 && i < c.Modelos.Count ? c.Modelos[i] : null;
        }

        static string Consulta(Coleccion c, Modelo m)
        {
            return "?coleccion=" + Uri.EscapeDataString(c.Clave) + (m != null ? "&pieza=" + Uri.EscapeDataString(m.Id) : "");
        }

        // Ruta web del .glb con cada tramo codificado (las carpetas pueden tener tildes y espacios)
        static string UrlArchivo(Modelo m)
        {
            return string.Join("/", (m.Archivo ?? "").Split('/').Select(Uri.EscapeDataString));
        }

        string RutaLocal(Modelo m)
        {
            try { return Path.Combine(raiz, (m.Archivo ?? "").Replace('/', '\\')); }
            catch (ArgumentException) { return ""; }
        }

        string RutaPieza(string campana, string pieza)
        {
            try { return Path.Combine(carpetaModelos, campana, pieza, pieza + ".glb"); }
            catch (ArgumentException) { return ""; }
        }

        // Carpetas de campaña existentes (también las que aún no tienen piezas)
        List<string> CarpetasCampana()
        {
            if (!Directory.Exists(carpetaModelos)) return new List<string>();
            return Directory.GetDirectories(carpetaModelos)
                .Select(Path.GetFileName)
                .Where(n => !n.StartsWith("_") && !n.StartsWith("."))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        int ContarPiezas(string campana)
        {
            try
            {
                string dir = Path.Combine(carpetaModelos, campana);
                if (!Directory.Exists(dir)) return 0;
                return Directory.GetDirectories(dir)
                    .Select(Path.GetFileName)
                    .Count(p => !p.StartsWith("_") && !p.StartsWith(".") && File.Exists(Path.Combine(dir, p, p + ".glb")));
            }
            catch (ArgumentException) { return 0; }
        }

        // Nombres libres (tildes y espacios valen) pero que sirvan como nombre de carpeta
        static string ValidarNombre(string valor, string que)
        {
            if (string.IsNullOrEmpty(valor)) return "Falta el nombre de la " + que + ".";
            if (valor.StartsWith("_") || valor.StartsWith(".")) return "El nombre de la " + que + " no puede empezar con _ ni con punto.";
            if (valor.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "El nombre de la " + que + " tiene caracteres no permitidos (\\ / : * ? \" < > |).";
            return null;
        }

        // ── Utilidades ─────────────────────────────────────────────
        void AbrirLocal(string consulta)
        {
            string url = servidor.Iniciar(raiz);
            if (url == null)
            {
                MessageBox.Show(this, "No se pudo iniciar la vista previa local.\n\n" + servidor.Error, "Flujo AR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Abrir(url + consulta);
        }

        void Abrir(string destino)
        {
            try { Process.Start(destino); }
            catch
            {
                // Archivos sin programa asociado (por ejemplo .md): se abren en el Bloc de notas
                try { Process.Start("notepad.exe", "\"" + destino + "\""); }
                catch (Exception ex) { MessageBox.Show(this, "No se pudo abrir: " + destino + "\n\n" + ex.Message, "Flujo AR"); }
            }
        }

        void EnVentana(Action accion)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(accion); } catch (InvalidOperationException) { }
        }

        // Comillas simples de PowerShell
        static string Comillas(string s) { return "'" + s.Replace("'", "''") + "'"; }

        // Comillas dobles para argumentos de línea de comandos
        static string Comillas2(string s) { return "\"" + s + "\""; }

        static string MB(long bytes) { return (bytes / 1048576.0).ToString("0.00") + " MB"; }

        int S(int px) { return (int)Math.Round(px * escala); }

        // ── Controles con el estilo de la ventana ──────────────────
        Button Boton(string texto, bool primario)
        {
            var b = new Button
            {
                Text = texto, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand, Padding = new Padding(S(10), S(3), S(10), S(3)), Margin = new Padding(0, 0, S(8), 0),
                UseVisualStyleBackColor = false, ForeColor = Oscuro,
            };
            if (primario)
            {
                b.BackColor = Acento;
                b.FlatAppearance.BorderColor = Acento;
                b.Font = new Font(Font, FontStyle.Bold);
            }
            else
            {
                b.BackColor = Color.White;
                b.FlatAppearance.BorderColor = Color.FromArgb(196, 200, 210);
            }
            return b;
        }

        FlowLayoutPanel Columna()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty,
            };
        }

        FlowLayoutPanel Fila(params Control[] controles)
        {
            var f = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, S(10)),
            };
            f.Controls.AddRange(controles);
            return f;
        }

        // UseMnemonic = false: un "&" en enlaces o nombres de campaña se muestra tal cual
        Label Titulo(string t)
        {
            return new Label { Text = t, AutoSize = true, UseMnemonic = false, Font = new Font("Segoe UI Semibold", 16f), ForeColor = Oscuro, Margin = new Padding(0, 0, 0, S(14)) };
        }

        Label Texto(string t)
        {
            return new Label { Text = t, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(S(640), 0), ForeColor = Color.FromArgb(40, 44, 52), Margin = new Padding(0, 0, 0, S(10)) };
        }

        Label Etiqueta(string t)
        {
            return new Label { Text = t, AutoSize = true, UseMnemonic = false, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, S(4), 0, S(4)) };
        }

        Label Estado()
        {
            return new Label { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(S(640), 0), Margin = new Padding(0, S(2), 0, S(8)) };
        }
    }
}

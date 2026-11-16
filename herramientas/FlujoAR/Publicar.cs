using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FlujoAR
{
    // Paso 8: Publicar. Es el último paso y se hace UNA sola vez, se haya preparado un QR (paso 6), una
    // tarjeta (paso 7) o las dos cosas: GitHub Desktop sube todo lo que cambió en la carpeta del proyecto.
    // «Verificar publicación» revisa de una vez todas las campañas y todas las tarjetas.
    partial class Ventana
    {
        string campanaParaTarjeta, piezaParaTarjeta;      // atajo «Poner en tarjeta» del catálogo de QR

        void PasoPublicar(FlowLayoutPanel col)
        {
            col.Controls.Add(Texto("Último paso, y uno solo: sube de una vez todo lo que preparaste, sea para QR (paso 6), para tarjeta (paso 7) o para las dos. Si solo usaste una forma de entrega, no tienes que hacer nada en la otra."));
            col.Controls.Add(Texto("En GitHub Desktop:\n1.   Ábrelo con el botón de abajo.\n2.   Verás lo que cambió: piezas nuevas (models\\…), la lista de campañas y, si tocaste tarjetas, tarjetas.json y sus videos o páginas (tarjetas\\…).\n3.   Escribe un mensaje abajo a la izquierda, por ejemplo: Campaña Showroom.\n4.   Haz clic en Commit to main y luego en Push origin.\n5.   Espera 1–2 minutos y pulsa «Verificar publicación»."));

            var btnDesktop = Boton("Abrir GitHub Desktop", true);
            var btnVerificar = Boton("Verificar publicación", false);
            var lblResultado = Estado();
            btnDesktop.Click += (s, e) => AbrirGitHubDesktop();
            col.Controls.Add(Fila(btnDesktop, btnVerificar));

            if (!CargarCampanas(col)) return;
            btnVerificar.Click += (s, e) => VerificarTodo(btnVerificar, lblResultado);
            lblResultado.ForeColor = Gris;
            lblResultado.Text = "«Verificar publicación» revisa todas las campañas y todas las tarjetas, y te dice qué falta por subir.";
            col.Controls.Add(lblResultado);
        }

        // Compara lo que hay en línea con lo que hay en el computador: cada campaña (piezas y dirección),
        // la lista de campañas, cada tarjeta (qué abre y su presentación) y la página de la experiencia.
        void VerificarTodo(Button boton, Label resultado)
        {
            boton.Enabled = false;
            resultado.ForeColor = Gris;
            resultado.Text = "Verificando en " + UrlBase + " …";

            var lista = campanas.Select(c => new
            {
                c.Titulo,
                Pagina = Consulta(c, null).StartsWith("?") ? null : Consulta(c, null),
                Piezas = c.Modelos.Select(m => new { m.Nombre, Local = RutaLocal(m), Web = UrlArchivo(m) }).ToList(),
            }).ToList();
            string errorTarjetas;
            var tarjetas = LeerTarjetas(out errorTarjetas).Where(t => t.Coleccion != "").ToList();

            new Thread(() =>
            {
                var lineas = new List<string>();
                int pendientes = 0;
                bool sinConexion = false;
                long remoto, ignorar;
                string texto;

                lineas.Add("CAMPAÑAS (lo que abren los QR)");
                foreach (var c in lista)
                {
                    var faltas = new List<string>();
                    foreach (var p in c.Piezas)
                    {
                        long local = File.Exists(p.Local) ? new FileInfo(p.Local).Length : -1;
                        int codigo = Consultar(UrlBase + p.Web, "HEAD", out remoto, out texto);
                        if (codigo == 200 && remoto == local) continue;
                        if (codigo < 0) sinConexion = true;
                        faltas.Add(codigo == 200 ? p.Nombre + ": en línea hay otra versión" : codigo == 404 ? p.Nombre + ": todavía no está en línea" : p.Nombre + ": no se pudo consultar");
                    }
                    if (c.Pagina != null && Consultar(UrlBase + c.Pagina + "index.html", "HEAD", out ignorar, out texto) != 200)
                        faltas.Add("su dirección " + UrlBase + c.Pagina + " todavía no está en línea");
                    if (faltas.Count == 0) lineas.Add("✓  " + c.Titulo + " · " + (c.Piezas.Count == 1 ? "1 pieza" : c.Piezas.Count + " piezas") + " en línea");
                    else { pendientes++; lineas.Add("✗  " + c.Titulo + " · falta subir:  " + string.Join("  ·  ", faltas)); }
                }
                int cod = Consultar(UrlBase + "colecciones.json", "GET", out ignorar, out texto);
                string localJson = File.Exists(rutaColecciones) ? File.ReadAllText(rutaColecciones, Encoding.UTF8) : "";
                if (cod != 200 || Normalizar(texto) != Normalizar(localJson)) { pendientes++; lineas.Add("⚠  La lista de campañas en línea es distinta a la de tu computador."); }

                lineas.Add("");
                lineas.Add("TARJETAS AR");
                if (errorTarjetas != null) { pendientes++; lineas.Add("✗  " + errorTarjetas); }
                else
                {
                    // Lo que dice tarjetas.json en línea, tarjeta por tarjeta
                    var js = new JavaScriptSerializer();
                    Dictionary<string, object> enLinea = null, enDisco = null;
                    cod = Consultar(UrlBase + "tarjetas.json", "GET", out ignorar, out texto);
                    try { if (cod == 200) enLinea = js.DeserializeObject(texto) as Dictionary<string, object>; } catch { }
                    try { enDisco = js.DeserializeObject(File.ReadAllText(Path.Combine(raiz, "tarjetas.json"), Encoding.UTF8)) as Dictionary<string, object>; } catch { }
                    if (cod < 0) sinConexion = true;

                    foreach (var t in tarjetas)
                    {
                        var faltas = new List<string>();
                        object a = null, b = null;
                        if (enLinea != null) enLinea.TryGetValue(t.Numero, out a);
                        if (enDisco != null) enDisco.TryGetValue(t.Numero, out b);
                        if (a == null || b == null || js.Serialize(a) != js.Serialize(b)) faltas.Add("en línea todavía abre lo anterior");

                        var archivos = t.PresTipo == "video" ? new List<string> { t.PresArchivo } : t.PresTipo == "pdf" ? t.PresPaginas : new List<string>();
                        int sinSubir = 0;
                        foreach (string rel in archivos)
                        {
                            string disco = Path.Combine(raiz, rel.Replace('/', '\\'));
                            long local = File.Exists(disco) ? new FileInfo(disco).Length : -1;
                            if (Consultar(UrlBase + rel, "HEAD", out remoto, out texto) != 200 || remoto != local) sinSubir++;
                        }
                        if (sinSubir > 0) faltas.Add(t.PresTipo == "video" ? "falta subir su video" : "faltan " + sinSubir + " páginas de su PDF");
                        if (t.Direccion != "" && Consultar(UrlBase + t.Direccion + "/index.html", "HEAD", out ignorar, out texto) != 200)
                            faltas.Add("su dirección " + t.Direccion + " todavía no está en línea");

                        string que = t.Nombre != "" ? t.Nombre : t.Coleccion;
                        if (faltas.Count == 0) lineas.Add("✓  Tarjeta " + t.Numero + " · " + que + " · " + PresentacionCorta(t).ToLower());
                        else { pendientes++; lineas.Add("✗  Tarjeta " + t.Numero + " · " + que + ":  " + string.Join("  ·  ", faltas)); }
                    }

                    string experiencia = Path.Combine(raiz, "pruebas", "tarjeta", "index.html");
                    cod = Consultar(UrlBase + "pruebas/tarjeta/index.html", "GET", out ignorar, out texto);
                    if (cod != 200 || Normalizar(texto) != Normalizar(File.Exists(experiencia) ? File.ReadAllText(experiencia, Encoding.UTF8) : ""))
                    {
                        pendientes++;
                        lineas.Add("⚠  La página que abren las tarjetas tiene cambios sin subir.");
                    }
                }

                lineas.Add("");
                lineas.Add(sinConexion ? "No se pudo consultar el sitio: revisa tu conexión a internet y verifica de nuevo."
                    : pendientes == 0 ? "Todo está en línea: los QR y las tarjetas ya muestran lo último."
                    : "Lo marcado con ✗ o ⚠ todavía no está en línea. Haz Commit to main y Push origin; si ya lo hiciste, espera 1–2 minutos y verifica de nuevo.");
                EnVentana(() =>
                {
                    if (boton.IsDisposed) return;
                    boton.Enabled = true;
                    // Con pendientes va en texto normal: en rojo, las líneas con ✓ parecían errores
                    resultado.ForeColor = pendientes == 0 && !sinConexion ? Verde : System.Drawing.Color.FromArgb(40, 44, 52);
                    resultado.Text = string.Join("\n", lineas);
                });
            }) { IsBackground = true }.Start();
        }
    }
}

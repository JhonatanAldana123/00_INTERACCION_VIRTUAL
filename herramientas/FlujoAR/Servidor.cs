using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace FlujoAR
{
    // Servidor web local mínimo para revisar el visor antes de publicar
    // (abrir index.html con doble clic no carga los modelos).
    public class Servidor
    {
        static readonly Dictionary<string, string> Tipos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".js", "text/javascript; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" },
            { ".md", "text/plain; charset=utf-8" },
            { ".glb", "model/gltf-binary" },
            { ".gltf", "model/gltf+json" },
            { ".usdz", "model/vnd.usdz+zip" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".svg", "image/svg+xml" },
        };

        HttpListener oyente;
        string raiz;
        string url;
        public string Error { get; private set; }

        // Devuelve la URL base (http://localhost:PUERTO/) o null si no pudo iniciar
        public string Iniciar(string carpeta)
        {
            if (url != null) return url;
            raiz = Path.GetFullPath(carpeta).TrimEnd('\\') + "\\";

            for (int puerto = 8765; puerto < 8785; puerto++)
            {
                var l = new HttpListener();
                l.Prefixes.Add("http://localhost:" + puerto + "/");
                try { l.Start(); }
                catch (Exception ex) { Error = ex.Message; l.Close(); continue; }

                oyente = l;
                url = "http://localhost:" + puerto + "/";
                new Thread(Escuchar) { IsBackground = true }.Start();
                return url;
            }
            return null;
        }

        public void Detener()
        {
            if (oyente == null) return;
            try { oyente.Stop(); oyente.Close(); } catch { }
            oyente = null;
            url = null;
        }

        void Escuchar()
        {
            var l = oyente;
            while (l != null && l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = l.GetContext(); }
                catch { return; }   // se detuvo el servidor
                ThreadPool.QueueUserWorkItem(Atender, ctx);
            }
        }

        void Atender(object estado)
        {
            var ctx = (HttpListenerContext)estado;
            try
            {
                string relativa = Uri.UnescapeDataString(ctx.Request.Url.AbsolutePath).TrimStart('/');
                string ruta = Path.GetFullPath(Path.Combine(raiz, relativa.Replace('/', '\\')));

                // Carpetas (direcciones limpias como /alpina/): se sirve su index.html
                if (Directory.Exists(ruta))
                {
                    if (!ctx.Request.Url.AbsolutePath.EndsWith("/"))
                    {
                        ctx.Response.Redirect(ctx.Request.Url.AbsolutePath + "/" + ctx.Request.Url.Query);
                        return;
                    }
                    ruta = Path.Combine(ruta, "index.html");
                }

                // Solo archivos dentro de la carpeta del proyecto
                if (!ruta.StartsWith(raiz, StringComparison.OrdinalIgnoreCase) || !File.Exists(ruta))
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }

                string tipo;
                ctx.Response.ContentType = Tipos.TryGetValue(Path.GetExtension(ruta), out tipo) ? tipo : "application/octet-stream";
                ctx.Response.AddHeader("Cache-Control", "no-store");   // siempre la versión más reciente
                byte[] contenido = File.ReadAllBytes(ruta);
                ctx.Response.ContentLength64 = contenido.Length;
                if (ctx.Request.HttpMethod != "HEAD") ctx.Response.OutputStream.Write(contenido, 0, contenido.Length);
            }
            catch { ctx.Response.StatusCode = 500; }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }
    }
}

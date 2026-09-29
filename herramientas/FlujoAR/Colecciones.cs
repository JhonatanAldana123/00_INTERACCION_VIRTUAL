using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FlujoAR
{
    // Una pieza de la campaña: models/<Carpeta>/<Nombre>/<Nombre>.glb
    public class Modelo
    {
        public string Id;       // enlace de la pieza (&pieza=)
        public string Nombre;   // nombre de la carpeta de la pieza
        public string Archivo;  // ruta relativa al proyecto, con barras /
    }

    // Una campaña = una carpeta raíz de models/
    public class Coleccion
    {
        public string Clave;    // enlace de la campaña (?coleccion=)
        public string Titulo;
        public string Carpeta;
        public double? Exposicion;   // luz de la escena; null = valor por defecto del visor
        public List<Modelo> Modelos = new List<Modelo>();
    }

    // Lee colecciones.json, que genera scripts\generar-colecciones.js a partir de las carpetas
    public static class Colecciones
    {
        public static List<Coleccion> Cargar(string ruta)
        {
            var lista = new List<Coleccion>();
            if (!File.Exists(ruta)) return lista;

            var raiz = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ruta, Encoding.UTF8)) as Dictionary<string, object>;
            if (raiz == null) throw new FormatException("el archivo debe ser un objeto { \"campaña\": { ... } }");

            foreach (var par in raiz)
            {
                var c = new Coleccion { Clave = par.Key };
                var def = par.Value as Dictionary<string, object>;
                if (def != null)
                {
                    c.Titulo = Texto(def, "titulo") ?? par.Key;
                    c.Carpeta = Texto(def, "carpeta") ?? c.Titulo;
                    object valor;
                    if (def.TryGetValue("exposicion", out valor) && valor != null)
                        c.Exposicion = Convert.ToDouble(valor, System.Globalization.CultureInfo.InvariantCulture);
                    if (def.TryGetValue("modelos", out valor) && valor is object[])
                    {
                        foreach (var m in (object[])valor)
                        {
                            var obj = m as Dictionary<string, object>;
                            if (obj == null || !obj.ContainsKey("id")) continue;
                            string id = Convert.ToString(obj["id"]);
                            c.Modelos.Add(new Modelo { Id = id, Nombre = Texto(obj, "nombre") ?? id, Archivo = Texto(obj, "archivo") });
                        }
                    }
                }
                lista.Add(c);
            }
            return lista;
        }

        static string Texto(Dictionary<string, object> d, string clave)
        {
            object v;
            return d.TryGetValue(clave, out v) && v != null ? Convert.ToString(v) : null;
        }

        public static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char ch in s ?? "")
            {
                if (ch == '"') sb.Append("\\\"");
                else if (ch == '\\') sb.Append("\\\\");
                else if (ch < 0x20) sb.AppendFormat("\\u{0:x4}", (int)ch);
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }
    }
}

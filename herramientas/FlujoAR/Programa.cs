using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FlujoAR
{
    public static class Programa
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        // args[0] (opcional): carpeta del proyecto. La pasa herramientas\FlujoAR.ps1.
        [STAThread]
        public static void Main(string[] args)
        {
            // Texto nítido en pantallas con escala de Windows (125 %, 150 %…)
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            try { Application.SetCompatibleTextRenderingDefault(false); } catch (InvalidOperationException) { }

            string raiz = args != null && args.Length > 0 && EsProyecto(args[0]) ? args[0] : BuscarProyecto();
            if (raiz == null)
            {
                MessageBox.Show(
                    "No encontré la carpeta del proyecto.\n\nAbre Flujo AR con FlujoAR.bat, dentro de la carpeta 00_INTERACCION_VIRTUAL.",
                    "Flujo AR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.Run(new Ventana(Path.GetFullPath(raiz)));
        }

        static bool EsProyecto(string dir)
        {
            return File.Exists(Path.Combine(dir, "index.html")) && Directory.Exists(Path.Combine(dir, "models"));
        }

        // Sube desde la carpeta del ejecutable hasta encontrar index.html y models\
        static string BuscarProyecto()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
                if (EsProyecto(dir.FullName)) return dir.FullName;
            return null;
        }
    }
}

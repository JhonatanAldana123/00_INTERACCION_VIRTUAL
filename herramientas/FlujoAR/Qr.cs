using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace FlujoAR
{
    // Codificador QR sin dependencias: modo byte, corrección de errores M (15 %),
    // versiones 1 a 10 (hasta 213 bytes, de sobra para los enlaces del visor).
    // Sigue la norma ISO/IEC 18004.
    public static class Qr
    {
        // Por versión: codewords de corrección por bloque y grupos {bloques, datos, bloques, datos}
        static readonly int[] EcPorBloque = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        static readonly int[][] Grupos =
        {
            null,
            new[] { 1, 16, 0, 0 }, new[] { 1, 28, 0, 0 }, new[] { 1, 44, 0, 0 }, new[] { 2, 32, 0, 0 },
            new[] { 2, 43, 0, 0 }, new[] { 4, 27, 0, 0 }, new[] { 4, 31, 0, 0 }, new[] { 2, 38, 2, 39 },
            new[] { 3, 36, 2, 37 }, new[] { 4, 43, 1, 44 },
        };
        static readonly int[][] Alineacion =
        {
            null, new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 },
            new[] { 6, 34 }, new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
        };

        // Devuelve la matriz [fila, columna]; true = módulo oscuro
        public static bool[,] Codificar(string texto)
        {
            byte[] datos = Encoding.UTF8.GetBytes(texto);

            int version = 0;
            for (int v = 1; v <= 10; v++)
            {
                int bits = 4 + (v < 10 ? 8 : 16) + datos.Length * 8;
                if (bits <= CapacidadDatos(v) * 8) { version = v; break; }
            }
            if (version == 0) throw new ArgumentException("El texto es demasiado largo para el QR (máximo 213 bytes).");

            byte[] codewords = AgregarCorreccion(ArmarDatos(datos, version), version);
            return new Matriz(version).Construir(codewords);
        }

        static int CapacidadDatos(int v)
        {
            int[] g = Grupos[v];
            return g[0] * g[1] + g[2] * g[3];
        }

        // Modo byte (0100) + longitud + datos + terminador + relleno 0xEC/0x11
        static byte[] ArmarDatos(byte[] datos, int version)
        {
            var bits = new List<bool>();
            Action<int, int> poner = (valor, n) => { for (int i = n - 1; i >= 0; i--) bits.Add(((valor >> i) & 1) == 1); };
            poner(4, 4);
            poner(datos.Length, version < 10 ? 8 : 16);
            foreach (byte b in datos) poner(b, 8);

            int capacidad = CapacidadDatos(version) * 8;
            poner(0, Math.Min(4, capacidad - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(false);

            var resultado = new List<byte>();
            for (int i = 0; i < bits.Count; i += 8)
            {
                int b = 0;
                for (int j = 0; j < 8; j++) b = (b << 1) | (bits[i + j] ? 1 : 0);
                resultado.Add((byte)b);
            }
            for (bool alterna = true; resultado.Count < capacidad / 8; alterna = !alterna)
                resultado.Add(alterna ? (byte)0xEC : (byte)0x11);
            return resultado.ToArray();
        }

        // Divide en bloques, calcula Reed-Solomon y entrelaza
        static byte[] AgregarCorreccion(byte[] datos, int version)
        {
            int[] g = Grupos[version];
            int nEc = EcPorBloque[version];
            byte[] generador = PolinomioGenerador(nEc);

            var bloques = new List<byte[]>();
            var correcciones = new List<byte[]>();
            int pos = 0;
            for (int grupo = 0; grupo < 2; grupo++)
            {
                for (int b = 0; b < g[grupo * 2]; b++)
                {
                    var bloque = new byte[g[grupo * 2 + 1]];
                    Array.Copy(datos, pos, bloque, 0, bloque.Length);
                    pos += bloque.Length;
                    bloques.Add(bloque);
                    correcciones.Add(RestoReedSolomon(bloque, generador));
                }
            }

            var salida = new List<byte>();
            int maxDatos = Math.Max(g[1], g[3]);
            for (int i = 0; i < maxDatos; i++)
                foreach (var bloque in bloques)
                    if (i < bloque.Length) salida.Add(bloque[i]);
            for (int i = 0; i < nEc; i++)
                foreach (var ec in correcciones) salida.Add(ec[i]);
            return salida.ToArray();
        }

        // ── Aritmética en GF(256) con polinomio 0x11D ──────────────
        static readonly byte[] Exp = new byte[512];
        static readonly byte[] Log = new byte[256];

        static Qr()
        {
            int x = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = (byte)x;
                Log[x] = (byte)i;
                x <<= 1;
                if (x >= 256) x ^= 0x11D;
            }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }

        static byte Mul(byte a, byte b)
        {
            return (a == 0 || b == 0) ? (byte)0 : Exp[Log[a] + Log[b]];
        }

        // (x - α^0)(x - α^1)...(x - α^(n-1)), coeficientes de mayor a menor grado
        static byte[] PolinomioGenerador(int n)
        {
            byte[] p = { 1 };
            for (int i = 0; i < n; i++)
            {
                var q = new byte[p.Length + 1];
                for (int j = 0; j < q.Length; j++)
                {
                    byte a = j < p.Length ? p[j] : (byte)0;
                    byte b = j >= 1 ? Mul(p[j - 1], Exp[i]) : (byte)0;
                    q[j] = (byte)(a ^ b);
                }
                p = q;
            }
            return p;
        }

        static byte[] RestoReedSolomon(byte[] datos, byte[] generador)
        {
            int n = generador.Length - 1;
            var resto = new byte[n];
            foreach (byte d in datos)
            {
                byte factor = (byte)(d ^ resto[0]);
                Array.Copy(resto, 1, resto, 0, n - 1);
                resto[n - 1] = 0;
                for (int i = 0; i < n; i++) resto[i] ^= Mul(generador[i + 1], factor);
            }
            return resto;
        }

        // ── Construcción de la matriz ──────────────────────────────
        class Matriz
        {
            readonly int version, lado;
            readonly bool[,] oscuro, funcion;   // [fila, columna]

            public Matriz(int version)
            {
                this.version = version;
                lado = 17 + version * 4;
                oscuro = new bool[lado, lado];
                funcion = new bool[lado, lado];
            }

            void Fijar(int x, int y, bool valor)
            {
                oscuro[y, x] = valor;
                funcion[y, x] = true;
            }

            public bool[,] Construir(byte[] codewords)
            {
                DibujarPatrones();
                ColocarDatos(codewords);

                int mejor = 0, menorPenalidad = int.MaxValue;
                for (int m = 0; m < 8; m++)
                {
                    AplicarMascara(m);
                    DibujarFormato(m);
                    int p = Penalidad();
                    if (p < menorPenalidad) { menorPenalidad = p; mejor = m; }
                    AplicarMascara(m);   // XOR otra vez = deshacer
                }
                AplicarMascara(mejor);
                DibujarFormato(mejor);
                return oscuro;
            }

            void DibujarPatrones()
            {
                for (int i = 0; i < lado; i++)
                {
                    Fijar(6, i, i % 2 == 0);
                    Fijar(i, 6, i % 2 == 0);
                }
                Buscador(3, 3);
                Buscador(lado - 4, 3);
                Buscador(3, lado - 4);

                int[] pos = Alineacion[version];
                int n = pos.Length;
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        bool esquinaBuscador = (i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0);
                        if (!esquinaBuscador) Alineador(pos[i], pos[j]);
                    }

                DibujarFormato(0);   // reserva las zonas de formato
                DibujarVersion();
            }

            // Patrón de búsqueda 7×7 con su separador claro alrededor
            void Buscador(int cx, int cy)
            {
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || x >= lado || y < 0 || y >= lado) continue;
                        int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        Fijar(x, y, d != 2 && d != 4);
                    }
            }

            void Alineador(int cx, int cy)
            {
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Fijar(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

            // Nivel M = 00, más la máscara, con BCH(15,5) y XOR 0x5412
            void DibujarFormato(int mascara)
            {
                int dato = (0 << 3) | mascara;
                int resto = dato;
                for (int i = 0; i < 10; i++) resto = (resto << 1) ^ ((resto >> 9) * 0x537);
                int bits = ((dato << 10) | resto) ^ 0x5412;

                for (int i = 0; i <= 5; i++) Fijar(8, i, Bit(bits, i));
                Fijar(8, 7, Bit(bits, 6));
                Fijar(8, 8, Bit(bits, 7));
                Fijar(7, 8, Bit(bits, 8));
                for (int i = 9; i < 15; i++) Fijar(14 - i, 8, Bit(bits, i));

                for (int i = 0; i < 8; i++) Fijar(lado - 1 - i, 8, Bit(bits, i));
                for (int i = 8; i < 15; i++) Fijar(8, lado - 15 + i, Bit(bits, i));
                Fijar(8, lado - 8, true);   // módulo oscuro fijo
            }

            // Información de versión (solo desde la versión 7), BCH(18,6)
            void DibujarVersion()
            {
                if (version < 7) return;
                int resto = version;
                for (int i = 0; i < 12; i++) resto = (resto << 1) ^ ((resto >> 11) * 0x1F25);
                int bits = (version << 12) | resto;
                for (int i = 0; i < 18; i++)
                {
                    bool b = Bit(bits, i);
                    int a = lado - 11 + i % 3, c = i / 3;
                    Fijar(a, c, b);
                    Fijar(c, a, b);
                }
            }

            // Recorrido en zigzag de dos columnas, de abajo hacia arriba y de derecha a izquierda
            void ColocarDatos(byte[] codewords)
            {
                int i = 0, total = codewords.Length * 8;
                for (int derecha = lado - 1; derecha >= 1; derecha -= 2)
                {
                    if (derecha == 6) derecha = 5;
                    for (int vert = 0; vert < lado; vert++)
                        for (int j = 0; j < 2; j++)
                        {
                            int x = derecha - j;
                            bool subiendo = ((derecha + 1) & 2) == 0;
                            int y = subiendo ? lado - 1 - vert : vert;
                            if (funcion[y, x]) continue;
                            if (i < total) oscuro[y, x] = Bit(codewords[i >> 3], 7 - (i & 7));
                            i++;   // los módulos sobrantes quedan claros (bits de resto)
                        }
                }
            }

            void AplicarMascara(int m)
            {
                for (int y = 0; y < lado; y++)
                    for (int x = 0; x < lado; x++)
                    {
                        if (funcion[y, x]) continue;
                        bool invertir;
                        switch (m)
                        {
                            case 0: invertir = (x + y) % 2 == 0; break;
                            case 1: invertir = y % 2 == 0; break;
                            case 2: invertir = x % 3 == 0; break;
                            case 3: invertir = (x + y) % 3 == 0; break;
                            case 4: invertir = (x / 3 + y / 2) % 2 == 0; break;
                            case 5: invertir = x * y % 2 + x * y % 3 == 0; break;
                            case 6: invertir = (x * y % 2 + x * y % 3) % 2 == 0; break;
                            default: invertir = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                        }
                        if (invertir) oscuro[y, x] = !oscuro[y, x];
                    }
            }

            // Reglas de penalización N1–N4 de la norma, para elegir la mejor máscara
            int Penalidad()
            {
                int p = 0, oscuros = 0;
                for (int a = 0; a < lado; a++)
                {
                    for (int dir = 0; dir < 2; dir++)
                    {
                        int racha = 1;
                        for (int b = 1; b < lado; b++)
                        {
                            bool actual = dir == 0 ? oscuro[a, b] : oscuro[b, a];
                            bool previo = dir == 0 ? oscuro[a, b - 1] : oscuro[b - 1, a];
                            if (actual == previo) racha++;
                            else { if (racha >= 5) p += racha - 2; racha = 1; }
                        }
                        if (racha >= 5) p += racha - 2;

                        for (int b = 0; b + 10 < lado; b++)
                            if (PatronBuscador(a, b, dir)) p += 40;
                    }
                }
                for (int y = 0; y < lado - 1; y++)
                    for (int x = 0; x < lado - 1; x++)
                    {
                        bool c = oscuro[y, x];
                        if (c == oscuro[y, x + 1] && c == oscuro[y + 1, x] && c == oscuro[y + 1, x + 1]) p += 3;
                    }
                foreach (bool m in oscuro) if (m) oscuros++;
                int total = lado * lado;
                p += Math.Abs(oscuros * 20 - total * 10) / total * 10;
                return p;
            }

            // 1011101 con 4 módulos claros antes o después
            bool PatronBuscador(int a, int b, int dir)
            {
                Func<int, bool> m = k => dir == 0 ? oscuro[a, b + k] : oscuro[b + k, a];
                bool nucleoAl0 = m(0) && !m(1) && m(2) && m(3) && m(4) && !m(5) && m(6);
                bool clarosDespues = !m(7) && !m(8) && !m(9) && !m(10);
                bool nucleoAl4 = m(4) && !m(5) && m(6) && m(7) && m(8) && !m(9) && m(10);
                bool clarosAntes = !m(0) && !m(1) && !m(2) && !m(3);
                return (nucleoAl0 && clarosDespues) || (nucleoAl4 && clarosAntes);
            }

            static bool Bit(int valor, int i) { return ((valor >> i) & 1) != 0; }
        }

        // ── Salida ─────────────────────────────────────────────────
        const int Margen = 4;   // zona silenciosa obligatoria de 4 módulos

        public static Bitmap ABitmap(bool[,] m, int pixelesPorModulo)
        {
            int n = m.GetLength(0);
            int ladoPx = (n + Margen * 2) * pixelesPorModulo;
            var bmp = new Bitmap(ladoPx, ladoPx);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        if (m[y, x])
                            g.FillRectangle(Brushes.Black, (x + Margen) * pixelesPorModulo, (y + Margen) * pixelesPorModulo, pixelesPorModulo, pixelesPorModulo);
            }
            return bmp;
        }

        // PNG de unos 1000 px, apto para imprimir
        public static void GuardarPng(bool[,] m, string ruta)
        {
            int ppm = Math.Max(1, 1024 / (m.GetLength(0) + Margen * 2));
            using (var bmp = ABitmap(m, ppm)) bmp.Save(ruta, ImageFormat.Png);
        }

        // SVG vectorial: escala sin perder calidad en PDF o Illustrator
        public static void GuardarSvg(bool[,] m, string ruta)
        {
            int n = m.GetLength(0), total = n + Margen * 2;
            var sb = new StringBuilder();
            sb.AppendFormat("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {0} {0}\" shape-rendering=\"crispEdges\">\n", total);
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#ffffff\"/>\n<path fill=\"#000000\" d=\"");
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[y, x]) sb.AppendFormat("M{0} {1}h1v1h-1z", x + Margen, y + Margen);
            sb.Append("\"/>\n</svg>\n");
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(false));
        }
    }
}

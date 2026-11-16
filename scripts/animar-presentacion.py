# -*- coding: utf-8 -*-
"""
Convierte una presentación (PowerPoint + su PDF) en un video animado para la Tarjeta AR.

Mantiene el formato de cada diapositiva y le da movimiento:
  · las imágenes (renders, zooms, fotomontajes) entran una por una, creciendo;
  · toda la diapositiva se acerca muy despacio mientras está en pantalla;
  · la foto a pantalla completa se recorre con un acercamiento más marcado;
  · entre diapositivas hay un fundido con un leve desplazamiento.

El PDF da la imagen exacta de cada diapositiva; el PowerPoint dice dónde está cada imagen.
Las plantillas de Innercia traen diapositivas sin imágenes sueltas (portada, información PTA, cierre):
para esas, REGLAS_POR_DISENO indica qué zonas entran por partes, según el nombre del diseño.

Uso:
    python scripts/animar-presentacion.py "ruta/presentacion.pptx" "ruta/presentacion.pdf" "ruta/salida.mp4"

Necesita: python-pptx, pypdfium2, opencv-python (ya instalados en este computador).
El video sale en H.264 (el que reproducen los celulares), 1280 × 720, sin sonido.
"""
import sys, os, subprocess, tempfile
import numpy as np, cv2
import pypdfium2 as pdfium
from pptx import Presentation

ANCHO, ALTO = 1600, 900            # tamaño de trabajo
SALIDA = (1280, 720)               # tamaño del video
FPS = 30
TRANSICION = 0.6                   # segundos de fundido entre diapositivas
ESCALON = 0.22                     # segundos entre la entrada de un elemento y el siguiente
ENTRADA = 0.55                     # lo que tarda cada elemento en entrar
ACERCAMIENTO = 0.035               # cuánto se acerca la diapositiva mientras está en pantalla

# Zonas (x, y, ancho, alto, tipo) en fracciones de la diapositiva, por nombre de diseño de la plantilla
REGLAS_POR_DISENO = {
    'Diseño personalizado': [(0.555, 0.13, 0.33, 0.11, 'subir'), (0.555, 0.30, 0.33, 0.13, 'subir'), (0.555, 0.61, 0.35, 0.15, 'subir')],
    'Info PTA':             [(0.49, 0.06, 0.49, 0.29, 'subir'), (0.49, 0.41, 0.49, 0.33, 'subir'), (0.49, 0.86, 0.49, 0.11, 'subir')],
    '1_Cierre':             [(0.20, 0.24, 0.60, 0.27, 'crecer'), (0.08, 0.72, 0.84, 0.13, 'subir'), (0.33, 0.88, 0.34, 0.10, 'subir')],
}
DURACION_POR_DISENO = {'Diseño personalizado': 3.6, 'EntradaCapNegro': 3.2, 'Inicio 1': 5.0, '1_Cierre': 4.0}
DURACION = 5.2

suave = lambda t: 1 - (1 - min(1.0, max(0.0, t))) ** 3
def rebote(t):                      # llega un poco más lejos y vuelve
    t = min(1.0, max(0.0, t)); c = 1.4
    return 1 + (c + 1) * (t - 1) ** 3 + c * (t - 1) ** 2


def leer_paginas(pdf):
    doc = pdfium.PdfDocument(pdf)
    paginas = []
    for i in range(len(doc)):
        p = doc[i]
        img = p.render(scale=ANCHO / p.get_width()).to_numpy()
        if img.shape[2] == 4: img = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)      # pdfium ya entrega azul-verde-rojo, como OpenCV
        img = np.ascontiguousarray(img)
        paginas.append(cv2.resize(img, (ANCHO, ALTO), interpolation=cv2.INTER_AREA))
    return paginas


def leer_diapositivas(pptx):
    pres = Presentation(pptx)
    W, H = pres.slide_width, pres.slide_height
    lista = []
    for s in pres.slides:
        zonas = []
        for sh in s.shapes:
            if sh.shape_type == 13 and sh.width and sh.height:          # imagen
                x, y, w, h = sh.left / W, sh.top / H, sh.width / W, sh.height / H
                zonas.append((x, y, w, h, 'foto' if w > 0.95 and h > 0.95 else 'crecer'))
        diseno = s.slide_layout.name
        zonas += REGLAS_POR_DISENO.get(diseno, [])
        lista.append({'diseno': diseno, 'zonas': zonas, 'dur': DURACION_POR_DISENO.get(diseno, DURACION)})
    return lista


def rect(z):
    x, y, w, h = z[:4]
    x0, y0 = max(0, int(round(x * ANCHO))), max(0, int(round(y * ALTO)))
    x1, y1 = min(ANCHO, int(round((x + w) * ANCHO))), min(ALTO, int(round((y + h) * ALTO)))
    return x0, y0, x1, y1


def preparar(pagina, d):
    """Fondo sin los elementos que entran (cada zona se rellena con el color que la rodea) y el recorte de cada uno."""
    fondo = pagina.copy()
    elementos = []
    for z in d['zonas']:
        if z[4] == 'foto': continue
        x0, y0, x1, y1 = rect(z)
        if x1 - x0 < 8 or y1 - y0 < 8: continue
        m = 5
        anillo = np.concatenate([
            pagina[max(0, y0 - m):y0, x0:x1].reshape(-1, 3), pagina[y1:min(ALTO, y1 + m), x0:x1].reshape(-1, 3),
            pagina[y0:y1, max(0, x0 - m):x0].reshape(-1, 3), pagina[y0:y1, x1:min(ANCHO, x1 + m)].reshape(-1, 3)])
        color = np.median(anillo, axis=0) if len(anillo) else np.array([255, 255, 255])
        fondo[y0:y1, x0:x1] = color
        elementos.append({'r': (x0, y0, x1, y1), 'img': pagina[y0:y1, x0:x1].copy(), 'tipo': z[4]})
    # primero las imágenes grandes, después las chicas (los zooms redondos «saltan» al final)
    elementos.sort(key=lambda e: (e['tipo'] != 'crecer', -(e['r'][2] - e['r'][0]) * (e['r'][3] - e['r'][1])))
    return fondo, elementos


def pintar(lienzo, e, t):
    """Dibuja el elemento e en el momento t (0 = empieza a entrar)."""
    if t <= 0: return
    x0, y0, x1, y1 = e['r']; w, h = x1 - x0, y1 - y0
    a = suave(t / (ENTRADA * 0.7))
    if t >= ENTRADA:
        lienzo[y0:y1, x0:x1] = e['img']; return
    if e['tipo'] == 'crecer':
        s = 0.86 + 0.14 * rebote(t / ENTRADA)
        nw, nh = max(2, int(w * s)), max(2, int(h * s))
        img = cv2.resize(e['img'], (nw, nh), interpolation=cv2.INTER_LINEAR)
        cx, cy = x0 + w // 2, y0 + h // 2
        dx0, dy0 = cx - nw // 2, cy - nh // 2
    else:                                                               # 'subir'
        img = e['img']; nw, nh = w, h
        dx0, dy0 = x0, y0 + int((1 - suave(t / ENTRADA)) * ALTO * 0.035)
    # recorte a los bordes del lienzo
    sx0, sy0 = max(0, -dx0), max(0, -dy0)
    ex, ey = min(ANCHO, dx0 + nw), min(ALTO, dy0 + nh)
    dx0, dy0 = max(0, dx0), max(0, dy0)
    if ex <= dx0 or ey <= dy0: return
    trozo = img[sy0:sy0 + ey - dy0, sx0:sx0 + ex - dx0]
    zona = lienzo[dy0:ey, dx0:ex]
    lienzo[dy0:ey, dx0:ex] = cv2.addWeighted(trozo, a, zona, 1 - a, 0)


def acercar(img, escala, centro=(0.5, 0.5)):
    if abs(escala - 1) < 1e-4: return img
    cx, cy = centro[0] * ANCHO, centro[1] * ALTO
    M = np.float32([[escala, 0, cx - cx * escala], [0, escala, cy - cy * escala]])
    return cv2.warpAffine(img, M, (ANCHO, ALTO), flags=cv2.INTER_LINEAR, borderMode=cv2.BORDER_REPLICATE)


def cuadro(pagina, d, fondo, elementos, t):
    """La diapositiva en el segundo t (contado desde que empieza a verse)."""
    hay_foto = any(z[4] == 'foto' for z in d['zonas'])
    lienzo = fondo.copy()
    for i, e in enumerate(elementos):
        pintar(lienzo, e, t - 0.35 - i * ESCALON)
    avance = suave(t / d['dur']) if hay_foto else t / d['dur']
    if hay_foto: return acercar(lienzo, 1 + 0.11 * avance, (0.58, 0.62))       # foto completa: recorrido más marcado
    return acercar(lienzo, 1 + ACERCAMIENTO * avance)


def main(pptx, pdf, salida):
    paginas, diapos = leer_paginas(pdf), leer_diapositivas(pptx)
    n = min(len(paginas), len(diapos))
    if len(paginas) != len(diapos): print('Aviso: el PDF tiene', len(paginas), 'páginas y el PowerPoint', len(diapos), 'diapositivas; se usan', n)
    prep = [preparar(paginas[i], diapos[i]) for i in range(n)]
    # OpenCV no trae codificador H.264: se escribe un intermedio de buena calidad (MJPEG) y al final
    # scripts\video-a-h264.ps1 lo convierte con el codificador de Windows.
    intermedio = os.path.join(tempfile.gettempdir(), 'animar-presentacion-intermedio.avi')
    video = cv2.VideoWriter(intermedio, cv2.VideoWriter_fourcc(*'MJPG'), FPS, SALIDA)
    if not video.isOpened(): raise SystemExit('No se pudo crear el video intermedio.')
    video.set(cv2.VIDEOWRITER_PROP_QUALITY, 95)
    azul = np.full((ALTO, ANCHO, 3), (126, 92, 0), np.uint8)
    inicio = 0.0; total = 0
    tiempos = []
    for i in range(n):
        tiempos.append(inicio); inicio += diapos[i]['dur']
    duracion = inicio
    for k in range(int(round(duracion * FPS))):
        t = k / FPS
        i = max(j for j in range(n) if tiempos[j] <= t)
        tl = t - tiempos[i]
        f = cuadro(paginas[i], diapos[i], prep[i][0], prep[i][1], tl)
        if tl < TRANSICION:
            # La diapositiva nueva entra fundiéndose y corriéndose un poco desde la derecha sobre la anterior
            p = suave(tl / TRANSICION)
            antes = azul if i == 0 else cuadro(paginas[i - 1], diapos[i - 1], prep[i - 1][0], prep[i - 1][1], diapos[i - 1]['dur'] + tl)
            corr = int((1 - p) * ANCHO * 0.05)
            if corr: f = cv2.warpAffine(f, np.float32([[1, 0, corr], [0, 1, 0]]), (ANCHO, ALTO), borderMode=cv2.BORDER_REPLICATE)
            f = cv2.addWeighted(f, p, antes, 1 - p, 0)
        if duracion - t < 0.5: f = cv2.addWeighted(f, (duracion - t) / 0.5, azul, 1 - (duracion - t) / 0.5, 0)      # cierre a azul
        video.write(cv2.resize(f, SALIDA, interpolation=cv2.INTER_AREA))
        total += 1
    video.release()
    convertidor = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'video-a-h264.ps1')
    r = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', convertidor,
                        '-Entrada', intermedio, '-Salida', os.path.abspath(salida), '-Ancho', str(SALIDA[0]), '-Alto', str(SALIDA[1]), '-Fps', str(FPS)],
                       capture_output=True, text=True)
    try: os.remove(intermedio)
    except OSError: pass
    if r.returncode != 0: raise SystemExit('No se pudo convertir a H.264: ' + (r.stderr or r.stdout).strip())
    print('Listo:', salida, '·', total, 'cuadros ·', round(duracion, 1), 's ·', n, 'diapositivas ·', round(os.path.getsize(salida) / 1048576, 1), 'MB')


if __name__ == '__main__':
    if len(sys.argv) < 4: raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2], sys.argv[3])

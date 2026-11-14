# Crea pruebas/tarjeta/tarjeta-seguimiento.png: la imagen que la cámara aprende a reconocer (luego se compila
# a tarjeta-seguimiento.mind en pruebas/tarjeta/herramientas.html).
#   python scripts/objetivo-tarjeta.py "ruta\TARJETAS_AR_TIRO_Y_RETIRO_CARTA_SOLO_IMPRESION.pdf"
#
# Sale del PDF que se mandó a imprimir, para que coincida al milímetro con la tarjeta real. La zona del QR, el
# ícono AR y el número se tapan con el azul del fondo: cambian de una tarjeta a otra y el QR, además, acapara
# los puntos de seguimiento. Los «puntos de ancla» los elige después herramientas.html.
# Requiere: pip install pypdfium2 pillow
import sys
from pathlib import Path
import pypdfium2 as pdfium
from PIL import Image, ImageDraw

MM = 72 / 25.4
ANCHO, ALTO, SEP_COL, SEP_FILA = 45 * MM, 80 * MM, 8 * MM, 10 * MM      # imposición del entregable (carta horizontal)
pagina = pdfium.PdfDocument(sys.argv[1])[0]
x0 = (792 - (4 * ANCHO + 3 * SEP_COL)) / 2
y0 = (612 - (2 * ALTO + SEP_FILA)) / 2
escala = 1000 / ANCHO
hoja = pagina.render(scale=escala).to_pil().convert('RGB')
izq, arr = round(x0 * escala), round(y0 * escala)
tarjeta = hoja.crop((izq, arr, izq + 1000, arr + round(ALTO * escala)))   # frente de la tarjeta 1, 1000 px de ancho

azul = tarjeta.getpixel((100, 1000))
ImageDraw.Draw(tarjeta).rectangle([320, 0, 680, 395], fill=azul)          # QR + ícono AR + número (ver TAPADO en herramientas.html)

destino = Path(__file__).resolve().parent.parent / 'pruebas' / 'tarjeta' / 'tarjeta-seguimiento.png'
tarjeta.save(destino, optimize=True)
print('Guardado', destino, tarjeta.size)

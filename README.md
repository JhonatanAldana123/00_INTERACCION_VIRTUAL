# 00_INTERACCION_VIRTUAL
**Visor AR para presentaciones de diseño — GitHub Pages**

Página web que carga modelos 3D en pantalla completa y activa Realidad Aumentada nativa en iOS (Quick Look) y Android (Scene Viewer) desde un código QR incrustado en PDF.

Sitio publicado: https://jhonatanaldana123.github.io/00_INTERACCION_VIRTUAL/

> **La forma más fácil de trabajar:** doble clic en **`FlujoAR.bat`**. Se abre una ventana que te guía en 7 pasos, desde preparar la pieza en Rhino hasta guardar el QR. Guía: [docs/FLUJO_AR.md](docs/FLUJO_AR.md)

---

## Estructura del proyecto

```
00_INTERACCION_VIRTUAL/
├── FlujoAR.bat                 ← Ventana paso a paso: de Rhino al QR
├── index.html                  ← Visor 3D principal
├── qr.html                     ← Generador de QR en el navegador (alternativa a Flujo AR)
├── colecciones.json            ← Lista de campañas (se genera sola a partir de models/)
├── optimizar.bat               ← Arrastra un .glb encima para optimizarlo
├── herramientas/               ← Código de la ventana Flujo AR
├── scripts/
│   ├── optimizar-modelo.ps1    ← Optimizador de piezas
│   ├── generar-colecciones.js  ← Lee las carpetas y genera colecciones.json
│   └── analizar-glb.js         ← Reporte de peso, triángulos y texturas
├── docs/
│   ├── FLUJO_AR.md             ← Cómo usar la ventana Flujo AR
│   ├── CAMPANAS.md             ← Campañas: la carpeta manda
│   └── OPTIMIZAR_MODELOS.md    ← Preparar y optimizar piezas (Rhino, pesos, opciones)
└── models/                     ← Una carpeta por campaña, una subcarpeta por pieza
    └── Campaña Alpina/
        ├── _campana.json       ← Enlace fijo del QR y orden de las piezas
        ├── Exhibidor01/
        │   ├── Exhibidor01.glb ← Optimizado: sirve para Android, iPhone y web
        │   └── _original/      ← Exportación de Rhino sin optimizar (no se sube a GitHub)
        └── Bandeja01/
            └── Bandeja01.glb
```

---

## Cómo agregar una pieza

Con **Flujo AR** (`FlujoAR.bat`) se hace todo sin terminal. A mano:

1. Prepara la pieza en Rhino (malla liviana, materiales simples, escala real) y exporta un `.glb`. Guía: [docs/OPTIMIZAR_MODELOS.md](docs/OPTIMIZAR_MODELOS.md)
2. Arrastra el `.glb` sobre `optimizar.bat` y escribe la campaña cuando lo pregunte → queda en `models/Campaña/Pieza/` y se actualiza `colecciones.json`
3. Commit y push con GitHub Desktop → GitHub Pages publica automáticamente
4. Genera el QR con Flujo AR (paso 7) o con `qr.html`

Solo hace falta el `.glb`: en iPhone, el visor genera el `.usdz` automáticamente a partir de él.

---

## Enlaces y QR

| QR | Enlace |
|---|---|
| Campaña completa | `https://jhonatanaldana123.github.io/00_INTERACCION_VIRTUAL/campana-alpina/` |
| Una sola pieza | `…/campana-alpina/bandeja01/` |

Las carpetas de la raíz con nombre de campaña (`alpina/`…) son esas direcciones limpias: las crea Flujo AR, no las edites a mano. La dirección base del sitio está en `sitio.json`.

- Con **1 pieza**, el QR de la campaña muestra esa pieza. Con **varias**, el cliente cambia de pieza con flechas o deslizando la barra inferior.
- Si agregas piezas a la campaña, **el mismo QR impreso las muestra**. Nunca hay que reimprimirlo.
- El enlace de cada campaña se guarda en su `_campana.json`: renombrar la carpeta no rompe el QR.

**Guía: [docs/CAMPANAS.md](docs/CAMPANAS.md)**

---

## Vista previa local

Desde Flujo AR (paso 4, botón **Abrir en el navegador**), o a mano:

```bash
python -m http.server 8000
```

Luego abre `http://localhost:8000/?coleccion=ENLACE` en el navegador.

> **Nota:** Abrir `index.html` directamente con doble clic **no funciona** para cargar modelos (bloqueo CORS). Usa siempre el servidor local o GitHub Pages.

---

## Formato de las piezas

| Archivo | Android | iPhone | Web |
|---------|---------|--------|-----|
| `.glb` optimizado (Draco + JPEG) | Scene Viewer | Quick Look (usdz generado automáticamente) | model-viewer |

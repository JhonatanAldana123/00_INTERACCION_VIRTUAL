# 00_INTERACCION_VIRTUAL
**Visor AR para presentaciones de diseño — GitHub Pages**

Página web que carga modelos 3D en pantalla completa y activa Realidad Aumentada nativa en iOS (Quick Look) y Android (Scene Viewer) desde un código QR incrustado en PDF.

---

## Estructura del proyecto

```
00_INTERACCION_VIRTUAL/
├── index.html          ← Visor 3D principal (no modificar)
├── README.md           ← Este archivo
└── models/             ← Aquí van TUS modelos
    ├── silla.glb       ← Para Android (Scene Viewer)
    ├── silla.usdz      ← Para iOS (Quick Look)
    ├── mesa.glb
    └── mesa.usdz
```

---

## Cómo agregar un nuevo modelo

1. Exporta desde Rhino: `modelo.glb` y `modelo.usdc` + texturas
2. Convierte el `.usdc` a `.usdz` con **Reality Converter** (macOS)
3. Copia `modelo.glb` y `modelo.usdz` en la carpeta `/models/`
4. Haz commit y push al repositorio → GitHub Pages publica automáticamente

---

## URL por modelo

Una vez publicado en GitHub Pages, la URL de cada pieza es:

```
https://TU-USUARIO.github.io/00_INTERACCION_VIRTUAL/?modelo=NOMBRE
```

**Ejemplos:**
```
?modelo=silla
?modelo=silla&nombre=Silla%20Auxiliar    ← con etiqueta personalizada
?modelo=mesa-comedor
?modelo=lampara-piso
```

El parámetro `modelo` debe coincidir exactamente con el nombre del archivo sin extensión.

---

## QR por pieza

Genera un QR para cada URL en [qrcode-monkey.com](https://www.qrcode-monkey.com) exportando en **SVG** para incrustar en el PDF sin pérdida de calidad.

---

## Publicar en GitHub Pages

1. Crea un repositorio en GitHub con el nombre `00_INTERACCION_VIRTUAL`
2. Sube todos los archivos (incluyendo la carpeta `/models/`)
3. Ve a **Settings → Pages → Branch: main → / (root)** → Save
4. GitHub Pages publica en: `https://TU-USUARIO.github.io/00_INTERACCION_VIRTUAL/`

---

## Vista previa local

Para probar localmente con los modelos reales, abre una terminal en esta carpeta y ejecuta:

```bash
# Python (recomendado — viene instalado en macOS y Windows)
python -m http.server 8000

# Node.js (si lo tienes instalado)
npx serve .
```

Luego abre `http://localhost:8000/?modelo=NOMBRE` en el navegador.

> **Nota:** Abrir `index.html` directamente con doble click **no funciona** para cargar modelos (bloqueo CORS). Usa siempre el servidor local o GitHub Pages.

---

## Formatos de modelo soportados

| Formato | Sistema | Visor nativo |
|---------|---------|-------------|
| `.glb`  | Android | Scene Viewer / ARCore |
| `.usdz` | iOS     | Quick Look AR |

Ambos archivos deben existir con el mismo nombre base para que el QR funcione en ambos sistemas.

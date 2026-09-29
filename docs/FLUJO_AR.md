# Flujo AR: la ventana paso a paso

**Doble clic en `FlujoAR.bat`** (en la carpeta del proyecto) y se abre una ventana que te guía de Rhino al QR. No hay que instalar nada.

A la izquierda están los 7 pasos. Puedes seguirlos en orden con **Siguiente →** o saltar directo a cualquiera, por ejemplo al paso 7 si solo necesitas un QR.

**La idea central:** cada **campaña** es una carpeta dentro de `models\`, y cada **pieza** (exhibidor, bandeja…) es una subcarpeta. Una campaña con varias piezas tiene un QR de colección; con una sola, un QR de esa pieza. Detalle en [CAMPANAS.md](CAMPANAS.md).

| Paso | Qué haces ahí |
|---|---|
| **1. Preparar en Rhino** | Lista de chequeo: malla liviana, bloques, materiales simples, escala y posición. Botón para abrir la guía completa. |
| **2. Exportar .glb** | Cómo exportar cada pieza desde Rhino y qué nombre ponerle. |
| **3. Optimizar** | **Examinar…** → eliges el `.glb` → eliges la **campaña** (o escribes una nueva) → **Optimizar**. La pieza queda en `models\Campaña\Pieza\`, con el original de Rhino en `_original\`. Antes de optimizar te dice cuántas piezas tendrá la campaña. |
| **4. Revisar** | Eliges la campaña (o una pieza) → **Abrir en el navegador**: la ves en el visor desde tu computador, antes de publicar. Con la barra **Luz de la escena** ajustas qué tan clara se ve y la guardas para toda la campaña. |
| **5. Campañas** | Lo que se detectó en las carpetas: piezas de cada campaña, su peso y su enlace. Aquí cambias el **orden** de las piezas (↑ Subir / ↓ Bajar). |
| **6. Publicar** | Instrucciones de GitHub Desktop, botón para abrirlo y **Verificar publicación**, que revisa pieza por pieza si ya están en línea. |
| **7. Generar QR** | **Catálogo** con una tarjeta por campaña (su QR abre todas sus piezas). Cada tarjeta tiene **PNG** (impresión) y **SVG** (PDF, Illustrator) y marca **✓** si ya está guardada. **Guardar todos** los guarda de una vez en la carpeta `qrs\`. |

---

## Flujo típico: una campaña con dos piezas

1. **Pasos 1–2:** preparas y exportas en Rhino cada pieza por separado: `Exhibidor01.glb` y `Bandeja01.glb`.
2. **Paso 3:** Examinar… → `Exhibidor01.glb` → Campaña: escribe `Campaña Alpina` → Optimizar → mensaje verde *"Listo"*.
   Repite con `Bandeja01.glb`, eligiendo `Campaña Alpina` de la lista.
3. **Paso 4:** Campaña Alpina → Abrir en el navegador → revisas las dos piezas con las flechas.
4. **Paso 5** (opcional): cambias el orden si quieres que la bandeja salga primero.
5. **Paso 6:** Abrir GitHub Desktop → **Commit to main** → **Push origin** → espera 1–2 minutos → Verificar publicación → todo en ✓.
6. **Paso 7:** en el catálogo, tarjeta **Alpina** → PNG o SVG (o **Guardar todos**) → escanéalo con Android y con iPhone.

> **¿Agregas una pieza a una campaña que ya tiene QR impreso?** No necesitas un QR nuevo: basta con los pasos 3 y 6. El mismo QR ya muestra la pieza nueva.

---

## Preguntas frecuentes

**¿Por qué es un `.bat` y no un `.exe`?**
Tu Windows tiene activado *Smart App Control*, que bloquea los `.exe` sin firma digital de pago. Por eso un `.exe` hecho por nosotros podía abrir un día y quedar bloqueado al siguiente. El `.bat` abre la ventana con PowerShell, que viene firmado por Microsoft y carga el programa en memoria. Por eso siempre abre.

**¿Qué necesita el computador?**
Windows 10 u 11 con Node.js instalado (para optimizar y para leer las carpetas de campañas). La primera optimización descarga gltf-transform automáticamente.

**¿Los QR necesitan internet para generarse?**
No. La ventana los genera por sí misma. Para que *funcionen* al escanearlos, la campaña tiene que estar publicada (paso 6).

**¿Puedo renombrar la carpeta de una campaña?**
Sí. El enlace del QR quedó guardado en `_campana.json` dentro de la carpeta, así que el QR impreso sigue funcionando. No borres ese archivo.

**¿Puedo organizar las carpetas a mano en el Explorador?**
Sí: crear, renombrar o borrar carpetas de campañas y piezas. Los pasos 4 a 7 vuelven a leer las carpetas cada vez que los abres (o pulsa **Volver a leer carpetas** en el paso 5). Si algo no cumple la estructura (por ejemplo, un `.glb` suelto en la carpeta de la campaña), el paso 5 lo avisa en naranja.

**Una parte de la pieza se ve negra.**
Ese objeto no tiene material asignado en Rhino: el exportador le pone uno negro y metálico. El paso 3 lo avisa en naranja al optimizar. En Rhino, asígnale un material (con Metálico en 0), exporta de nuevo y vuelve a optimizar con el mismo nombre de pieza.

**El paso 6 dice ⚠ o ✗.**
- *"todavía no está publicada"*: falta Commit y Push en GitHub Desktop.
- *"es otra versión"* o *"la lista de campañas es distinta"*: ya hiciste Push pero GitHub Pages aún no se actualizó. Espera 1–2 minutos y verifica de nuevo. También aparece si hiciste cambios después del último Push.

---

## Para quien mantenga el proyecto

| Archivo | Función |
|---|---|
| `FlujoAR.bat` | Lanzador: abre `herramientas\FlujoAR.ps1` sin ventana de consola |
| `herramientas\FlujoAR.ps1` | Compila en memoria el código C# (Add-Type) y abre la ventana |
| `herramientas\FlujoAR\Ventana.cs` | La ventana y los 7 pasos |
| `herramientas\FlujoAR\Qr.cs` | Generador de QR propio (ISO/IEC 18004, corrección M), sin dependencias |
| `herramientas\FlujoAR\Colecciones.cs` | Lee `colecciones.json` |
| `herramientas\FlujoAR\Servidor.cs` | Servidor local para la vista previa del paso 4 |
| `scripts\generar-colecciones.js` | Lee las carpetas de `models\` y genera `colecciones.json`, el `_campana.json` de cada campaña y las direcciones limpias (`alpina\index.html`…, anotadas en `_paginas.json`) |
| `sitio.json` | Dirección del sitio publicado, usada para los QR |
| `scripts\optimizar-modelo.ps1` | El optimizador que ejecuta el paso 3 (el mismo de `optimizar.bat`) |

Para cambiar la dirección de los QR (por ejemplo, con un dominio propio), edita `sitio.json`. Flujo AR y el optimizador la leen de ahí.

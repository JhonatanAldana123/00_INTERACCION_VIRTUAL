# Preparar y optimizar piezas: de Rhino al QR

Guía para preparar, optimizar y publicar una pieza en el visor AR. Con este proceso, un exhibidor de prueba pasó de **19,2 MB a 1,42 MB** sin cambios visibles.

> Lo más fácil es hacerlo con la ventana **Flujo AR** (`FlujoAR.bat`), que guía cada paso: [FLUJO_AR.md](FLUJO_AR.md). Esta guía explica el detalle y la alternativa sin ventana.

**Resumen rápido:**

1. Preparar la pieza en Rhino
2. Exportar el `.glb`
3. Optimizarla dentro de su campaña (Flujo AR paso 3, o arrastrarla sobre `optimizar.bat`)
4. Revisarla en el visor local
5. Publicar con GitHub Desktop
6. Generar el QR

---

## Requisitos (solo la primera vez)

| Programa | Para qué | Cómo comprobar que está instalado |
|---|---|---|
| **Rhino 8** | Modelar y exportar | — |
| **Node.js LTS** ([nodejs.org](https://nodejs.org)) | Optimizar y leer las campañas | En una terminal: `node --version` |
| **GitHub Desktop** | Publicar los cambios | — |

La primera vez que optimizas, se descarga sola la herramienta **gltf-transform** (unos 30 MB). Las siguientes veces ya no descarga nada.

---

## Paso 1 — Preparar la pieza en Rhino

Esto es lo que más reduce el peso. El optimizador no puede corregirlo por ti.

### 1.1 Malla liviana para las piezas repetidas
La mayor parte del peso suele estar en los elementos que se repiten (botellas, tapas, cajas).

1. Toma **un** elemento (por ejemplo, una botella).
2. Crea su malla con `Mesh` → **Opciones detalladas**:
   - Ángulo máximo: **15–20°**
   - Longitud mínima de arista: **1–2 mm**
   - Desactiva **Refinar malla**
3. Si ya es una malla, usa `ReduceMesh` para bajar polígonos.
4. Meta: **1.000–1.500 triángulos** por elemento repetido. El exhibidor de ejemplo usa 384 por botella y se ve bien.
5. Convierte el elemento en **Bloque** (`Block`) y copia el bloque.

> El exportador glTF de Rhino convierte los bloques en copias normales. Aun así, usar bloques te asegura que todas las copias usen la misma malla liviana.

### 1.2 Borrar lo que no se ve
- La base de las botellas, el interior de la estructura y las caras tapadas.
- Tornillos, pestañas, espesores internos, ensambles.
- Las filas del fondo que nunca se ven. Ojo: en AR la gente camina alrededor del modelo.

### 1.3 Materiales simples
- Usa materiales **PBR** básicos: color, rugosidad y metalicidad.
- Evita **transmisión** (vidrio) y **clearcoat** (barniz): son pesados en celular y el AR de iPhone no los reproduce.
- **Evita la transparencia** en las texturas si no la necesitas. Si algún material es transparente, el optimizador deja sus texturas en PNG y el archivo pesa más.
- **Todo objeto debe tener material.** Los que no tienen salen negros y metálicos en el visor (el optimizador lo avisa).
- **Revisa las etiquetas en vista Renderizada** antes de exportar. Si Rhino exporta el mapeo solo en uno de varios objetos copiados (pasó con 20 de 21 botellas), el optimizador lo repara solo, siempre que sean copias exactas.
- **Lo que en Rhino se ve bien no siempre sale bien en el `.glb`.** Productos copiados con la etiqueta corrida, o que se ven huecos por delante, son errores conocidos del exporte: el optimizador los detecta y los arregla solo. La lista completa está en [Errores de Rhino que el optimizador arregla solo](#errores-de-rhino-que-el-optimizador-arregla-solo).
- Ejecuta `Purge` para borrar materiales, bloques y capas sin usar.

### 1.4 Escala y posición para AR
- **Unidades reales.** En AR la pieza se ve a escala 1:1: si está mal, sale gigante o miniatura.
- **Base apoyada en Z = 0** y centrada en el origen, para que aparezca sobre el piso.
- Si en el visor el frente sale de espaldas, gira la pieza en Rhino y exporta de nuevo.

### Metas de peso
| | Meta |
|---|---|
| Triángulos totales | menos de 100.000 |
| `.glb` optimizado | menos de 5 MB (ideal: menos de 2 MB) |
| Texturas | hasta 2048 px |

---

## Paso 2 — Exportar el `.glb` desde Rhino

1. Selecciona solo la pieza. Cada pieza de la campaña (exhibidor, bandeja, rompetráfico…) se exporta por separado.
2. `Archivo → Exportar selección` (`Export Selected`) → tipo **glTF Binary (.glb)**.
3. Ponle al archivo el **nombre de la pieza**: `Exhibidor01.glb`, `Bandeja 01.glb`. Puede tener tildes y espacios, pero no `\ / : * ? " < > |`.
4. Guárdalo donde quieras, por ejemplo en `Descargas`.

**No hace falta exportar `.usdz`.** El visor genera la versión de iPhone automáticamente a partir del `.glb`.

---

## Paso 3 — Optimizar

### Opción A: Flujo AR (recomendada)
Paso 3 de la ventana: **Examinar…** → eliges el `.glb` → eliges la **campaña** de la lista o escribes una nueva → **Optimizar**. Ver [FLUJO_AR.md](FLUJO_AR.md).

### Opción B: arrastrar y soltar
1. Arrastra tu `.glb` sobre `optimizar.bat` (en la carpeta del proyecto).
2. En la ventana negra escribe el **nombre de la campaña** y presiona Enter. Te muestra las campañas que ya existen.
3. Al final verás el resultado:

```
===================== RESULTADO =====================
Peso:        19,16 MB  ->  2,43 MB   (-87 %)
Triangulos:  64868
Texturas:    0,66 MB en 13 imagenes (maximo 2048 px)
Archivo:     models\Campaña Alpina\Exhibidor01\Exhibidor01.glb
Campaña:     Campaña Alpina (2 piezas)
QR campaña:  https://innerciaar.com/campana-alpina/
QR pieza:    https://innerciaar.com/campana-alpina/exhibidor01/
```

### Opción C: desde la terminal (para usar otras opciones)

```bash
powershell -ExecutionPolicy Bypass -File scripts\optimizar-modelo.ps1 "C:\ruta\Exhibidor01.glb" -Campana "Campaña Alpina"
```

| Opción | Qué hace | Por defecto |
|---|---|---|
| `-Campana "Campaña Alpina"` | Carpeta de la campaña | Se pregunta |
| `-Nombre Bandeja01` | Nombre de la pieza | Nombre del archivo |
| `-MaxTextura 1024` | Lado máximo de las texturas en píxeles | 2048 |
| `-CalidadJpeg 90` | Calidad de las texturas JPEG (1–100) | 85 |

### Qué hace el optimizador
| Paso | Acción | Por qué |
|---|---|---|
| 1 | Crea `models\Campaña\Pieza\` y copia el original a `_original\` | Orden, y poder volver atrás. `_original` no se sube a GitHub. |
| 2 | Arregla los errores conocidos de Rhino (ver la tabla de más abajo): mapeo de objetos copiados, caras al revés, líneas y puntos sueltos, vidrios y calados. Borra datos sin usar y une los objetos que comparten material | Las curvas o bordes que se cuelan al exportar hacen que el AR de Android y de iPhone rechace la pieza completa ("no se puede cargar el objeto"). | Rhino a veces exporta el mapeo (UV) solo en uno de varios objetos iguales (por ejemplo, 1 de 21 botellas) y los demás se ven sin etiqueta: si son copias exactas, se les aplica el mismo mapeo. Unir objetos no cambia la forma, pero una pieza con miles de objetos sueltos pesa mucho más y va lenta en el celular (una isla bajó de 15 MB a 4 MB solo con esto). |
| 2b | Centra la pieza en el origen y apoya su base en el piso | En AR aparece justo donde el cliente toca, sin quedar corrida ni hundida en el suelo. No hace falta ubicarla en el 0,0,0 en Rhino. |
| 3 | Limita las texturas a 2048 px | En un celular no se nota más resolución |
| 4 | Convierte las texturas PNG a JPEG | Pesan 5 a 7 veces menos. Se omite si hay transparencia. |
| 5 | Comprime la geometría con Draco | Reduce la geometría a una fracción. Compatible con el visor web y el AR de Android. |
| 6 | Actualiza `colecciones.json` | Así la campaña (y su QR) incluye la pieza nueva |

### Mensajes que puedes ver
| Mensaje | Qué significa | Qué hacer |
|---|---|---|
| `npm warn deprecated ...` | Aviso interno de npm | Nada, es normal |
| `Este archivo ya está optimizado` | Le pasaste un `.glb` que ya salió del optimizador | Usa el `.glb` exportado de Rhino, o el de `_original\` |
| `caracteres no permitidos` | El nombre tiene `\ / : * ? " < > \|` | Cambia el nombre |
| `No se encontró Node.js` | Falta instalar Node.js | Instálalo desde [nodejs.org](https://nodejs.org) |
| `AVISO: mas de 100.000 triangulos` | La malla sigue muy densa | Vuelve al paso 1.1 |
| `AVISO: hay objetos sin material asignado` | Objetos sin material en Rhino: se verán negros | Asígnales un material (Metálico en 0) y exporta de nuevo |
| `AVISO: N objetos tienen textura pero no tienen mapeo (UV)` | Su imagen no se verá, y no son copias de otro objeto con mapeo, así que no se pudieron reparar solos | En Rhino, aplícales el mapeo de textura y exporta de nuevo |
| `Se omite la conversion a JPEG` | Hay materiales transparentes | Si no necesitas transparencia, quítala en Rhino (paso 1.3) |
| `ARREGLADO: ...` | La pieza traía un error conocido de Rhino y el optimizador lo corrigió solo | Nada: revisa la pieza en el visor. Qué error fue, en la tabla de abajo |

### Errores de Rhino que el optimizador arregla solo

Este es el registro de los errores que ya conocemos. Cada vez que optimizas una pieza (paso 3 de Flujo AR, `optimizar.bat` o el animador), el optimizador los busca, los corrige y lo anota al final como `ARREGLADO: ...`. Flujo AR repite esas líneas junto al resultado, con un ✓.

| Lo que se ve en el visor | Qué pasó | Qué hace el optimizador | Visto en |
|---|---|---|---|
| Productos copiados con la **etiqueta corrida o partida** (el logo arriba y el resto abajo), cada copia distinta; **tapas en blanco** | El producto se mapeó una vez y luego se copió, movió o cambió de tamaño. En Rhino la imagen viaja con cada copia; en el `.glb` queda clavada donde estaba el original | Vuelve a encajar la imagen completa en cada objeto y en sus copias iguales. Solo actúa si el mapeo es una proyección plana, ocupa casi una imagen entera y se sale más de 5 % | Latas, tapas y pastas del shipper y la bandeja de Zenú (20/11/2026) |
| Un producto se ve **hueco por delante**, como si la cara frontal fuera transparente y se viera el interior | El objeto viene con las caras mirando hacia adentro. Rhino dibuja las dos caras y no se nota; el visor y el AR solo dibujan la de afuera | Voltea triángulos y normales. Solo en objetos cerrados (sin bordes sueltos): en una lámina abierta no se puede saber cuál es el lado de afuera | Empaques de pasta Doria del shipper de Zenú (20/11/2026) |
| Al girar la pieza, el **arte parpadea** o se le cuela en rayas el color de la estructura | El arte es una lámina puesta a 0,2–0,4 mm de la cara que cubre: tan pegadas que el visor no distingue cuál va adelante | Adelanta la cara del arte hasta dejarla a 1 mm de la que cubre. La gráfica no se estira ni cambia de tamaño | Artes del shipper y la bandeja de Zenú (20/11/2026) |
| Varias copias de un producto **sin etiqueta**, de un solo color | Rhino escribió el mapeo solo en una de las copias | Le pone a las demás el mapeo de la que sí lo trae. Solo si son copias exactas | Botellas de Alpina (29/09/2026) |
| «Ver en AR» dice que **no se puede cargar el objeto** | Se coló una curva o un borde suelto en el exporte | Quita las líneas y puntos sueltos | 29/09/2026 |
| El **vidrio sale como una placa negra** en AR | El AR no entiende la transmisión del vidrio de Rhino | Lo cambia por transparencia sencilla; en el visor de la página sigue viéndose como vidrio | Exhibidor de Rexona (16/11/2026) |
| Una **malla o rejilla** se ve como lámina maciza | El calado de Rhino es una imagen de transmisión que el AR no usa | La convierte en un recorte real | Canasta de Rexona (16/11/2026) |

Si aparece un error que no está en la tabla, no se arregla solo todavía. Para registrarlo (quien mantenga el proyecto):

1. La detección y el arreglo van en `scripts\reparar-uv.js`, con un contador en la línea de resultado que imprime al final.
2. En `scripts\optimizar-modelo.ps1` se agrega su línea a la lista `$arreglos`.
3. Se agrega su fila a esta tabla.
4. Antes de regenerar nada, se corre `reparar-uv.js` contra todos los `models\*\*\_original\*.glb`: las campañas que estaban bien deben dar cero cambios.

---

## Paso 4 — Revisar en local

Flujo AR → paso 4 → elige la campaña → **Abrir en el navegador**. Revisa texturas, escala y orientación; con varias piezas, pasa por todas con las flechas.

> Abrir `index.html` con doble clic **no funciona**: el navegador bloquea la carga del modelo. Usa Flujo AR o un servidor local (`python -m http.server 8000`).

---

## Paso 5 — Publicar con GitHub Desktop

1. Abre GitHub Desktop. Verás la carpeta de la pieza nueva y `colecciones.json` en la lista de cambios.
2. Escribe un mensaje, por ejemplo `Campaña Alpina: bandeja`.
3. **Commit to main** → **Push origin**.
4. Espera 1–2 minutos a que GitHub Pages actualice el sitio. Flujo AR (paso 6) verifica que ya esté en línea.

---

## Paso 6 — Generar el QR

Flujo AR → paso 7 → campaña → **campaña completa** o **una sola pieza** → Guardar PNG o SVG. **Escanéalo con Android y con iPhone** antes de imprimir.

**¿Agregaste una pieza a una campaña que ya tiene QR?** No necesitas un QR nuevo: el mismo QR ya la muestra. Ver [CAMPANAS.md](CAMPANAS.md).

---

## Volver al original

El original queda siempre en `models\Campaña\Pieza\_original\Pieza.glb`. Para reoptimizarlo con otras opciones:

```bash
powershell -ExecutionPolicy Bypass -File scripts\optimizar-modelo.ps1 "models\Campaña Alpina\Exhibidor01\_original\Exhibidor01.glb" -Campana "Campaña Alpina" -MaxTextura 1024
```

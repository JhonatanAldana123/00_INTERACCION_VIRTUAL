# Flujo AR: la ventana paso a paso

**Doble clic en `FlujoAR.bat`** (en la carpeta del proyecto) y se abre una ventana que te guía de Rhino al QR. No hay que instalar nada.

A la izquierda están los 7 pasos. Puedes seguirlos en orden con **Siguiente →** o saltar directo a cualquiera, por ejemplo al paso 7 si solo necesitas un QR.

**La idea central:** cada **campaña** es una carpeta dentro de `models\`, y cada **pieza** (exhibidor, bandeja…) es una subcarpeta. Una campaña con varias piezas tiene un QR de colección; con una sola, un QR de esa pieza. Detalle en [CAMPANAS.md](CAMPANAS.md).

| Paso | Qué haces ahí |
|---|---|
| **1. Preparar en Rhino** | Lista de chequeo: malla liviana, bloques, materiales simples, escala y posición. Botón para abrir la guía completa. |
| **2. Exportar .glb** | Cómo exportar cada pieza desde Rhino y qué nombre ponerle. |
| **3. Optimizar o animar** | **Pieza estática:** **Examinar…** → eliges el `.glb` → eliges la **campaña** (o escribes una nueva) → **Optimizar**. La pieza queda en `models\Campaña\Pieza\`, con el original de Rhino en `_original\`. Antes de optimizar te dice cuántas piezas tendrá la campaña. **Pieza animada:** el animador (ver abajo). |
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

## El animador (paso 3 → «Pieza animada»)

Arma una animación de armado (paredes que se doblan, piezas que llegan, piezas que giran) sin abrir Blender.
La ventana se agranda mientras lo usas. Necesita Blender instalado: la primera vez lo busca solo; si no
lo encuentra, haz clic en **Falta Blender** y elige `blender.exe`.

1. **Examinar…** → el `.glb` de Rhino → **Analizar pieza** (unos segundos). Aparece la pieza con 4 vistas:
   Frente ¾, Atrás ¾, De frente y Desde arriba.
2. **+ Grupo** por cada cosa que se mueve junta, y **clic** sobre sus piezas en la vista
   (clic otra vez = la quita). **Arrastrar** agrega todo lo que queda dentro del recuadro (desde arriba es
   ideal para tomar una fila de botellas); arrastrar con **clic derecho** quita.
   Las caras y gráficas pegadas por dentro de un grupo **se incluyen solas** (se pintan con su color).
   **+ Iguales** agrega todas las piezas iguales a la última que tocaste (por ejemplo, las 21 botellas).
   **Precisión:** la **rueda** acerca hacia donde apuntas, el **botón central** (rueda presionada) mueve la vista y
   **Encuadrar** vuelve. Junto al cursor aparece qué pieza es y qué hará el clic, y la pieza se marca con un
   contorno. **Clic derecho** = lista de **todas las piezas en ese punto**, de adelante hacia atrás (también las
   que están detrás o pegadas): al pasar por cada una se resalta, y con clic la pones o la quitas.
   **Ocultar para llegar a lo de atrás:** el **👁** a la derecha de cada grupo lo oculta o lo muestra (sin cambiar
   el grupo que editas). Las piezas ocultas se oscurecen y dejan de responder al clic al instante, y en unos
   segundos la vista se redibuja sin ellas para que veas y elijas lo que estaba detrás. **H** con el mouse sobre una
   pieza la oculta; **Shift+H** o **Mostrar todo** (arriba de la lista) muestra todo. Ocultar no cambia la animación.
   **Como en Rhino:** **Ctrl+clic** solo quita la pieza del grupo, **Shift+clic** solo la agrega y **Ctrl+arrastrar**
   quita las del recuadro. El recuadro depende de hacia dónde arrastras: **hacia la derecha → (ventana, línea
   continua)** toma las piezas que quedan **completas dentro, también las que están detrás** (desde la vista De
   frente, un recuadro alrededor del producto toma todas las filas sin tocar piso ni espaldar); **hacia la izquierda
   ← (cruce, línea punteada)** toma solo lo que se ve y toca el recuadro. El clic derecho también ofrece
   **Quitar del grupo todas las iguales**.
   **Control de piezas:** arrastrar solo toma piezas **libres** (sin grupo); **Shift + arrastrar** también toma las
   de otros grupos. **Bloquear** protege un grupo (🔒 en la lista): sus piezas no se pueden quitar ni tomar desde
   otro grupo. Consejo: agrupa y bloquea primero el producto; después las bandejas se eligen sin tocarlo.
   En la vista previa, pestaña **Piezas** (dentro de ✎ Editar), lo mismo en 3D girando la pieza: clic para poner o
   quitar, **Shift + arrastrar** recuadro, **👁** por grupo, **H** oculta la pieza bajo el mouse, **Aislar este grupo**,
   **Mostrar todo** (Shift+H) y **🔒** por grupo (sincronizado con la ventana).
3. A cada grupo le das su movimiento:

   | Movimiento | Qué hace | Ajustes |
   |---|---|---|
   | **Bisagra** | Empieza acostado y se levanta, girando sobre su borde | *Se abre hacia* (el lado al que queda acostado; la bisagra se pone sola en ese borde), *Borde del doblez* (abajo: paredes · arriba: tapas), *Ángulo* (90 = acostado) |
   | **Entrar** | Llega desde un lado hasta su lugar | *Llega desde*, *Distancia (cm)*, *Aparece al empezar* (antes no se ve) |
   | **Girar** | Da vueltas sobre su eje vertical | *Ángulo* (360 = una vuelta) |

   **Se mueve con** (movimiento encadenado): el grupo viaja pegado a otro y su propio movimiento se suma
   encima. Ejemplo: espaldar con bisagra abajo que empieza acostado (paso 1) y laterales que «se mueven con» el
   espaldar, con bisagra vertical en el borde que lo toca (paso 2): al inicio los tres están acostados juntos,
   el espaldar se levanta con los laterales cerrados y después los laterales se abren.

   Y su tiempo: *Duración*, *Empieza* (**después del anterior** = paso nuevo · **junto con el anterior** =
   a la vez, como las dos paredes) y *Retraso* (para escalonar filas). Abajo, los tiempos generales:
   quieto al inicio, pausa entre pasos y quieto al final.
4. **▶ Vista previa en vivo** → abre la pieza en tu navegador y reproduce el movimiento. Déjala abierta al lado:
   cada cambio en la ventana (piezas, lado de la bisagra, duración…) se ve allá en menos de un segundo, sin
   Blender. Marca con una línea la bisagra del grupo elegido, y **Solo el grupo elegido** repite solo ese paso.
   **✎ Editar movimiento** (arriba a la derecha de la vista previa) abre el **gumball**, para lo que los menús
   no alcanzan:
   - **Bisagra por 2 puntos:** clic en dos puntos del doblez (se pegan al vértice más cercano; con Alt, al punto
     exacto). Sirve para cualquier bisagra: vertical en el borde del espaldar, inclinada, en una tapa…
   - **Mover** (W) y **Girar eje** (E): ajustan la bisagra con el gumball. **Vertical / Izq–Der / Frente–Atrás**
     ponen el eje recto de un clic.
   - **Dónde empieza:** el deslizador muestra la pieza en su posición de inicio; muévelo hasta dejarla donde
     quieres que arranque (p. ej. el lateral metido casi tocando el espaldar). **Invertir sentido** la gira al otro lado.
   - En piezas que **entran**, el gumball mueve su **punto de partida**.
   - **▶ Probar este paso** lo reproduce una vez. Todo se guarda solo en Flujo AR; en la ventana aparece
     «ajustada con el gumball · volver a automática».
5. **Generar animación** → la animación final en Blender (.blend, GLB, USDZ). Abajo a la derecha aparece una
   imagen al final de cada paso.
6. **Preparar prueba en celulares** → prepara `pruebas\<pieza>\` (comprimida) y la abre en tu navegador. Para
   probar el AR en los celulares, publícala (paso 6) y escanea el QR que aparece junto a los botones.

Todo se guarda solo en `001_ANIMACIÓN\<Pieza>\` (la receta en `animacion.json`): al volver al paso 3 con la
misma pieza, retomas donde quedaste. **Abrir en Blender** abre el archivo animado para retoques finos.

> Por ahora la animación se prueba en su página aparte (`pruebas\…`). Los QR de campaña siguen mostrando
> la pieza estática hasta que llevemos la animación al visor.

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
| `herramientas\FlujoAR\Animador.cs` | El animador del paso 3 (pieza animada): vistas para hacer clic, grupos, movimientos |
| `scripts\animador.py` | Motor del animador dentro de Blender: `analizar` (vistas y mapa de piezas) y `generar` (animación, .blend, GLB, USDZ, imágenes) |
| `herramientas\vista-animador\index.html` | Vista previa en vivo (three.js): reproduce `animacion.json` con la misma lógica que `animador.py` y se rearma cuando cambia |
| `scripts\preparar-prueba-animacion.ps1` | Comprime la animación y arma `pruebas\<pieza>\` desde `herramientas\plantillas\prueba-animacion.html` |

Para cambiar la dirección de los QR (por ejemplo, con un dominio propio), edita `sitio.json`. Flujo AR y el optimizador la leen de ahí.

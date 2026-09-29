# Campañas: la carpeta manda

Cada **carpeta dentro de `models/` es una campaña**, y cada **subcarpeta es una pieza**. No hay que configurar nada: Flujo AR (y `optimizar.bat`) arman la lista de campañas a partir de las carpetas.

```
models/
├── Campaña Alpina/                ← campaña con 2 piezas → QR de colección
│   ├── _campana.json              ← enlace fijo y orden de las piezas (lo crea Flujo AR)
│   ├── Exhibidor01/
│   │   ├── Exhibidor01.glb        ← optimizado (se publica)
│   │   └── _original/             ← exportación de Rhino (no se sube a GitHub)
│   └── Bandeja01/
│       └── Bandeja01.glb
└── Campaña Nestlé/                ← campaña con 1 pieza → QR de esa pieza
    └── Rompetrafico01/
        └── Rompetrafico01.glb
```

## Cómo se comporta el QR

| La campaña tiene… | El QR de la campaña muestra… |
|---|---|
| **1 pieza** | Esa pieza, sin flechas |
| **2 o más piezas** | Todas: el cliente cambia de pieza con las flechas ‹ › o deslizando la barra inferior |

**El QR impreso nunca hay que cambiarlo.** Si una campaña empieza con 1 pieza y después le agregas otra, el mismo QR ya muestra las dos.

Además, en el paso 7 de Flujo AR puedes sacar el **QR de una sola pieza** de una campaña ("Qué mostrar" → "Solo: Bandeja01").

## Enlaces

| QR | Enlace |
|---|---|
| Campaña completa | `…/?coleccion=campana-alpina` |
| Una pieza | `…/?coleccion=campana-alpina&pieza=bandeja01` |

- El enlace de la campaña se crea solo a partir del nombre de la carpeta: sin tildes, sin ñ y con guiones (`Campaña Alpina` → `campana-alpina`).
- **Se crea una sola vez y queda guardado en `_campana.json`**. Si después renombras la carpeta, el enlace (y el QR impreso) no cambia.
- No borres `_campana.json`: si lo borras, el enlace se vuelve a calcular con el nombre actual de la carpeta y los QR ya impresos podrían dejar de funcionar.

## Nombres

- Campañas y piezas pueden tener tildes, ñ y espacios: `Campaña Alpina`, `Bandeja 01`.
- No pueden empezar con `_` ni con punto (esas carpetas se ignoran) ni tener `\ / : * ? " < > |`.
- El `.glb` de cada pieza se llama igual que su carpeta: `Bandeja01/Bandeja01.glb`. Flujo AR lo hace así automáticamente.

## Orden de las piezas

Las piezas se muestran en el orden en que las fuiste agregando. Para cambiarlo: Flujo AR → paso 5 **Campañas** → selecciona la pieza → **↑ Subir** / **↓ Bajar**. El orden se guarda en `_campana.json`.

## Luz de la escena

Flujo AR → paso 4 → barra **Luz de la escena** → **Abrir en el navegador** para ver el cambio → **Guardar luz para esta campaña**. Se guarda en `_campana.json` y se aplica al QR después de publicar. Si no guardas nada, el visor usa 0,80.

## Quitar una pieza o una campaña

Borra su carpeta en `models/` (botón **Abrir carpeta** del paso 5) y pulsa **Volver a leer carpetas**. Luego publica con GitHub Desktop. Si quitas todas las piezas de una campaña, su QR deja de funcionar.

## `colecciones.json`

Es la lista de campañas que usa el visor en internet. **Se genera sola, no la edites a mano**: cada vez que optimizas una pieza o abres los pasos 4 a 7 de Flujo AR se vuelve a crear a partir de las carpetas. Tiene que subirse a GitHub junto con las piezas.

// Genera colecciones.json a partir de las carpetas de models/:
//
//   models/<Campaña>/<Pieza>/<Pieza>.glb
//
// Cada carpeta raíz es una campaña; cada subcarpeta con su .glb es una pieza.
// El enlace de cada campaña (lo que va en el QR) se guarda la primera vez en
// models/<Campaña>/_campana.json, así renombrar la carpeta no rompe los QR impresos.
// En ese mismo archivo se guarda el orden de las piezas.
//
// Uso: node scripts/generar-colecciones.js      (escribe colecciones.json e imprime un resumen)

const fs = require('fs');
const path = require('path');

const proyecto = path.resolve(__dirname, '..');
const carpetaModels = path.join(proyecto, 'models');
const salida = path.join(proyecto, 'colecciones.json');

// "Campaña Alpina 2026" → "campana-alpina-2026"
function enlaceDe(nombre) {
  return nombre
    .normalize('NFD').replace(/[̀-ͯ]/g, '')   // quita tildes y la virgulilla de la ñ
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '') || 'campana';
}

function subcarpetas(dir) {
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true })
    .filter(d => d.isDirectory() && !d.name.startsWith('_') && !d.name.startsWith('.'))
    .map(d => d.name)
    .sort((a, b) => a.localeCompare(b, 'es', { numeric: true }));
}

function leerJson(ruta) {
  try { return JSON.parse(fs.readFileSync(ruta, 'utf8')); } catch { return null; }
}

const campanas = [];
const avisos = [];
const enlacesUsados = new Set();

for (const carpeta of subcarpetas(carpetaModels)) {
  const dirCampana = path.join(carpetaModels, carpeta);

  const piezas = subcarpetas(dirCampana).filter(p => {
    const ok = fs.existsSync(path.join(dirCampana, p, p + '.glb'));
    if (!ok) avisos.push(`"${carpeta}/${p}" no tiene ${p}.glb: se ignora`);
    return ok;
  });
  const sueltos = fs.readdirSync(dirCampana).filter(f => f.toLowerCase().endsWith('.glb'));
  if (sueltos.length) avisos.push(`"${carpeta}" tiene .glb sueltos (${sueltos.join(', ')}): cada pieza debe ir en su propia subcarpeta`);
  if (!piezas.length) continue;

  // Enlace fijo y orden guardados en _campana.json
  const rutaConfig = path.join(dirCampana, '_campana.json');
  const config = leerJson(rutaConfig) || {};
  let enlace = config.enlace;
  if (!enlace || enlacesUsados.has(enlace)) {
    const base = enlaceDe(carpeta);
    enlace = base;
    for (let n = 2; enlacesUsados.has(enlace); n++) enlace = `${base}-${n}`;
  }
  enlacesUsados.add(enlace);

  // Orden: primero las del orden guardado que sigan existiendo, luego las nuevas (alfabético)
  const orden = (Array.isArray(config.orden) ? config.orden : []).filter(p => piezas.includes(p));
  for (const p of piezas) if (!orden.includes(p)) orden.push(p);

  // Luz de la escena elegida en Flujo AR (paso 4); si no hay, el visor usa su valor por defecto
  const exposicion = typeof config.exposicion === 'number' ? config.exposicion : undefined;

  const nuevaConfig = { enlace, orden, exposicion };
  if (JSON.stringify(nuevaConfig) !== JSON.stringify({ enlace: config.enlace, orden: config.orden, exposicion: config.exposicion })) {
    fs.writeFileSync(rutaConfig, JSON.stringify(nuevaConfig, null, 2) + '\n', 'utf8');
  }

  const idsUsados = new Set();
  const modelos = orden.map(p => {
    let id = enlaceDe(p);
    for (let n = 2; idsUsados.has(id); n++) id = `${enlaceDe(p)}-${n}`;
    idsUsados.add(id);
    return { id, nombre: p, archivo: `models/${carpeta}/${p}/${p}.glb` };
  });

  campanas.push({ enlace, titulo: carpeta, carpeta, exposicion, modelos });
}

campanas.sort((a, b) => a.titulo.localeCompare(b.titulo, 'es', { numeric: true }));

const resultado = {};
for (const c of campanas) resultado[c.enlace] = { titulo: c.titulo, carpeta: c.carpeta, exposicion: c.exposicion, modelos: c.modelos };
fs.writeFileSync(salida, JSON.stringify(resultado, null, 2) + '\n', 'utf8');

console.log(JSON.stringify({
  campanas: campanas.map(c => ({ enlace: c.enlace, titulo: c.titulo, piezas: c.modelos.length })),
  avisos,
}));

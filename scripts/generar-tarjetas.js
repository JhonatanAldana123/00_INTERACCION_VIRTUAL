// Crea la página de cada «dirección visible» de tarjetas.json (innerciaar.com/Ramo/Exh).
// El QR impreso siempre abre /t/<n>; la experiencia muestra en la barra la dirección de la tarjeta,
// y estas páginas hacen que esa dirección también funcione si alguien la recarga o la comparte.
//   node scripts/generar-tarjetas.js        (o doble clic en ActualizarTarjetas.bat)
const fs = require('fs');
const path = require('path');

const RAIZ = path.resolve(__dirname, '..');
const MARCA = '<!-- tarjeta-ar: página generada por scripts/generar-tarjetas.js -->';
const RESERVADAS = new Set(['t', 'models', 'docs', 'scripts', 'herramientas', 'pruebas', 'qrs', 'node_modules', '.git', '.claude']);
const rutaManifiesto = path.join(RAIZ, '_tarjetas-paginas.json');
const leer = (r, defecto) => { try { return JSON.parse(fs.readFileSync(r, 'utf8').replace(/^﻿/, '')); } catch { return defecto; } };

const tarjetas = leer(path.join(RAIZ, 'tarjetas.json'), null);
if (!tarjetas) { console.error('No se pudo leer tarjetas.json (revisa comas y comillas).'); process.exit(1); }
const colecciones = leer(path.join(RAIZ, 'colecciones.json'), {});
const anteriores = leer(rutaManifiesto, { carpetas: [] }).carpetas || [];
const generadas = [];
let errores = 0;
const error = t => { errores++; console.error('  ✗ ' + t); };

// Windows no distingue mayúsculas en las carpetas, pero la web sí: se exige el nombre tal como está en el disco
function nombreReal(carpetaPadre, nombre) {
  if (!fs.existsSync(carpetaPadre)) return nombre;
  return fs.readdirSync(carpetaPadre).find(x => x.toLowerCase() === nombre.toLowerCase()) || nombre;
}

for (const n of Object.keys(tarjetas).filter(k => /^\d+$/.test(k))) {
  const t = tarjetas[n];
  console.log(`Tarjeta ${n}: ${t.nombre || '(sin nombre)'}`);
  if (!fs.existsSync(path.join(RAIZ, 't', n, 'index.html'))) error(`falta la página fija t/${n}/ (el QR de esa tarjeta no abriría)`);
  const col = colecciones[t.coleccion];
  if (!col) error(`la campaña "${t.coleccion}" no existe en colecciones.json`);
  else if (t.pieza && !col.modelos.some(m => m.id === t.pieza)) error(`la campaña "${t.coleccion}" no tiene la pieza "${t.pieza}"`);
  if (!t.direccion) { console.log('  (sin dirección propia: se verá /pruebas/tarjeta/)'); continue; }

  const partes = String(t.direccion).split('/').map(p => p.trim()).filter(Boolean);
  if (!partes.length || partes.some(p => !/^[A-Za-z0-9_-]+$/.test(p))) { error(`dirección "${t.direccion}": usa solo letras sin tilde, números, - y _ (sin espacios)`); continue; }
  if (RESERVADAS.has(partes[0].toLowerCase())) { error(`dirección "${t.direccion}": "${partes[0]}" es una carpeta del sitio, usa otro nombre`); continue; }

  let carpeta = RAIZ, real = [];
  for (const p of partes) { const r = nombreReal(carpeta, p); real.push(r); carpeta = path.join(carpeta, r); }
  if (real.join('/') !== partes.join('/')) { error(`dirección "${t.direccion}": ya existe una carpeta con otras mayúsculas; escríbela así: "${real.join('/')}"`); continue; }
  const destino = path.join(carpeta, 'index.html');
  if (fs.existsSync(destino) && !fs.readFileSync(destino, 'utf8').includes(MARCA)) { error(`dirección "${t.direccion}": ya la usa el visor normal, elige otra`); continue; }
  if (generadas.includes(partes.join('/'))) { error(`dirección "${t.direccion}": está repetida en otra tarjeta`); continue; }

  fs.mkdirSync(carpeta, { recursive: true });
  fs.writeFileSync(destino, `<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <title>${String(t.nombre || 'Innercia').replace(/[<>&]/g, '')} · Innercia AR</title>
  ${MARCA}
  <script>location.replace('/pruebas/tarjeta/?t=${n}' + location.search.replace('?', '&'));</script>
</head>
<body><a href="/pruebas/tarjeta/?t=${n}">Abrir la tarjeta</a></body>
</html>
`, 'utf8');
  generadas.push(partes.join('/'));
  console.log(`  ✓ innerciaar.com/${partes.join('/')}`);
}

// Direcciones que ya no usa ninguna tarjeta
for (const vieja of anteriores.filter(a => !generadas.includes(a))) {
  const dir = path.join(RAIZ, vieja), pag = path.join(dir, 'index.html');
  try {
    if (fs.existsSync(pag) && fs.readFileSync(pag, 'utf8').includes(MARCA)) { fs.rmSync(pag); console.log(`  – se quitó ${vieja}`); }
    for (let d = dir; d !== RAIZ && fs.existsSync(d) && !fs.readdirSync(d).length; d = path.dirname(d)) fs.rmdirSync(d);
  } catch { /* si no se puede borrar, queda como estaba */ }
}
fs.writeFileSync(rutaManifiesto, JSON.stringify({ carpetas: generadas }, null, 2) + '\n', 'utf8');

console.log(errores ? `\nHay ${errores} cosa(s) por corregir en tarjetas.json.` : '\nListo. Ahora publica con GitHub Desktop.');
process.exit(errores ? 1 : 0);

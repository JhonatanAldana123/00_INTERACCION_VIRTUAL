// Servidor local para trabajar la tarjeta AR (solo en este computador).
// Sirve el proyecto en http://localhost:8791 y deja que las páginas de herramientas
// guarden archivos generados en el navegador (el reconocimiento .mind y el video de relleno)
// dentro de pruebas/tarjeta/.
//   node scripts/servidor-tarjeta.js
const http = require('http');
const fs = require('fs');
const path = require('path');

const RAIZ = path.resolve(__dirname, '..');
const PUERTO = 8791;
const DESTINO = path.join(RAIZ, 'pruebas', 'tarjeta');
const PERMITIDOS = /^[a-z0-9_-]+\.(mind|mp4|webm|png|svg)$/i;
const TIPOS = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.json': 'application/json; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml',
  '.glb': 'model/gltf-binary', '.usdz': 'model/vnd.usdz+zip', '.mp4': 'video/mp4', '.webm': 'video/webm',
  '.mind': 'application/octet-stream', '.wasm': 'application/wasm',
};

http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');

  if (req.method === 'POST' && url.pathname === '/__tarjeta/guardar') {
    const nombre = url.searchParams.get('archivo') || '';
    if (!PERMITIDOS.test(nombre)) { res.writeHead(400); res.end('nombre no permitido'); return; }
    const partes = [];
    req.on('data', d => partes.push(d));
    req.on('end', () => {
      const datos = Buffer.concat(partes);
      fs.writeFileSync(path.join(DESTINO, nombre), datos);
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ archivo: nombre, bytes: datos.length }));
    });
    return;
  }

  let ruta = path.normalize(path.join(RAIZ, decodeURIComponent(url.pathname)));
  if (!ruta.startsWith(RAIZ)) { res.writeHead(403); res.end(); return; }
  if (fs.existsSync(ruta) && fs.statSync(ruta).isDirectory()) ruta = path.join(ruta, 'index.html');
  if (!fs.existsSync(ruta)) { res.writeHead(404); res.end('no existe'); return; }

  const total = fs.statSync(ruta).size;
  const cabeceras = { 'Content-Type': TIPOS[path.extname(ruta).toLowerCase()] || 'application/octet-stream',
                      'Cache-Control': 'no-store', 'Accept-Ranges': 'bytes' };
  // Los videos se piden por tramos (Range): sin esto Safari no los reproduce
  const rango = /^bytes=(\d*)-(\d*)$/.exec(req.headers.range || '');
  if (rango) {
    const ini = rango[1] ? parseInt(rango[1], 10) : 0;
    const fin = rango[2] ? Math.min(parseInt(rango[2], 10), total - 1) : total - 1;
    res.writeHead(206, { ...cabeceras, 'Content-Range': `bytes ${ini}-${fin}/${total}`, 'Content-Length': fin - ini + 1 });
    fs.createReadStream(ruta, { start: ini, end: fin }).pipe(res);
  } else {
    res.writeHead(200, { ...cabeceras, 'Content-Length': total });
    fs.createReadStream(ruta).pipe(res);
  }
}).listen(PUERTO, () => console.log(`Tarjeta AR: http://localhost:${PUERTO}/pruebas/tarjeta/`));

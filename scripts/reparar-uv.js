// Repara el mapeo (UV) de copias de un objeto.
//
// El exportador glTF de Rhino a veces escribe el mapeo de textura solo en uno de los objetos
// copiados (por ejemplo, una de 20 botellas iguales). Las demás quedan con la textura asignada
// pero sin coordenadas UV, y se ven de un solo color.
//
// Si un objeto sin UV tiene exactamente la misma malla (misma imagen de textura, mismos vértices
// y mismos triángulos, en el mismo orden) que otro que sí tiene UV, se le asigna ese mismo mapeo.
// No se modifica la geometría: solo se enlaza el mapeo existente.
//
// Uso: node scripts/reparar-uv.js entrada.glb salida.glb
//      Imprime {"reparados": N, "sinReparar": M}

const fs = require('fs');
const [entrada, salida] = process.argv.slice(2);

const b = fs.readFileSync(entrada);
const largoJson = b.readUInt32LE(12);
const gltf = JSON.parse(b.slice(20, 20 + largoJson).toString());
const inicioBin = 20 + largoJson;
const bin = b.slice(inicioBin + 8, inicioBin + 8 + b.readUInt32LE(inicioBin));

const TAMANO = { 5121: 1, 5123: 2, 5125: 4, 5126: 4 };
const COMPONENTES = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4 };

// Bytes de un accessor (para comparar los triángulos de dos objetos)
function bytes(indice) {
  const a = gltf.accessors[indice];
  const v = gltf.bufferViews[a.bufferView];
  const largo = a.count * COMPONENTES[a.type] * TAMANO[a.componentType];
  const inicio = (v.byteOffset || 0) + (a.byteOffset || 0);
  return bin.slice(inicio, inicio + largo);
}

function tieneTextura(p) {
  const m = (gltf.materials || [])[p.material];
  return m && m.pbrMetallicRoughness && m.pbrMetallicRoughness.baseColorTexture;
}

// Rhino puede crear un material y una copia de la imagen por objeto aunque sean iguales:
// se compara el contenido de la imagen (huella SHA-1), no su número
const huellas = {};
function imagenDe(p) {
  const tex = gltf.textures[tieneTextura(p).index];
  if (!tex || tex.source == null) return null;
  if (!(tex.source in huellas)) {
    const img = gltf.images[tex.source];
    const v = img.bufferView != null ? gltf.bufferViews[img.bufferView] : null;
    huellas[tex.source] = v
      ? require('crypto').createHash('sha1').update(bin.slice(v.byteOffset || 0, (v.byteOffset || 0) + v.byteLength)).digest('hex')
      : 'uri:' + img.uri;
  }
  return huellas[tex.source];
}

const primitivas = (gltf.meshes || []).flatMap(m => m.primitives);
const donantes = primitivas.filter(p => tieneTextura(p) && p.attributes.TEXCOORD_0 != null && p.indices != null);
const rotas = primitivas.filter(p => tieneTextura(p) && p.attributes.TEXCOORD_0 == null);

let reparados = 0;
for (const p of rotas) {
  const vertices = gltf.accessors[p.attributes.POSITION].count;
  const triangulos = p.indices != null ? bytes(p.indices) : null;
  const donante = triangulos && donantes.find(d =>
    imagenDe(d) === imagenDe(p) &&
    gltf.accessors[d.attributes.POSITION].count === vertices &&
    bytes(d.indices).equals(triangulos));
  if (donante) {
    p.attributes.TEXCOORD_0 = donante.attributes.TEXCOORD_0;
    reparados++;
  }
}

// Quita líneas y puntos sueltos (curvas o bordes exportados desde Rhino): el AR de Android y de
// iPhone solo acepta superficies (triángulos) y rechaza el archivo completo si encuentra una línea.
const TRIANGULOS = new Set([4, 5, 6]);   // triángulos, tira y abanico
let lineasQuitadas = 0;
const nuevoIndice = [];
const mallas = [];
(gltf.meshes || []).forEach((m, i) => {
  const antes = m.primitives.length;
  m.primitives = m.primitives.filter(p => TRIANGULOS.has(p.mode == null ? 4 : p.mode));
  lineasQuitadas += antes - m.primitives.length;
  if (m.primitives.length) { nuevoIndice[i] = mallas.length; mallas.push(m); }
});
if (lineasQuitadas) {
  gltf.meshes = mallas;
  for (const n of gltf.nodes || []) {
    if (n.mesh == null) continue;
    if (nuevoIndice[n.mesh] == null) delete n.mesh;   // la malla solo tenía líneas: el nodo queda vacío
    else n.mesh = nuevoIndice[n.mesh];
  }
}

// ── Vidrios y materiales de Rhino que el AR no entiende ─────────────────────────────────────────
// Rhino 8 exporta TODOS los materiales con las extensiones de «vidrio físico» (transmisión, barniz, índice de
// refracción), casi siempre en cero, y el vidrio de verdad con transmisión = 1 y color base negro.
// Esa transmisión solo la dibuja bien el visor 3D de la página. El AR (el del navegador, la app de Google y el de
// iPhone) no la conoce y pinta el vidrio como una placa NEGRA y opaca, que tapa lo que hay detrás (pasó con el
// exhibidor de Rexona, 16/11/2026). Además obliga al celular a dibujar la escena dos veces.
// Aquí se cambia por la transparencia sencilla que sí entienden todos: color claro + opacidad baja.
//   · vidrio (transmisión > 0)            → transparente, 12 % de opacidad, liso, con su color si lo tenía
//   · calado (transmisión con una imagen) → semitransparente parejo (el dibujo del calado no se puede conservar)
//   · todo lo demás                       → se le quitan esas extensiones, que estaban en cero
let vidrios = 0, calados = 0, caladosParejos = 0;
const nombresVidrio = [];
const agregados = [];          // imágenes nuevas (máscaras de calado) que se suman al bloque binario

// ── Calados: mallas, rejillas y láminas perforadas ──────────────────────────────────────────────
// En Rhino el calado es una imagen que dice dónde pasa la luz (canal rojo: blanco = hueco, negro = material).
// Se convierte en una imagen de recorte (blanca, con transparencia donde hay hueco) que se usa como color base
// con alphaMode MASK: eso sí lo entienden el visor y el AR, y la pieza se ve calada de verdad.
// Lector y escritor de PNG mínimos (8 bits, sin entrelazado), para no depender de nada instalado.
const zlib = require('zlib');
function leerPng(buf) {
  if (buf.readUInt32BE(0) !== 0x89504e47) return null;
  let p = 8, ancho = 0, alto = 0, tipo = 0, bits = 0, entrelazado = 0;
  const datos = [];
  while (p < buf.length) {
    const largo = buf.readUInt32BE(p), nombre = buf.toString('ascii', p + 4, p + 8), d = buf.slice(p + 8, p + 8 + largo);
    if (nombre === 'IHDR') { ancho = d.readUInt32BE(0); alto = d.readUInt32BE(4); bits = d[8]; tipo = d[9]; entrelazado = d[12]; }
    if (nombre === 'IDAT') datos.push(d);
    if (nombre === 'IEND') break;
    p += 12 + largo;
  }
  const canales = { 0: 1, 2: 3, 4: 2, 6: 4 }[tipo];
  if (bits !== 8 || entrelazado || !canales) return null;
  const crudo = zlib.inflateSync(Buffer.concat(datos)), paso = ancho * canales, pix = Buffer.alloc(alto * paso);
  for (let y = 0; y < alto; y++) {
    const filtro = crudo[y * (paso + 1)], e = y * (paso + 1) + 1, s = y * paso;
    for (let x = 0; x < paso; x++) {
      const a = x >= canales ? pix[s + x - canales] : 0, b = y ? pix[s + x - paso] : 0, c = x >= canales && y ? pix[s + x - paso - canales] : 0;
      let v = crudo[e + x];
      if (filtro === 1) v += a; else if (filtro === 2) v += b; else if (filtro === 3) v += (a + b) >> 1;
      else if (filtro === 4) { const pa = Math.abs(b - c), pb = Math.abs(a - c), pc = Math.abs(a + b - 2 * c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
      pix[s + x] = v & 255;
    }
  }
  return { ancho, alto, canales, pix };
}
const tablaCrc = Array.from({ length: 256 }, (_, n) => { for (let k = 0; k < 8; k++) n = n & 1 ? 0xedb88320 ^ (n >>> 1) : n >>> 1; return n >>> 0; });
function trozoPng(nombre, d) {
  const cab = Buffer.alloc(8); cab.writeUInt32BE(d.length, 0); cab.write(nombre, 4, 'ascii');
  let c = 0xffffffff; for (const v of Buffer.concat([cab.slice(4), d])) c = tablaCrc[(c ^ v) & 255] ^ (c >>> 8);
  const fin = Buffer.alloc(4); fin.writeUInt32BE((c ^ 0xffffffff) >>> 0, 0);
  return Buffer.concat([cab, d, fin]);
}
// Máscara de recorte de «lado» píxeles: blanca, con opacidad = 1 − transmisión (promediando el canal rojo)
function mascaraDeCalado(png, lado = 512) {
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(lado, 0); ihdr.writeUInt32BE(lado, 4); ihdr[8] = 8; ihdr[9] = 6;
  const filas = Buffer.alloc(lado * (lado * 4 + 1));
  const bx = png.ancho / lado, by = png.alto / lado;
  for (let y = 0; y < lado; y++) {
    for (let x = 0; x < lado; x++) {
      let suma = 0, n = 0;
      for (let j = Math.floor(y * by); j < Math.max(Math.floor(y * by) + 1, Math.floor((y + 1) * by)); j += Math.max(1, Math.floor(by / 4)))
        for (let i = Math.floor(x * bx); i < Math.max(Math.floor(x * bx) + 1, Math.floor((x + 1) * bx)); i += Math.max(1, Math.floor(bx / 4))) { suma += png.pix[(j * png.ancho + i) * png.canales]; n++; }
      const o = y * (lado * 4 + 1) + 1 + x * 4;
      filas[o] = filas[o + 1] = filas[o + 2] = 255;
      filas[o + 3] = 255 - Math.round(suma / n);
    }
  }
  return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), trozoPng('IHDR', ihdr), trozoPng('IDAT', zlib.deflateSync(filas, { level: 9 })), trozoPng('IEND', Buffer.alloc(0))]);
}
// Devuelve el índice de una textura nueva con la máscara, o null si la imagen del calado no se puede leer
function texturaDeCalado(tt) {
  try {
    const tex = gltf.textures[tt.index], img = gltf.images[tex.source], v = gltf.bufferViews[img.bufferView];
    if (!v || img.mimeType !== 'image/png') return null;
    const png = leerPng(bin.slice(v.byteOffset || 0, (v.byteOffset || 0) + v.byteLength));
    if (!png) return null;
    agregados.push(mascaraDeCalado(png));
    gltf.images.push({ name: 'calado-mascara-' + agregados.length, mimeType: 'image/png', bufferView: -agregados.length });     // se enlaza al escribir
    gltf.textures.push({ source: gltf.images.length - 1, ...(tex.sampler != null ? { sampler: tex.sampler } : {}) });
    return gltf.textures.length - 1;
  } catch (e) { return null; }
}
for (const m of gltf.materials || []) {
  const ext = m.extensions;
  if (!ext) continue;
  const t = ext.KHR_materials_transmission;
  const factor = t ? (t.transmissionFactor || 0) : 0;
  if (t && factor > 0) {
    const pbr = m.pbrMetallicRoughness = m.pbrMetallicRoughness || {};
    const c = pbr.baseColorFactor || [1, 1, 1, 1];
    const oscuro = Math.max(c[0], c[1], c[2]) < 0.08;            // negro = vidrio incoloro en Rhino
    // Con 18 % de opacidad y un tinte casi blanco se veía «lechoso» (16/11/2026). El vidrio real casi no se ve:
    // 7 % de opacidad, un tinte gris azulado (no blanco) y liso del todo, para que solo quede el brillo.
    // Con 7 % casi no se veía. Queda en 12 % con tinte azulado: es lo que muestra el AR. En el visor 3D de la
    // página (index.html, vidrioReal) este material se cambia por vidrio de verdad, con reflejos; por eso se
    // marca con «extras.vidrio».
    const tinte = oscuro ? [0.66, 0.82, 0.9] : [c[0], c[1], c[2]];
    if (t.transmissionTexture) {
      const indice = pbr.baseColorTexture ? null : texturaDeCalado(t.transmissionTexture);
      if (indice != null) {
        // Calado de verdad: recorte con la misma repetición que tenía el patrón en Rhino
        pbr.baseColorTexture = { index: indice, ...(t.transmissionTexture.texCoord != null ? { texCoord: t.transmissionTexture.texCoord } : {}),
                                 ...(t.transmissionTexture.extensions ? { extensions: t.transmissionTexture.extensions } : {}) };
        pbr.baseColorFactor = [c[0], c[1], c[2], 1];
        m.alphaMode = 'MASK'; m.alphaCutoff = 0.5; m.doubleSided = true;
        calados++;
        nombresVidrio.push((m.name || 'sin nombre').replace(/^\//, ''));
        delete ext.KHR_materials_transmission; delete ext.KHR_materials_ior; delete ext.KHR_materials_volume;
        if (ext.KHR_materials_clearcoat && !(ext.KHR_materials_clearcoat.clearcoatFactor > 0)) delete ext.KHR_materials_clearcoat;
        if (!Object.keys(ext).length) delete m.extensions;
        continue;
      }
      pbr.baseColorFactor = [...(oscuro ? [0.86, 0.93, 0.97] : tinte), 0.55]; caladosParejos++;      // no se pudo leer el patrón: semitransparente parejo
    }
    else {
      pbr.baseColorFactor = [...tinte, Math.max(0.12, 1 - 0.88 * factor)]; pbr.roughnessFactor = 0; vidrios++;
      m.extras = { ...(m.extras || {}), vidrio: true };
      ext.KHR_materials_specular = { specularFactor: 1 };       // brillo completo (y mantiene el material «físico» que necesita el visor)
    }
    pbr.metallicFactor = t.transmissionTexture ? (pbr.metallicFactor ?? 1) : 0;
    m.alphaMode = 'BLEND';
    m.doubleSided = true;
    nombresVidrio.push((m.name || 'sin nombre').replace(/^\//, ''));
  }
  delete ext.KHR_materials_transmission;
  delete ext.KHR_materials_ior;
  delete ext.KHR_materials_volume;
  const barniz = ext.KHR_materials_clearcoat;
  if (barniz && !(barniz.clearcoatFactor > 0)) delete ext.KHR_materials_clearcoat;
  if (!Object.keys(ext).length) delete m.extensions;
}
// La lista de extensiones del archivo solo debe nombrar las que siguen en uso
if (gltf.extensionsUsed) {
  const texto = JSON.stringify({ ...gltf, extensionsUsed: undefined, extensionsRequired: undefined });
  gltf.extensionsUsed = gltf.extensionsUsed.filter(e => texto.includes('"' + e + '"'));
  if (!gltf.extensionsUsed.length) delete gltf.extensionsUsed;
}

// Reescribe el .glb: el JSON cambia, el bloque binario queda igual
// Rhino exporta las imágenes con filtro «lineal», sin mipmaps (versiones reducidas): cuando la pieza se ve
// pequeña o lejos, la gráfica chispea y se ve con ruido (se notó en el AR del navegador, 16/11/2026).
// 9987 = lineal con mipmaps: el visor los genera al cargar.
for (const s of gltf.samplers || []) s.minFilter = 9987;

// Las máscaras de calado se suman al final del bloque binario
let resto = b.slice(inicioBin);
if (agregados.length) {
  let datos = bin;
  agregados.forEach((png, k) => {
    if (datos.length % 4) datos = Buffer.concat([datos, Buffer.alloc(4 - (datos.length % 4))]);
    gltf.bufferViews.push({ buffer: 0, byteOffset: datos.length, byteLength: png.length });
    gltf.images.find(i => i.bufferView === -(k + 1)).bufferView = gltf.bufferViews.length - 1;
    datos = Buffer.concat([datos, png]);
  });
  if (datos.length % 4) datos = Buffer.concat([datos, Buffer.alloc(4 - (datos.length % 4))]);
  gltf.buffers[0].byteLength = datos.length;
  const cabBin = Buffer.alloc(8); cabBin.writeUInt32LE(datos.length, 0); cabBin.writeUInt32LE(0x004e4942, 4);
  resto = Buffer.concat([cabBin, datos]);
  if (!gltf.extensionsUsed) gltf.extensionsUsed = [];
}

let json = Buffer.from(JSON.stringify(gltf));
if (json.length % 4) json = Buffer.concat([json, Buffer.alloc(4 - (json.length % 4), 0x20)]);
const cabecera = Buffer.alloc(12);
cabecera.writeUInt32LE(0x46546c67, 0);
cabecera.writeUInt32LE(2, 4);
cabecera.writeUInt32LE(12 + 8 + json.length + resto.length, 8);
const cabJson = Buffer.alloc(8);
cabJson.writeUInt32LE(json.length, 0);
cabJson.writeUInt32LE(0x4e4f534a, 4);
fs.writeFileSync(salida, Buffer.concat([cabecera, cabJson, json, resto]));

console.log(JSON.stringify({ reparados, sinReparar: rotas.length - reparados, lineasQuitadas, vidrios, calados, caladosParejos, nombresVidrio }));

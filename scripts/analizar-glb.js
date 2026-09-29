// Lee un .glb y devuelve un resumen en JSON: peso, triángulos, texturas y materiales.
// Uso: node analizar-glb.js ruta/al/modelo.glb
// No necesita dependencias: lee directamente el bloque JSON del archivo.

const fs = require('fs');

const ruta = process.argv[2];
const b = fs.readFileSync(ruta);
if (b.readUInt32LE(0) !== 0x46546c67) {
  console.error(`"${ruta}" no es un archivo .glb válido`);
  process.exit(1);
}

const jsonLen = b.readUInt32LE(12);
const gltf = JSON.parse(b.slice(20, 20 + jsonLen).toString());
const binStart = 20 + jsonLen + 8;
const views = gltf.bufferViews || [];

// Triángulos: se cuentan por cada nodo que usa una malla
const usos = {};
(gltf.nodes || []).forEach(n => { if (n.mesh != null) usos[n.mesh] = (usos[n.mesh] || 0) + 1; });
let triangulos = 0;
(gltf.meshes || []).forEach((m, i) => {
  m.primitives.forEach(p => {
    const n = p.indices != null
      ? gltf.accessors[p.indices].count / 3
      : gltf.accessors[p.attributes.POSITION].count / 3;
    triangulos += n * (usos[i] || 0);
  });
});

// Texturas: formato, tamaño en píxeles y peso
function medidas(im) {
  const d = b.slice(binStart + (views[im.bufferView].byteOffset || 0));
  if (im.mimeType === 'image/png') return [d.readUInt32BE(16), d.readUInt32BE(20)];
  if (im.mimeType === 'image/jpeg') {
    for (let k = 2; k < d.length && d[k] === 0xFF;) {
      const marca = d[k + 1], largo = d.readUInt16BE(k + 2);
      if (marca >= 0xC0 && marca <= 0xC2) return [d.readUInt16BE(k + 7), d.readUInt16BE(k + 5)];
      k += 2 + largo;
    }
  }
  return [0, 0];
}
const texturas = (gltf.images || []).map(im => {
  const [ancho, alto] = im.bufferView != null ? medidas(im) : [0, 0];
  const bytes = im.bufferView != null ? views[im.bufferView].byteLength : 0;
  return { formato: (im.mimeType || '').replace('image/', ''), ancho, alto, bytes };
});

// Materiales con transparencia: si existen, no se pueden pasar a JPEG
const transparentes = (gltf.materials || [])
  .filter(m => m.alphaMode === 'BLEND' || m.alphaMode === 'MASK')
  .map(m => m.name || '(sin nombre)');

// Objetos sin material en Rhino: el exportador les pone uno negro y 100 % metálico, y se ven oscuros
const trisPorMaterial = {};
(gltf.meshes || []).forEach((m, i) => m.primitives.forEach(p => {
  if (p.material == null) return;
  const n = (p.indices != null ? gltf.accessors[p.indices].count : gltf.accessors[p.attributes.POSITION].count) / 3;
  trisPorMaterial[p.material] = (trisPorMaterial[p.material] || 0) + n * (usos[i] || 0);
}));
const sinMaterial = (gltf.materials || [])
  .map((m, i) => ({ m, i }))
  .filter(({ m }) => {
    const pbr = m.pbrMetallicRoughness || {};
    const color = pbr.baseColorFactor || [1, 1, 1, 1];
    const negro = color.slice(0, 3).every(c => c <= 0.01);
    const metalico = (pbr.metallicFactor == null ? 1 : pbr.metallicFactor) >= 0.99;
    return negro && metalico && !pbr.baseColorTexture;
  })
  .map(({ m, i }) => ({ nombre: m.name || '(sin nombre)', triangulos: trisPorMaterial[i] || 0 }))
  .filter(s => s.triangulos > 0);

// Objetos con textura pero sin coordenadas de mapeo (UV): la imagen no se puede aplicar y se ven de un solo color
let sinUv = 0, trisSinUv = 0;
(gltf.nodes || []).forEach(n => {
  if (n.mesh == null) return;
  gltf.meshes[n.mesh].primitives.forEach(p => {
    const mat = (gltf.materials || [])[p.material] || {};
    const conTextura = mat.pbrMetallicRoughness && mat.pbrMetallicRoughness.baseColorTexture;
    if (conTextura && p.attributes.TEXCOORD_0 == null) {
      sinUv++;
      trisSinUv += (p.indices != null ? gltf.accessors[p.indices].count : gltf.accessors[p.attributes.POSITION].count) / 3;
    }
  });
});

console.log(JSON.stringify({
  bytes: b.length,
  triangulos,
  mallas: (gltf.meshes || []).length,
  materiales: (gltf.materials || []).length,
  transparentes,
  sinMaterial,
  sinUv: { objetos: sinUv, triangulos: trisSinUv },
  texturas,
  bytesTexturas: texturas.reduce((s, t) => s + t.bytes, 0),
  draco: (gltf.extensionsUsed || []).includes('KHR_draco_mesh_compression'),
}));

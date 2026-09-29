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

// Reescribe el .glb: el JSON cambia, el bloque binario queda igual
let json = Buffer.from(JSON.stringify(gltf));
if (json.length % 4) json = Buffer.concat([json, Buffer.alloc(4 - (json.length % 4), 0x20)]);
const resto = b.slice(inicioBin);
const cabecera = Buffer.alloc(12);
cabecera.writeUInt32LE(0x46546c67, 0);
cabecera.writeUInt32LE(2, 4);
cabecera.writeUInt32LE(12 + 8 + json.length + resto.length, 8);
const cabJson = Buffer.alloc(8);
cabJson.writeUInt32LE(json.length, 0);
cabJson.writeUInt32LE(0x4e4f534a, 4);
fs.writeFileSync(salida, Buffer.concat([cabecera, cabJson, json, resto]));

console.log(JSON.stringify({ reparados, sinReparar: rotas.length - reparados, lineasQuitadas }));

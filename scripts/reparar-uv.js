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
// Otras veces sí escribe el mapeo de todas las copias, pero clavado donde estaba el objeto original: cada
// copia muestra un trozo distinto de la imagen. Eso también se corrige aquí (ver «Mapeo que no siguió a las copias»).
//
// Uso: node scripts/reparar-uv.js entrada.glb salida.glb
//      Imprime {"reparados": N, "sinReparar": M, "reanclados": K, ...}

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

// ── Mapeo que no siguió a las copias ────────────────────────────────────────────────────────────
// Otra falla del exportador de Rhino (shipper y bandeja de Zenú, 20/11/2026): el producto se mapea una vez
// (la tapa de la lata, la etiqueta, el empaque de pasta), luego se copia, se mueve o se ajusta de tamaño para
// llenar el exhibidor. En Rhino la imagen viaja con cada copia; en el .glb el mapeo queda clavado donde estaba
// el objeto al mapearlo. Resultado: cada copia muestra un trozo distinto de la imagen, partida por la mitad
// (el logo arriba y el resto abajo) o cayendo en una esquina en blanco (tapas «sin mapeo»).
//
// Cómo se reconoce: el mapeo del objeto es una proyección plana (el UV es una función lineal de la posición),
// ocupa casi una imagen entera (entre 85 % y 105 % en cada sentido) y aun así la imagen le queda partida
// por el borde. Nadie mapea así a propósito. Se vuelve a encajar la imagen completa en el objeto, y lo
// mismo en sus copias idénticas, para que todas queden iguales.
// No se toca: lo que ya cae dentro de la imagen, los patrones que se repiten (más de una imagen por objeto),
// los mapeos cilíndricos o por superficie, ni los materiales con repetición propia en la textura.
function vistaReal(indice, componentes) {
  const a = gltf.accessors[indice];
  if (!a || a.componentType !== 5126 || a.sparse || a.bufferView == null || COMPONENTES[a.type] !== componentes) return null;
  const v = gltf.bufferViews[a.bufferView];
  const paso = v.byteStride || componentes * 4, inicio = (v.byteOffset || 0) + (a.byteOffset || 0);
  return { n: a.count, leer: (i, k) => bin.readFloatLE(inicio + i * paso + k * 4), escribir: (i, k, x) => bin.writeFloatLE(x, inicio + i * paso + k * 4) };
}
// La imagen se usa tal cual (como mucho volteada, que es como la escribe Rhino): sin repetición, giro ni corrimiento
function imagenSinRepeticion(material) {
  const t = material.pbrMetallicRoughness.baseColorTexture;
  if ((t.texCoord || 0) !== 0) return false;
  const x = t.extensions && t.extensions.KHR_texture_transform;
  if (!x) return true;
  const escala = x.scale || [1, 1], corrimiento = x.offset || [0, 0];
  return !x.rotation && (x.texCoord || 0) === 0 && escala.every(e => Math.abs(Math.abs(e) - 1) < 1e-6) &&
    corrimiento.every(c => Math.abs(c - Math.round(c)) < 1e-6);
}
// Mayor diferencia entre el UV real y el mejor ajuste lineal UV = f(posición): ~0 si el mapeo es una proyección plana
function desvioDePlano(puntos) {
  const n = puntos.length, media = [0, 0, 0, 0, 0];
  for (const p of puntos) for (let k = 0; k < 5; k++) media[k] += p[k] / n;
  const C = [[0, 0, 0], [0, 0, 0], [0, 0, 0]], B = [[0, 0, 0], [0, 0, 0]];
  for (const p of puntos) {
    const d = [p[0] - media[0], p[1] - media[1], p[2] - media[2]];
    for (let i = 0; i < 3; i++) {
      for (let j = 0; j < 3; j++) C[i][j] += d[i] * d[j];
      B[0][i] += d[i] * (p[3] - media[3]); B[1][i] += d[i] * (p[4] - media[4]);
    }
  }
  const traza = C[0][0] + C[1][1] + C[2][2];
  if (!(traza > 0)) return Infinity;
  for (let i = 0; i < 3; i++) C[i][i] += traza * 1e-9;      // objetos planos: el sentido sin grosor queda en cero
  let peor = 0;
  for (let eje = 0; eje < 2; eje++) {
    const m = C.map((fila, i) => [...fila, B[eje][i]]);
    for (let i = 0; i < 3; i++) {
      let mayor = i;
      for (let r = i + 1; r < 3; r++) if (Math.abs(m[r][i]) > Math.abs(m[mayor][i])) mayor = r;
      [m[i], m[mayor]] = [m[mayor], m[i]];
      for (let r = 0; r < 3; r++) if (r !== i) { const f = m[r][i] / m[i][i]; for (let c = i; c < 4; c++) m[r][c] -= f * m[i][c]; }
    }
    const x = m.map((fila, i) => fila[3] / fila[i]);
    for (const p of puntos) {
      const calculado = (p[0] - media[0]) * x[0] + (p[1] - media[1]) * x[1] + (p[2] - media[2]) * x[2] + media[3 + eje];
      peor = Math.max(peor, Math.abs(calculado - p[3 + eje]));
    }
  }
  return peor;
}
// Cuánto puede salirse el objeto de la imagen sin que sea esta falla: una gráfica que sobresale un poco y pareja
// por los bordes es un sangrado puesto a propósito (cabezote del shipper de OLIZA: 2 % por lado), no una copia
// corrida. Las copias corridas se salen mucho más: entre 6 % y 50 % en las piezas de Zenú.
const MARGEN = 0.05;
const objetosPorMaterial = new Map();
(gltf.meshes || []).forEach(malla => {
  const grupos = new Map();          // un objeto de Rhino = una malla; puede traer varias caras con el mismo material
  for (const p of malla.primitives) {
    if (p.attributes.TEXCOORD_0 == null || !tieneTextura(p)) continue;
    if (!grupos.has(p.material)) grupos.set(p.material, []);
    grupos.get(p.material).push(p);
  }
  for (const [material, caras] of grupos) {
    if (!imagenSinRepeticion(gltf.materials[material])) continue;
    const puntos = [], mapeos = new Set();
    let legible = true;
    for (const p of caras) {
      const pos = vistaReal(p.attributes.POSITION, 3), uv = vistaReal(p.attributes.TEXCOORD_0, 2);
      if (!pos || !uv || pos.n !== uv.n) { legible = false; break; }
      mapeos.add(p.attributes.TEXCOORD_0);
      for (let i = 0; i < pos.n; i++) puntos.push([pos.leer(i, 0), pos.leer(i, 1), pos.leer(i, 2), uv.leer(i, 0), uv.leer(i, 1)]);
    }
    if (!legible || puntos.length < 3) continue;
    const min = [Infinity, Infinity], max = [-Infinity, -Infinity];
    for (const p of puntos) for (let k = 0; k < 2; k++) { min[k] = Math.min(min[k], p[3 + k]); max[k] = Math.max(max[k], p[3 + k]); }
    const ancho = [max[0] - min[0], max[1] - min[1]];
    if (!ancho.every(a => a >= 0.85 && a <= 1.05)) continue;                 // no es «una imagen por objeto»
    if (desvioDePlano(puntos) > 0.002) continue;                             // no es una proyección plana
    // Partida = se sale de la imagen en la que cae su centro (los números enteros de UV son los bordes de la imagen)
    const partida = [0, 1].some(k => { const borde = Math.floor((min[k] + max[k]) / 2); return Math.max(borde - min[k], max[k] - (borde + 1)) > MARGEN; });
    if (!objetosPorMaterial.has(material)) objetosPorMaterial.set(material, []);
    objetosPorMaterial.get(material).push({ mapeos, min, ancho, partida });
  }
});
let reanclados = 0;
const nombresReanclados = [];
const mapeosHechos = new Set();
for (const [material, objetos] of objetosPorMaterial) {
  const partidos = objetos.filter(o => o.partida);
  if (!partidos.length) continue;
  // También sus copias que por casualidad cayeron dentro de la imagen: si no, quedarían distintas a las demás
  const copias = objetos.filter(o => o.partida || partidos.some(q => Math.abs(q.ancho[0] - o.ancho[0]) < 0.002 && Math.abs(q.ancho[1] - o.ancho[1]) < 0.002));
  for (const o of copias) {
    for (const indice of o.mapeos) {
      if (mapeosHechos.has(indice)) continue;
      mapeosHechos.add(indice);
      const uv = vistaReal(indice, 2), a = gltf.accessors[indice];
      const nuevoMin = [Infinity, Infinity], nuevoMax = [-Infinity, -Infinity];
      for (let i = 0; i < uv.n; i++) for (let k = 0; k < 2; k++) {
        const x = Math.fround(Math.min(1, Math.max(0, (uv.leer(i, k) - o.min[k]) / o.ancho[k])));
        uv.escribir(i, k, x);
        nuevoMin[k] = Math.min(nuevoMin[k], x); nuevoMax[k] = Math.max(nuevoMax[k], x);
      }
      if (a.min) a.min = nuevoMin;
      if (a.max) a.max = nuevoMax;
    }
    reanclados++;
  }
  nombresReanclados.push((gltf.materials[material].name || 'sin nombre').replace(/^\//, ''));
}

// ── Objetos con las caras hacia adentro ─────────────────────────────────────────────────────────
// Rhino dibuja las dos caras de toda superficie, así que un objeto con las normales volteadas se ve bien allá.
// El visor y el AR solo dibujan la cara de afuera: si el objeto viene al revés, esa cara desaparece y se ve
// el interior de la cara del fondo, como si el frente fuera transparente (empaques de pasta Doria del shipper
// de Zenú, 20/11/2026: los 54 venían así).
// Solo se corrige lo que no admite duda: un objeto CERRADO (sin bordes sueltos), con todas sus caras en el
// mismo sentido, cuyo volumen sale negativo. A ese se le voltean los triángulos y las normales. Las láminas
// abiertas no se tocan: en ellas no se puede saber cuál es el lado de afuera.
function leerIndices(indice) {
  const a = gltf.accessors[indice];
  if (!a || a.sparse || a.bufferView == null || !TAMANO[a.componentType] || a.componentType === 5126) return null;
  const v = gltf.bufferViews[a.bufferView], t = TAMANO[a.componentType];
  const paso = v.byteStride || t, inicio = (v.byteOffset || 0) + (a.byteOffset || 0);
  const leer = t === 4 ? i => bin.readUInt32LE(inicio + i * paso) : t === 2 ? i => bin.readUInt16LE(inicio + i * paso) : i => bin[inicio + i * paso];
  const escribir = t === 4 ? (i, x) => bin.writeUInt32LE(x, inicio + i * paso) : t === 2 ? (i, x) => bin.writeUInt16LE(x, inicio + i * paso) : (i, x) => { bin[inicio + i * paso] = x; };
  return { n: a.count, leer, escribir };
}
const alReves = [];                  // objetos por voltear: se decide con todos los datos intactos y se aplica después
(gltf.meshes || []).forEach(malla => {
  const caras = [];
  for (const p of malla.primitives) {
    if ((p.mode == null ? 4 : p.mode) !== 4) continue;                       // líneas y puntos: se quitan más abajo
    const pos = vistaReal(p.attributes.POSITION, 3), idx = p.indices != null ? leerIndices(p.indices) : null;
    if (!pos || !idx || idx.n % 3) return;                                   // algo que no se sabe leer: no se toca el objeto
    caras.push({ p, pos, idx });
  }
  if (!caras.length) return;
  const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
  for (const c of caras) for (let i = 0; i < c.pos.n; i++) for (let k = 0; k < 3; k++) { const x = c.pos.leer(i, k); if (x < min[k]) min[k] = x; if (x > max[k]) max[k] = x; }
  const lado = [max[0] - min[0], max[1] - min[1], max[2] - min[2]], diagonal = Math.hypot(...lado);
  if (!(diagonal > 0)) return;
  // Los vértices en el mismo punto cuentan como uno solo (cada cara de Rhino trae los suyos)
  const paso = diagonal * 1e-5, puntos = new Map(), aristas = new Map();
  const centro = min.map((x, k) => x + lado[k] / 2);
  let volumen = 0;
  for (const c of caras) {
    const numero = new Int32Array(c.pos.n), xyz = new Float64Array(c.pos.n * 3);
    for (let i = 0; i < c.pos.n; i++) {
      for (let k = 0; k < 3; k++) xyz[i * 3 + k] = c.pos.leer(i, k) - centro[k];
      const clave = Math.round(xyz[i * 3] / paso) + ',' + Math.round(xyz[i * 3 + 1] / paso) + ',' + Math.round(xyz[i * 3 + 2] / paso);
      let n = puntos.get(clave);
      if (n == null) { n = puntos.size; puntos.set(clave, n); }
      numero[i] = n;
    }
    for (let t = 0; t < c.idx.n; t += 3) {
      const i = [c.idx.leer(t), c.idx.leer(t + 1), c.idx.leer(t + 2)];
      if (i.some(x => x >= c.pos.n)) return;
      const [a, b, d] = i.map(x => x * 3);
      volumen += (xyz[a] * (xyz[b + 1] * xyz[d + 2] - xyz[b + 2] * xyz[d + 1]) +
                  xyz[a + 1] * (xyz[b + 2] * xyz[d] - xyz[b] * xyz[d + 2]) +
                  xyz[a + 2] * (xyz[b] * xyz[d + 1] - xyz[b + 1] * xyz[d])) / 6;
      for (let e = 0; e < 3; e++) {
        const u = numero[i[e]], w = numero[i[(e + 1) % 3]];
        if (u === w) continue;
        const clave = u < w ? u * 4294967296 + w : w * 4294967296 + u;
        const arista = aristas.get(clave) || [0, 0];                         // [veces usada, sentido acumulado]
        arista[0]++; arista[1] += u < w ? 1 : -1;
        aristas.set(clave, arista);
      }
    }
  }
  if (!aristas.size) return;
  // Cerrado y con las caras en un mismo sentido = cada arista se recorre tantas veces en un sentido como en el
  // otro. Un borde suelto o una cara volteada respecto a su vecina dejan la arista descompensada. (No basta con
  // exigir «dos caras por arista»: en el sello aplastado de un empaque coinciden cuatro, y sigue estando cerrado.)
  let descompensadas = 0;
  for (const arista of aristas.values()) if (arista[1] !== 0) descompensadas++;
  const cerrado = descompensadas / aristas.size < 0.02;
  const caja = Math.max(lado[0], paso) * Math.max(lado[1], paso) * Math.max(lado[2], paso);
  if (cerrado && volumen < -0.01 * caja) alReves.push(caras);
});
let volteados = 0;
const nombresVolteados = new Set(), indicesHechos = new Set(), normalesHechas = new Set(), tangentesHechas = new Set();
for (const caras of alReves) {
  for (const { p, pos, idx } of caras) {
    // ¿Las normales acompañaban a los triángulos (hacia adentro) o ya apuntaban hacia afuera? Se mira antes de voltear
    const nor = p.attributes.NORMAL != null ? vistaReal(p.attributes.NORMAL, 3) : null;
    let conLosTriangulos = 0;
    if (nor && nor.n === pos.n && !normalesHechas.has(p.attributes.NORMAL) && !indicesHechos.has(p.indices)) {
      for (let t = 0; t < idx.n; t += 3) {
        const [a, b, d] = [idx.leer(t), idx.leer(t + 1), idx.leer(t + 2)];
        const e1 = [0, 1, 2].map(k => pos.leer(b, k) - pos.leer(a, k)), e2 = [0, 1, 2].map(k => pos.leer(d, k) - pos.leer(a, k));
        const n = [e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]];
        const s = [0, 1, 2].reduce((suma, k) => suma + n[k] * (nor.leer(a, k) + nor.leer(b, k) + nor.leer(d, k)), 0);
        conLosTriangulos += s > 0 ? 1 : s < 0 ? -1 : 0;
      }
      if (conLosTriangulos > 0) {
        for (let i = 0; i < nor.n; i++) for (let k = 0; k < 3; k++) nor.escribir(i, k, -nor.leer(i, k));
        normalesHechas.add(p.attributes.NORMAL);
        const tan = p.attributes.TANGENT != null && !tangentesHechas.has(p.attributes.TANGENT) ? vistaReal(p.attributes.TANGENT, 4) : null;
        if (tan) { for (let i = 0; i < tan.n; i++) tan.escribir(i, 3, -tan.leer(i, 3)); tangentesHechas.add(p.attributes.TANGENT); }
      }
    }
    if (!indicesHechos.has(p.indices)) {
      for (let t = 0; t < idx.n; t += 3) { const b = idx.leer(t + 1); idx.escribir(t + 1, idx.leer(t + 2)); idx.escribir(t + 2, b); }
      indicesHechos.add(p.indices);
    }
    const material = (gltf.materials || [])[p.material];
    nombresVolteados.add(((material && material.name) || 'sin nombre').replace(/^\//, ''));
  }
  volteados++;
}

// ── Artes pegados a la cara que cubren ──────────────────────────────────────────────────────────
// El arte de un exhibidor es una lámina muy delgada puesta sobre el cartón: su cara visible queda a 0,2–0,4 mm
// de la cara de la estructura (shipper y bandeja de Zenú, 20/11/2026). A esa distancia el visor no alcanza a
// distinguir cuál de las dos va adelante, y al girar la pieza el arte parpadea y se le cuela el color de atrás.
// Se adelanta la cara del arte hasta dejarla a SEPARACION de la cara que cubre. Solo se mueve esa cara (la
// lámina queda un poco más gruesa hacia afuera): la gráfica no se estira, no cambia de tamaño ni de lugar.
// Qué cuenta como «arte pegado»: una cara plana con imagen que queda a menos de SEPARACION por delante de una
// cara plana de OTRO objeto, paralela, mirando hacia el mismo lado y que se traslapa con ella.
const SEPARACION = 0.001;            // 1 mm (el .glb está en metros): no se nota ni a tamaño real en AR
let separados = 0;
const nombresSeparados = new Set();
// Las medidas entre objetos solo valen si ninguno trae giro o corrimiento propio (así exporta Rhino)
const sinTransformar = (gltf.nodes || []).every(n => !n.matrix && !n.translation && !n.rotation && !n.scale);
if (sinTransformar) {
  const planas = [];
  (gltf.meshes || []).forEach((malla, objeto) => {
    for (const p of malla.primitives) {
      if ((p.mode == null ? 4 : p.mode) !== 4 || p.indices == null) continue;
      const pos = vistaReal(p.attributes.POSITION, 3), idx = leerIndices(p.indices);
      if (!pos || !idx || idx.n % 3 || !pos.n) continue;
      const xyz = new Float64Array(pos.n * 3), min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
      for (let i = 0; i < pos.n; i++) for (let k = 0; k < 3; k++) { const x = xyz[i * 3 + k] = pos.leer(i, k); if (x < min[k]) min[k] = x; if (x > max[k]) max[k] = x; }
      const suma = [0, 0, 0];
      let area = 0, malo = false;
      for (let t = 0; t < idx.n; t += 3) {
        const a = idx.leer(t) * 3, b = idx.leer(t + 1) * 3, c = idx.leer(t + 2) * 3;
        if (a >= xyz.length || b >= xyz.length || c >= xyz.length) { malo = true; break; }
        const e1 = [xyz[b] - xyz[a], xyz[b + 1] - xyz[a + 1], xyz[b + 2] - xyz[a + 2]], e2 = [xyz[c] - xyz[a], xyz[c + 1] - xyz[a + 1], xyz[c + 2] - xyz[a + 2]];
        const n = [e1[1] * e2[2] - e1[2] * e2[1], e1[2] * e2[0] - e1[0] * e2[2], e1[0] * e2[1] - e1[1] * e2[0]];
        area += Math.hypot(n[0], n[1], n[2]) / 2;
        for (let k = 0; k < 3; k++) suma[k] += n[k] / 2;
      }
      const largo = Math.hypot(suma[0], suma[1], suma[2]);
      if (malo || area < 1e-4 || largo / area < 0.9995) continue;            // menos de 1 cm² o no es plana
      const n = suma.map(x => x / largo);
      let d = 0, desvio = 0;
      for (let i = 0; i < pos.n; i++) d += (n[0] * xyz[i * 3] + n[1] * xyz[i * 3 + 1] + n[2] * xyz[i * 3 + 2]) / pos.n;
      for (let i = 0; i < pos.n; i++) desvio = Math.max(desvio, Math.abs(n[0] * xyz[i * 3] + n[1] * xyz[i * 3 + 1] + n[2] * xyz[i * 3 + 2] - d));
      if (desvio > 2e-5) continue;
      planas.push({ objeto, p, n, d, min, max, conImagen: !!tieneTextura(p), detras: [] });
    }
  });
  // Se traslapan: sus cajas se tocan en los tres sentidos (con el margen de la separación) y comparten más de
  // 1 mm en al menos dos de ellos (el tercero es el del grosor, donde casi coinciden)
  const traslapan = (a, c) => {
    let amplios = 0;
    for (let k = 0; k < 3; k++) {
      const comun = Math.min(a.max[k], c.max[k]) - Math.max(a.min[k], c.min[k]);
      if (comun < -SEPARACION) return false;
      if (comun > 0.001) amplios++;
    }
    return amplios >= 2;
  };
  const pegadas = [];
  for (const a of planas) {
    if (!a.conImagen) continue;
    for (const c of planas) {
      if (c.objeto === a.objeto || a.n[0] * c.n[0] + a.n[1] * c.n[1] + a.n[2] * c.n[2] < 0.99995) continue;
      const delante = a.d - c.d;                                             // cuánto sobresale el arte de la cara de atrás
      if (delante < -5e-5 || delante >= SEPARACION - 1e-5 || !traslapan(a, c)) continue;
      a.detras.push(c);
    }
    if (a.detras.length) pegadas.push(a);
  }
  // De atrás hacia adelante, por si hay un arte encima de otro: cada uno queda a SEPARACION del que cubre
  pegadas.sort((a, b) => a.d - b.d);
  const nuevaD = new Map(), movimientos = new Map();      // accessor de posiciones → (vértice → corrimiento)
  for (const a of pegadas) {
    let destino = a.d;
    for (const c of a.detras) destino = Math.max(destino, (nuevaD.has(c) ? nuevaD.get(c) : c.d) + SEPARACION);
    const avance = destino - a.d;
    if (avance < 1e-6) continue;
    nuevaD.set(a, destino);
    // Se mueven todos los vértices del objeto que están sobre ese plano: la cara y el borde de sus cantos,
    // para que la lámina no quede abierta por los lados
    const vistos = new Set();
    for (const q of gltf.meshes[a.objeto].primitives) {
      const acc = q.attributes.POSITION;
      if (acc == null || vistos.has(acc)) continue;
      vistos.add(acc);
      const pos = vistaReal(acc, 3);
      if (!pos) continue;
      for (let i = 0; i < pos.n; i++) {
        if (Math.abs(a.n[0] * pos.leer(i, 0) + a.n[1] * pos.leer(i, 1) + a.n[2] * pos.leer(i, 2) - a.d) > 2e-5) continue;
        if (!movimientos.has(acc)) movimientos.set(acc, new Map());
        const m = movimientos.get(acc), v = m.get(i) || [0, 0, 0];
        for (let k = 0; k < 3; k++) v[k] += a.n[k] * avance;
        m.set(i, v);
      }
    }
    separados++;
    const material = (gltf.materials || [])[a.p.material];
    nombresSeparados.add(((material && material.name) || 'sin nombre').replace(/^\//, ''));
  }
  for (const [acc, m] of movimientos) {
    const pos = vistaReal(acc, 3), a = gltf.accessors[acc];
    for (const [i, v] of m) for (let k = 0; k < 3; k++) pos.escribir(i, k, pos.leer(i, k) + v[k]);
    const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
    for (let i = 0; i < pos.n; i++) for (let k = 0; k < 3; k++) { const x = pos.leer(i, k); if (x < min[k]) min[k] = x; if (x > max[k]) max[k] = x; }
    a.min = min; a.max = max;
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

console.log(JSON.stringify({ reparados, sinReparar: rotas.length - reparados, reanclados, nombresReanclados, volteados, nombresVolteados: [...nombresVolteados], separados, nombresSeparados: [...nombresSeparados], lineasQuitadas, vidrios, calados, caladosParejos, nombresVidrio }));

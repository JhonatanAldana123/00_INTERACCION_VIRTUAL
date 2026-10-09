// Requiere (una vez, en una carpeta de trabajo): npm i @gltf-transform/core@4.5.1 @gltf-transform/extensions@4.5.1 @gltf-transform/functions@4.5.1
// Después optimizar cada salida con gltf-transform (weld, flatten, join, simplify, resize, jpeg, draco). Ver pruebas/vr/index.html.
// Separa la isla en: empaque de referencia + matrices de cada copia (para dibujarlos instanciados),
// pieza de acrílico (Generic#13) y el resto estático.
// Uso: node separar-isla.js entrada.glb carpetaSalida
const { NodeIO } = require('@gltf-transform/core');
const { ALL_EXTENSIONS } = require('@gltf-transform/extensions');
const { prune } = require('@gltf-transform/functions');
const fs = require('fs');
const path = require('path');

const [entrada, salida] = process.argv.slice(2);
fs.mkdirSync(salida, { recursive: true });
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);

function nombreMat(p) { const m = p.getMaterial(); return m ? m.getName() : ''; }
function posicionesMundo(nodo) {
  const M = nodo.getWorldMatrix();
  const salidaPos = [];
  for (const p of nodo.getMesh().listPrimitives()) {
    const a = p.getAttribute('POSITION').getArray();
    for (let i = 0; i < a.length; i += 3) {
      const x = a[i], y = a[i + 1], z = a[i + 2];
      salidaPos.push(
        M[0] * x + M[4] * y + M[8] * z + M[12],
        M[1] * x + M[5] * y + M[9] * z + M[13],
        M[2] * x + M[6] * y + M[10] * z + M[14]);
    }
  }
  return salidaPos;
}
function uvDe(nodo) {
  const r = [];
  for (const p of nodo.getMesh().listPrimitives()) {
    const t = p.getAttribute('TEXCOORD_0');
    const c = p.getAttribute('POSITION').getCount();
    for (let i = 0; i < c; i++) { if (t) { const v = t.getElement(i, []); r.push(v[0], v[1]); } else r.push(0, 0); }
  }
  return r;
}
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const cruz = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const largo = v => Math.hypot(v[0], v[1], v[2]);
const pt = (P, i) => [P[i * 3], P[i * 3 + 1], P[i * 3 + 2]];
function inv3(m) {            // m: filas [[..],[..],[..]]
  const [[a, b, c], [d, e, f], [g, h, i]] = m;
  const A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
  const det = a * A + b * B + c * C;
  return [[A / det, -(b * i - c * h) / det, (b * f - c * e) / det],
          [B / det, (a * i - c * g) / det, -(a * f - c * d) / det],
          [C / det, -(a * h - b * g) / det, (a * e - b * d) / det]];
}
const mul3 = (A, B) => A.map(r => [0, 1, 2].map(c => r[0] * B[0][c] + r[1] * B[1][c] + r[2] * B[2][c]));
const apl = (T, v) => [T[0][0] * v[0] + T[0][1] * v[1] + T[0][2] * v[2], T[1][0] * v[0] + T[1][1] * v[1] + T[1][2] * v[2], T[2][0] * v[0] + T[2][1] * v[1] + T[2][2] * v[2]];

(async () => {
  let doc = await io.read(entrada);
  const nodos = doc.getRoot().listNodes();
  const info = [];
  nodos.forEach((n, idx) => {
    const m = n.getMesh(); if (!m) return;
    const prims = m.listPrimitives();
    const mats = new Set(prims.map(nombreMat));
    const firma = prims.map(p => p.getAttribute('POSITION').getCount() + ':' + (p.getIndices() ? p.getIndices().getCount() : 0)).join(',');
    info.push({ idx, n, prims: prims.length, mats, firma });
  });
  // empaques = nodos solo con material SELLO ROJO y muchas partes; se toma la firma más repetida
  const candidatos = info.filter(i => i.mats.size === 1 && i.mats.has('/SELLO ROJO') && i.prims >= 20);
  const cuentas = {};
  candidatos.forEach(i => { cuentas[i.firma] = (cuentas[i.firma] || 0) + 1; });
  const firmaPack = Object.entries(cuentas).sort((a, b) => b[1] - a[1])[0][0];
  const packs = candidatos.filter(i => i.firma === firmaPack);
  console.log('Empaques con la misma malla:', packs.length, 'de', candidatos.length, 'candidatos');

  // referencia y marco (3 puntos lejanos)
  const P0 = posicionesMundo(packs[0].n);
  const N = P0.length / 3;
  const UV0 = uvDe(packs[0].n);
  const a0 = pt(P0, 0);
  let ib = 0, db = 0;
  for (let i = 1; i < N; i++) { const d = largo(sub(pt(P0, i), a0)); if (d > db) { db = d; ib = i; } }
  const b0 = pt(P0, ib);
  let ic = 0, dc = 0;
  for (let i = 1; i < N; i++) { const d = largo(cruz(sub(b0, a0), sub(pt(P0, i), a0))); if (d > dc) { dc = d; ic = i; } }
  const marco = (a, b, c) => {
    const u = sub(b, a), v = sub(c, a), w0 = cruz(u, v), k = Math.sqrt(largo(w0));
    const w = [w0[0] / k, w0[1] / k, w0[2] / k];
    return [[u[0], v[0], w[0]], [u[1], v[1], w[1]], [u[2], v[2], w[2]]];
  };
  const Fref = marco(a0, b0, pt(P0, ic));
  const FrefInv = inv3(Fref);
  // centro de la caja del empaque de referencia
  const mn = [1e9, 1e9, 1e9], mx = [-1e9, -1e9, -1e9];
  for (let i = 0; i < N; i++) { const p = pt(P0, i); for (let k = 0; k < 3; k++) { mn[k] = Math.min(mn[k], p[k]); mx[k] = Math.max(mx[k], p[k]); } }
  const c0 = [0, 1, 2].map(k => (mn[k] + mx[k]) / 2);

  const matrices = [];
  const buenos = new Set();
  let peorError = 0, axiales = 0;

  // las 24 rotaciones propias que son permutaciones con signo de los ejes
  const rotaciones = [];
  const perms = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
  for (const pm of perms) for (const sx of [1, -1]) for (const sy of [1, -1]) for (const sz of [1, -1]) {
    const S = [[0, 0, 0], [0, 0, 0], [0, 0, 0]];
    S[0][pm[0]] = sx; S[1][pm[1]] = sy; S[2][pm[2]] = sz;
    const det = S[0][0]*(S[1][1]*S[2][2]-S[1][2]*S[2][1]) - S[0][1]*(S[1][0]*S[2][2]-S[1][2]*S[2][0]) + S[0][2]*(S[1][0]*S[2][1]-S[1][1]*S[2][0]);
    if (det > 0) rotaciones.push(S);
  }
  const CELDA = 2e-3, TOL = 4e-4;
  function ajusteAxial(Pi, UVi) {
    const cantidad = Pi.length / 3;
    const mni = [1e9, 1e9, 1e9], mxi = [-1e9, -1e9, -1e9];
    for (let i = 0; i < cantidad; i++) for (let k = 0; k < 3; k++) { mni[k] = Math.min(mni[k], Pi[i*3+k]); mxi[k] = Math.max(mxi[k], Pi[i*3+k]); }
    const ci = [0, 1, 2].map(k => (mni[k] + mxi[k]) / 2);
    const rejilla = new Map();
    const llave = (x, y, z) => Math.floor(x / CELDA) + ',' + Math.floor(y / CELDA) + ',' + Math.floor(z / CELDA);
    for (let i = 0; i < cantidad; i++) { const k = llave(Pi[i*3], Pi[i*3+1], Pi[i*3+2]); if (!rejilla.has(k)) rejilla.set(k, []); rejilla.get(k).push(i); }
    function hay(x, y, z, u, v) {
      const cx = Math.floor(x / CELDA), cy = Math.floor(y / CELDA), cz = Math.floor(z / CELDA);
      for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) for (let c = -1; c <= 1; c++) {
        const lst = rejilla.get((cx + a) + ',' + (cy + b) + ',' + (cz + c)); if (!lst) continue;
        for (const i of lst) if (Math.abs(Pi[i*3] - x) < TOL && Math.abs(Pi[i*3+1] - y) < TOL && Math.abs(Pi[i*3+2] - z) < TOL && Math.abs(UVi[i*2] - u) < 2e-3 && Math.abs(UVi[i*2+1] - v) < 2e-3) return true;
      }
      return false;
    }
    for (const S of rotaciones) {
      let ok = true;
      for (let i = 0; i < N && ok; i += 3) {                        // se prueba cada tercer punto: basta para descartar
        const g = [P0[i*3] - c0[0], P0[i*3+1] - c0[1], P0[i*3+2] - c0[2]];
        const q = apl(S, g);
        if (!hay(q[0] + ci[0], q[1] + ci[1], q[2] + ci[2], UV0[i*2], UV0[i*2+1])) ok = false;
      }
      if (!ok) continue;
      for (let i = 0; i < N && ok; i++) {                          // confirmación con todos los puntos
        const g = [P0[i*3] - c0[0], P0[i*3+1] - c0[1], P0[i*3+2] - c0[2]];
        const q = apl(S, g);
        if (!hay(q[0] + ci[0], q[1] + ci[1], q[2] + ci[2], UV0[i*2], UV0[i*2+1])) ok = false;
      }
      if (ok) return [S[0][0], S[1][0], S[2][0], 0, S[0][1], S[1][1], S[2][1], 0, S[0][2], S[1][2], S[2][2], 0, ci[0], ci[1], ci[2], 1];
    }
    return null;
  }
  packs.forEach((pk, j) => {
    const Pi = posicionesMundo(pk.n);
    if (Pi.length !== P0.length) return;
    // se prueba la orientación normal y la reflejada (espejo): se queda la que mejor encaja
    let mejor = null;
    for (const signo of [1, -1]) {
      const Fi = marco(pt(Pi, 0), pt(Pi, ib), pt(Pi, ic));
      if (signo < 0) for (let r = 0; r < 3; r++) Fi[r][2] = -Fi[r][2];
      const Tn = mul3(Fi, FrefInv);
      const Ta = apl(Tn, a0);
      const tn = [pt(Pi, 0)[0] - Ta[0], pt(Pi, 0)[1] - Ta[1], pt(Pi, 0)[2] - Ta[2]];
      let e = 0;
      for (let i = 0; i < N; i++) {
        const q = apl(Tn, pt(P0, i));
        e = Math.max(e, Math.abs(q[0] + tn[0] - Pi[i * 3]), Math.abs(q[1] + tn[1] - Pi[i * 3 + 1]), Math.abs(q[2] + tn[2] - Pi[i * 3 + 2]));
      }
      if (!mejor || e < mejor.e) mejor = { T: Tn, t: tn, e };
    }
    const T = mejor.T, t = mejor.t, err = mejor.e;
    if (err > 2e-4) {                             // otro orden de vértices: se busca entre las 24 rotaciones de 90° por coincidencia de puntos
      const M = ajusteAxial(Pi, uvDe(pk.n));
      if (M) { matrices.push(M); buenos.add(pk.n); axiales++; }
      return;
    }
    peorError = Math.max(peorError, err);
    const Tc = apl(T, c0);
    // matriz 4x4 por columnas: la geometría centrada (referencia - c0) pasa a su lugar en el mundo
    matrices.push([T[0][0], T[1][0], T[2][0], 0, T[0][1], T[1][1], T[2][1], 0, T[0][2], T[1][2], T[2][2], 0, Tc[0] + t[0], Tc[1] + t[1], Tc[2] + t[2], 1]);
    buenos.add(pk.n);
  });
  console.log('Copias rígidas válidas:', matrices.length, '(' + axiales + ' por rotación de 90°) · peor error (m):', peorError.toExponential(2));
  fs.writeFileSync(path.join(salida, 'isla_packs.json'), JSON.stringify({ n: matrices.length, matrices: matrices.map(m => m.map(v => +v.toFixed(6))) }));

  const refNodo = packs[0].n;
  const acril = info.filter(i => i.mats.has('/Generic#13')).map(i => i.n);

  async function exportar(nombre, conservar, ajustar) {
    const d = await io.read(entrada);
    const ns = d.getRoot().listNodes();
    const claves = conservar.map(n => nodos.indexOf(n));
    ns.forEach((n, i) => { if (!claves.includes(i)) n.dispose(); });
    if (ajustar) ajustar(ns[nodos.indexOf(refNodo)]);
    await d.transform(prune());
    await io.write(path.join(salida, nombre), d);
    console.log('escrito', nombre);
  }
  await exportar('isla_pack_ref.glb', [refNodo], n => { n.setTranslation([-c0[0], -c0[1], -c0[2]]); });
  await exportar('isla_acrilico.glb', acril);
  const resto = info.filter(i => !buenos.has(i.n) && !acril.includes(i.n)).map(i => i.n);
  await exportar('isla_resto.glb', resto);
})();

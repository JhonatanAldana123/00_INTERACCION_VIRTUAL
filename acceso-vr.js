// Contraseña para entrar al entorno VR (pruebas/vr/).
//
// La contraseña NO está escrita aquí: solo su «huella» (PBKDF2-SHA256, 100 000 vueltas, con sal). Al escribirla,
// el navegador calcula la huella de lo escrito y la compara. Una vez aceptada, el aparato la recuerda.
//
// Ojo: el sitio es estático (GitHub Pages), así que esto es una puerta, no una caja fuerte: evita que entre quien
// llega a la página, pero los archivos de los modelos siguen siendo públicos para quien sepa su dirección.
//
// Para CAMBIAR la contraseña: en PowerShell, en la carpeta del proyecto (cambia NUEVA por la clave):
//   $env:CLAVE_VR='NUEVA'; node -e "const c=require('crypto');const s=c.randomBytes(16).toString('hex');console.log(s+' '+c.pbkdf2Sync(process.env.CLAVE_VR,Buffer.from(s,'hex'),100000,32,'sha256').toString('hex'))"; Remove-Item Env:CLAVE_VR
// y copia los dos valores que salen en SAL y HUELLA. Los aparatos que ya habían entrado tendrán que escribirla de nuevo.
//
// Escrito sin funciones flecha ni async a propósito: lo abre también el navegador viejo de las Oculus Go.
(function () {
  var SAL = 'dd60cf65a2c6d2a482414c3b6c597024';
  var HUELLA = '85e7aca614b599eac4a0387b43502b9ec612c69f103deb5d4caf60c4efc5cf0b';
  var VUELTAS = 100000;
  var LLAVE = 'innercia-acceso-vr';

  function recordada() {
    try { return localStorage.getItem(LLAVE) === HUELLA; } catch (e) { return false; }
  }
  function recordar() {
    try { localStorage.setItem(LLAVE, HUELLA); } catch (e) { /* sin almacenamiento: la pedirá otra vez */ }
  }
  function hex(buffer) {
    var b = new Uint8Array(buffer), s = '';
    for (var i = 0; i < b.length; i++) s += (b[i] < 16 ? '0' : '') + b[i].toString(16);
    return s;
  }
  function bytesDeHex(t) {
    var b = new Uint8Array(t.length / 2);
    for (var i = 0; i < b.length; i++) b[i] = parseInt(t.substr(i * 2, 2), 16);
    return b;
  }
  // Devuelve una promesa con true si la clave es la correcta
  function comprobar(clave) {
    var sutil = window.crypto && window.crypto.subtle;
    if (!sutil || !window.TextEncoder) return Promise.reject(new Error('sin-cifrado'));
    return sutil.importKey('raw', new TextEncoder().encode(clave), { name: 'PBKDF2' }, false, ['deriveBits'])
      .then(function (k) {
        return sutil.deriveBits({ name: 'PBKDF2', salt: bytesDeHex(SAL), iterations: VUELTAS, hash: 'SHA-256' }, k, 256);
      })
      .then(function (bits) { return hex(bits) === HUELLA; });
  }

  var CSS =
    '.avr-fondo{position:fixed;inset:0;top:0;left:0;right:0;bottom:0;z-index:300;display:flex;align-items:center;justify-content:center;' +
    'padding:20px;background:rgba(0,20,30,.6);font-family:Ubuntu,-apple-system,"Segoe UI",system-ui,sans-serif;}' +
    '.avr-caja{width:100%;max-width:340px;padding:26px 24px 22px;border-radius:20px;background:#005C7E;color:#fff;text-align:center;' +
    'box-shadow:0 16px 50px rgba(0,0,0,.35);border:1px solid rgba(255,255,255,.18);}' +
    '.avr-caja h2{margin:0 0 6px;font-size:20px;font-weight:700;}' +
    '.avr-caja p{margin:0 0 16px;font-size:14px;font-weight:300;color:rgba(255,255,255,.85);}' +
    '.avr-caja input{width:100%;box-sizing:border-box;padding:12px 16px;border-radius:100px;border:1.5px solid rgba(255,255,255,.5);' +
    'background:rgba(0,38,56,.45);color:#fff;font:500 16px Ubuntu,system-ui,sans-serif;text-align:center;outline:none;}' +
    '.avr-caja input:focus{border-color:#93C01F;}' +
    '.avr-error{min-height:18px;margin:8px 0 2px;font-size:13px;color:#ffb3b3;}' +
    '.avr-botones{display:flex;gap:10px;justify-content:center;margin-top:8px;}' +
    '.avr-botones button{border:none;border-radius:100px;padding:11px 24px;font:700 15px Ubuntu,system-ui,sans-serif;cursor:pointer;}' +
    '.avr-entrar{background:#93C01F;color:#005C7E;}' +
    '.avr-entrar[disabled]{opacity:.6;cursor:default;}' +
    '.avr-cancelar{background:transparent;color:#fff;border:1.5px solid rgba(255,255,255,.45) !important;}';

  // Muestra la ventana de contraseña; al acertar, llama a alEntrar()
  function pedir(alEntrar) {
    if (recordada()) { alEntrar(); return; }
    if (!document.getElementById('avr-estilos')) {
      var estilo = document.createElement('style');
      estilo.id = 'avr-estilos'; estilo.textContent = CSS;
      document.head.appendChild(estilo);
    }
    var fondo = document.createElement('div');
    fondo.className = 'avr-fondo';
    fondo.innerHTML =
      '<form class="avr-caja" autocomplete="off">' +
      '<h2>Entorno VR</h2><p>Escribe la contraseña para entrar.</p>' +
      '<input type="password" aria-label="Contraseña" autocomplete="current-password">' +
      '<div class="avr-error" aria-live="polite"></div>' +
      '<div class="avr-botones"><button type="button" class="avr-cancelar">Cancelar</button>' +
      '<button type="submit" class="avr-entrar">Entrar</button></div></form>';
    document.body.appendChild(fondo);
    var form = fondo.firstChild, campo = form.querySelector('input'), error = form.querySelector('.avr-error'),
        entrar = form.querySelector('.avr-entrar');
    function cerrar() { if (fondo.parentNode) fondo.parentNode.removeChild(fondo); }
    form.querySelector('.avr-cancelar').onclick = cerrar;
    fondo.onclick = function (e) { if (e.target === fondo) cerrar(); };
    form.onsubmit = function (e) {
      e.preventDefault();
      if (!campo.value) { error.textContent = 'Escribe la contraseña.'; return; }
      entrar.disabled = true; error.textContent = 'Comprobando…';
      comprobar(campo.value).then(function (bien) {
        entrar.disabled = false;
        if (bien) { recordar(); cerrar(); alEntrar(); }
        else { error.textContent = 'Contraseña incorrecta.'; campo.value = ''; campo.focus(); }
      }, function () {
        entrar.disabled = false;
        error.textContent = 'Este navegador no puede comprobar la contraseña. Abre la página con https.';
      });
    };
    setTimeout(function () { campo.focus(); }, 50);
  }

  // Para la página VR: si este aparato no ha puesto la contraseña, vuelve a la página principal, que la pide
  function proteger(inicio) {
    if (recordada()) return;
    document.documentElement.style.visibility = 'hidden';
    location.replace(inicio + '?vr=1');
  }

  window.accesoVR = { pedir: pedir, proteger: proteger, recordada: recordada };
})();

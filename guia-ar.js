// Guía de gestos para el AR a tamaño real. La usan el visor del QR (index.html) y la Tarjeta AR
// (pruebas/tarjeta/index.html).
//
// Qué se puede y qué no:
//  · AR del navegador en Android (WebXR): lo dibuja esta misma página, así que la guía sale ENCIMA de la cámara:
//    una pastilla pequeña abajo, con un dibujo animado y una frase corta.
//      1. al entrar ............ «Mueve el celular despacio apuntando al piso»
//      2. al aparecer la pieza . tres gestos, uno tras otro (mover, girar, tamaño), y se quita sola
//      3. si pierde el piso .... «Apunta de nuevo al piso»
//    Un botón «?» pequeño repite los gestos. Nada de esto recibe toques: los gestos llegan a la pieza.
//  · AR de iPhone (Quick Look) y app de AR de Google: son de Apple y de Google y no dejan poner nada encima.
//    En iPhone, la primera vez se muestra una tarjeta con los mismos gestos ANTES de abrir el AR.
//
// Uso:   const guia = instalarGuiaAR(elementoModelViewer);
//        if (guia.previa(() => abrirAR())) return;   // en iPhone, la primera vez: muestra la tarjeta y abre al tocar
(function () {
  const ICONOS = {
    // celular que se mece, buscando el piso
    piso: '<svg viewBox="0 0 48 48"><g class="gar-mecer"><rect x="17" y="8" width="14" height="26" rx="3" fill="none" stroke="#fff" stroke-width="2.4"/><circle cx="24" cy="29.5" r="1.4" fill="#fff"/></g><path d="M8 41h32" stroke="#93C01F" stroke-width="2.4" stroke-linecap="round" stroke-dasharray="3 5"/></svg>',
    // un dedo que arrastra
    mover: '<svg viewBox="0 0 48 48"><path d="M10 24h28" stroke="rgba(255,255,255,.35)" stroke-width="2" stroke-linecap="round" stroke-dasharray="2 5"/><g class="gar-arrastrar"><circle cx="24" cy="24" r="9" fill="none" stroke="#93C01F" stroke-width="2"/><circle cx="24" cy="24" r="4.5" fill="#fff"/></g></svg>',
    // dos dedos que giran
    girar: '<svg viewBox="0 0 48 48"><circle cx="24" cy="24" r="13" fill="none" stroke="rgba(255,255,255,.35)" stroke-width="2" stroke-dasharray="2 5"/><g class="gar-girar"><circle cx="24" cy="11" r="4.5" fill="#fff"/><circle cx="24" cy="37" r="4.5" fill="#fff"/></g></svg>',
    // dos dedos que se separan y se juntan
    tamano: '<svg viewBox="0 0 48 48"><path d="M12 36L36 12" stroke="rgba(255,255,255,.35)" stroke-width="2" stroke-linecap="round" stroke-dasharray="2 5"/><circle class="gar-abrir-a" cx="20" cy="28" r="4.5" fill="#fff"/><circle class="gar-abrir-b" cx="28" cy="20" r="4.5" fill="#fff"/></svg>',
  };
  const TEXTOS = {
    piso: 'Mueve el celular despacio apuntando al piso',
    mover: 'Un dedo: muévela por el piso',
    girar: 'Dos dedos: gírala',
    tamano: 'Pellizca: cambia el tamaño',
    perdido: 'Apunta de nuevo al piso',
  };
  const CSS = `
    .gar-guia, .gar-ayuda { display: none; }
    model-viewer[ar-status="session-started"] .gar-guia, model-viewer[ar-status="object-placed"] .gar-guia { display: block; }
    model-viewer[ar-status="object-placed"] .gar-ayuda { display: grid; }
    .gar-guia { position: absolute; left: 0; right: 0; bottom: calc(max(env(safe-area-inset-bottom, 0px), 14px) + 22px); text-align: center; pointer-events: none; z-index: 5; }
    .gar-tip {
      display: none; align-items: center; gap: 10px; max-width: 88%; padding: 7px 18px 7px 9px; border-radius: 100px;
      background: rgba(0, 38, 56, .74); color: #fff; font: 500 15px/1.25 'Ubuntu', -apple-system, 'Segoe UI', system-ui, sans-serif;
      text-align: left; box-shadow: 0 6px 22px rgba(0,0,0,.28); -webkit-backdrop-filter: blur(6px); backdrop-filter: blur(6px);
    }
    .gar-tip.gar-ver { display: inline-flex; animation: gar-entra .4s ease both; }
    .gar-tip svg { flex: none; width: 40px; height: 40px; }
    .gar-ayuda {
      position: absolute; left: 14px; bottom: calc(max(env(safe-area-inset-bottom, 0px), 14px) + 90px); z-index: 6; width: 40px; height: 40px;
      place-items: center; border-radius: 50%; border: 1.5px solid rgba(255,255,255,.6); background: rgba(0, 38, 56, .6); color: #fff;
      font: 700 18px 'Ubuntu', system-ui, sans-serif; padding: 0; cursor: pointer; -webkit-tap-highlight-color: transparent;
    }
    @keyframes gar-entra { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }
    .gar-mecer { transform-origin: 24px 34px; animation: gar-mecer 1.8s ease-in-out infinite; }
    @keyframes gar-mecer { 0%, 100% { transform: rotate(-16deg); } 50% { transform: rotate(16deg); } }
    .gar-arrastrar { animation: gar-arrastrar 1.9s ease-in-out infinite; }
    @keyframes gar-arrastrar { 0%, 100% { transform: translateX(-11px); } 50% { transform: translateX(11px); } }
    .gar-girar { transform-origin: 24px 24px; animation: gar-girar 2.2s ease-in-out infinite; }
    @keyframes gar-girar { 0%, 100% { transform: rotate(-50deg); } 50% { transform: rotate(50deg); } }
    .gar-abrir-a { animation: gar-abrir-a 1.8s ease-in-out infinite; }
    .gar-abrir-b { animation: gar-abrir-b 1.8s ease-in-out infinite; }
    @keyframes gar-abrir-a { 0%, 100% { transform: translate(0, 0); } 50% { transform: translate(-8px, 8px); } }
    @keyframes gar-abrir-b { 0%, 100% { transform: translate(0, 0); } 50% { transform: translate(8px, -8px); } }

    /* Tarjeta previa (iPhone): sobre la página, antes de abrir el AR de Apple */
    .gar-previa { position: fixed; inset: 0; z-index: 200; display: flex; align-items: flex-end; justify-content: center; background: rgba(0, 20, 30, .55); animation: gar-fondo .25s ease both; }
    .gar-previa[hidden] { display: none; }
    .gar-hoja {
      width: 100%; max-width: 460px; padding: 22px 22px max(env(safe-area-inset-bottom, 0px), 22px); border-radius: 22px 22px 0 0;
      background: #005C7E; color: #fff; font: 300 15px/1.35 'Ubuntu', -apple-system, system-ui, sans-serif; text-align: center;
      animation: gar-sube .35s ease both;
    }
    .gar-hoja h2 { font-size: 19px; font-weight: 500; margin: 0 0 12px; }
    .gar-fila { display: flex; align-items: center; gap: 12px; text-align: left; padding: 5px 0; }
    .gar-fila svg { flex: none; width: 44px; height: 44px; }
    .gar-hoja button { margin-top: 16px; border: none; border-radius: 100px; padding: 10px 30px; background: #93C01F; color: #005C7E; font: 700 18px 'Ubuntu', system-ui, sans-serif; }
    @keyframes gar-fondo { from { opacity: 0; } to { opacity: 1; } }
    @keyframes gar-sube { from { transform: translateY(40px); opacity: 0; } to { transform: none; opacity: 1; } }
  `;

  window.instalarGuiaAR = function (visor) {
    if (!document.getElementById('gar-estilos')) {
      const estilo = document.createElement('style');
      estilo.id = 'gar-estilos'; estilo.textContent = CSS;
      document.head.appendChild(estilo);
    }
    // Dentro de <model-viewer>: el AR del navegador muestra sus hijos encima de la cámara
    const guia = document.createElement('div');
    guia.className = 'gar-guia';
    const tips = {};
    for (const fase of Object.keys(TEXTOS)) {
      const t = tips[fase] = document.createElement('div');
      t.className = 'gar-tip';
      t.innerHTML = (ICONOS[fase] || ICONOS.piso) + '<span></span>';
      t.lastChild.textContent = TEXTOS[fase];
      guia.appendChild(t);
    }
    const ayuda = document.createElement('button');
    ayuda.className = 'gar-ayuda'; ayuda.type = 'button'; ayuda.textContent = '?';
    ayuda.setAttribute('aria-label', 'Ver cómo mover la pieza');
    visor.append(guia, ayuda);

    let reloj, colocada = false;
    const mostrar = fase => { for (const k in tips) tips[k].classList.toggle('gar-ver', k === fase); };
    function gestos() {
      clearTimeout(reloj);
      const orden = ['mover', 'girar', 'tamano'];
      let i = 0;
      (function paso() {
        if (i >= orden.length) { mostrar(null); return; }
        mostrar(orden[i++]);
        reloj = setTimeout(paso, 3400);
      })();
    }
    ayuda.addEventListener('click', gestos);
    visor.addEventListener('ar-status', e => {
      const estado = e.detail && e.detail.status;
      if (estado === 'session-started') { colocada = false; clearTimeout(reloj); mostrar('piso'); }
      else if (estado === 'object-placed') { colocada = true; gestos(); }
      else { colocada = false; clearTimeout(reloj); mostrar(null); }
    });
    visor.addEventListener('ar-tracking', e => {
      const estado = e.detail && e.detail.status;
      if (estado === 'not-tracking') { clearTimeout(reloj); mostrar('perdido'); }
      else if (tips.perdido.classList.contains('gar-ver')) mostrar(colocada ? null : 'piso');
    });

    // iPhone: la primera vez, los gestos se explican antes de abrir el AR de Apple
    const esIOS = /iPad|iPhone|iPod/.test(navigator.userAgent) && !window.MSStream;
    let previa = null;
    return {
      gestos, mostrar,
      previa(abrir, forzar) {
        let vista = false;
        try { vista = sessionStorage.getItem('gar-previa') === '1'; } catch (e) { /* sin almacenamiento */ }
        if (!forzar && (!esIOS || vista)) return false;
        if (!previa) {
          previa = document.createElement('div');
          previa.className = 'gar-previa';
          previa.innerHTML = '<div class="gar-hoja"><h2>Así se mueve la pieza</h2>'
            + ['mover', 'girar', 'tamano'].map(f => '<div class="gar-fila">' + ICONOS[f] + '<span>' + TEXTOS[f] + '</span></div>').join('')
            + '<button type="button">Abrir en mi espacio</button></div>';
          document.body.appendChild(previa);
        }
        previa.hidden = false;
        previa.querySelector('button').onclick = () => {
          previa.hidden = true;
          try { sessionStorage.setItem('gar-previa', '1'); } catch (e) { /* ídem */ }
          abrir();
        };
        previa.onclick = e => { if (e.target === previa) previa.hidden = true; };      // tocar afuera la cierra sin abrir el AR
        return true;
      },
    };
  };
})();

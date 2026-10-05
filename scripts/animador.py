"""Motor del animador de Flujo AR (se ejecuta dentro de Blender, sin ventana).

  blender --background --factory-startup --python-exit-code 1 --python scripts/animador.py -- analizar <pieza.glb> <carpeta _animador>
  blender --background --factory-startup --python-exit-code 1 --python scripts/animador.py -- generar  <carpeta del proyecto>
  blender --background --factory-startup --python-exit-code 1 --python scripts/animador.py -- vista    <pieza.glb> <carpeta _animador>

vistas_sin <pieza.glb> <carpeta> <ocultas.json>: las 4 vistas sin las piezas ocultas en la ventana (el ojo 👁 de
  cada grupo), con el mismo encuadre, para elegir lo que estaba detrás.

vista: solo exporta _animador/vista.glb (lo hace también "analizar") para la vista previa en vivo
  (herramientas/vista-animador/), que reproduce la receta en el navegador sin pasar por Blender.

analizar: lista las piezas del .glb y genera 4 vistas (frente, atrás, de frente y desde arriba). Cada vista
  trae una imagen (vista_X.png) y un mapa de piezas (vista_X.ids: un número de 16 bits por píxel, 0 = fondo,
  k = pieza k de piezas.json) para que la ventana sepa qué pieza hay bajo el mouse.

generar: lee animacion.json (la receta que arma la ventana) y crea la animación:
  - cada grupo cuelga de un eje (empty) en su bisagra o en su base, y solo se animan los ejes
  - bisagra: el grupo empieza abierto (acostado hacia un lado) y se cierra
  - entrar:  el grupo llega desde un lado y puede aparecer (escala 0,001 → 1; nunca 0: el exportador
             guardaría las piezas con escala 0 y quedarían invisibles para siempre)
  - girar:   el grupo gira sobre su eje vertical
  Guarda el .blend, exporta GLB (una sola animación) y USDZ (iPhone, Y arriba) y una imagen al final de cada paso.

Los mensajes que empiezan con @@ los lee la ventana (progreso, errores y resultado).
"""
import bpy, sys, os, json, math
from mathutils import Vector, Matrix

MINIMA = 0.001          # escala para "invisible"
FPS = 30
VISTAS = {               # dirección desde el centro hacia la cámara (Z arriba; el frente mira a -Y)
    'frente_iso': Vector((-0.8, -1.0, 0.75)),
    'atras_iso': Vector((0.8, 1.0, 0.75)),
    'frente': Vector((0.0, -1.0, 0.0)),
    'arriba': Vector((0.0, -0.001, 1.0)),
}
ANCHO, ALTO = 1600, 1200   # más resolución = clics más precisos en piezas pequeñas


def aviso(clave, valor=''):
    print(f'@@{clave} {valor}'.rstrip(), flush=True)


# ── Escena ───────────────────────────────────────────────────────
def importar(glb):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glb)
    corregir_mapeo()
    return piezas()


def piezas():
    # Orden por nombre: el mismo en "analizar" y en "generar" para el mismo .glb
    return sorted([o for o in bpy.context.scene.objects if o.type == 'MESH'], key=lambda o: o.name)


def corregir_mapeo():
    """Rhino exporta las imágenes volteadas y las corrige con KHR_texture_transform (nodo Mapping).
    Las vistas (Workbench) y el exportador USD no siempre respetan ese nodo: se aplica al mapeo (UV)."""
    trans = {}
    for mat in bpy.data.materials:
        if not mat.node_tree:
            continue
        nodos = [n for n in mat.node_tree.nodes if n.type == 'MAPPING']
        if not nodos:
            continue
        m = nodos[0]
        if any(abs(v) > 1e-6 for v in m.inputs['Rotation'].default_value):
            continue  # con rotación se deja como está
        trans[mat.name] = (Vector(m.inputs['Location'].default_value), Vector(m.inputs['Scale'].default_value))
        for mp in nodos:
            src = mp.inputs['Vector'].links[0].from_socket if mp.inputs['Vector'].links else None
            for l in list(mp.outputs['Vector'].links):
                if src:
                    mat.node_tree.links.new(src, l.to_socket)
                else:
                    mat.node_tree.links.remove(l)
            mat.node_tree.nodes.remove(mp)
    hechos = set()
    for o in bpy.data.objects:
        if o.type != 'MESH' or o.data.name in hechos:
            continue
        hechos.add(o.data.name)
        me = o.data
        for poly in me.polygons:
            mat = me.materials[poly.material_index] if me.materials else None
            if not mat or mat.name not in trans:
                continue
            loc, esc = trans[mat.name]
            for uvl in me.uv_layers:
                for li in poly.loop_indices:
                    u, v = uvl.data[li].uv
                    uvl.data[li].uv = (u * esc.x + loc.x, v * esc.y + loc.y)


def caja(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    if not pts:
        return Vector((0, 0, 0)), Vector((0, 0, 0))
    return (Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))),
            Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))))


def camara(nombre, mn, mx, ancho=ANCHO, alto=ALTO):
    sc = bpy.context.scene
    cam = bpy.data.objects.get('CAM_VISTA') or bpy.data.objects.new('CAM_VISTA', bpy.data.cameras.new('CAM_VISTA'))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    d = VISTAS[nombre].normalized()
    centro = (mn + mx) / 2
    diag = (mx - mn).length or 1
    cam.location = centro + d * diag * 4
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'ORTHO'
    cam.data.clip_end = diag * 20
    bpy.context.view_layer.update()
    # Ancho y alto de la pieza vistos desde la cámara
    rot = cam.matrix_world.to_3x3()
    der, arr = rot.col[0], rot.col[1]
    esquinas = [Vector((x, y, z)) for x in (mn.x, mx.x) for y in (mn.y, mx.y) for z in (mn.z, mx.z)]
    w = max(c.dot(der) for c in esquinas) - min(c.dot(der) for c in esquinas)
    h = max(c.dot(arr) for c in esquinas) - min(c.dot(arr) for c in esquinas)
    cam.data.ortho_scale = max(w, h * ancho / alto) * 1.12
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = ancho, alto
    sc.render.resolution_percentage = 100
    return cam


def preparar_workbench():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.film_transparent = True
    sc.render.image_settings.media_type = 'IMAGE'
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA'
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.render.dither_intensity = 0
    sh = sc.display.shading
    sh.show_shadows = False
    sh.show_cavity = False
    sh.show_object_outline = False
    sh.show_backface_culling = False


def foto(ruta):
    sc = bpy.context.scene
    sc.render.filepath = ruta
    bpy.ops.render.render(write_still=True)


def srgb_a_lineal(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


# ── analizar ─────────────────────────────────────────────────────
def analizar(glb, salida):
    import numpy as np
    os.makedirs(salida, exist_ok=True)
    aviso('PROGRESO', 'Leyendo la pieza')
    ps = importar(glb)
    if not ps:
        raise RuntimeError('El .glb no tiene piezas (mallas).')
    if len(ps) > 32000:
        raise RuntimeError('Demasiadas piezas sueltas para el animador.')
    mn, mx = caja(ps)

    datos = []
    for i, o in enumerate(ps):
        a, b = caja([o])
        mats = [m.name.lstrip('/') for m in o.data.materials if m]
        datos.append({
            'nombre': o.name, 'material': mats[0] if mats else '',
            'triangulos': sum(len(p.vertices) - 2 for p in o.data.polygons),
            'min': [round(v, 4) for v in a], 'max': [round(v, 4) for v in b],
        })

    renderizar_vistas(ps, mn, mx, salida, datos)

    with open(os.path.join(salida, 'piezas.json'), 'w', encoding='utf-8') as f:
        json.dump({'origen': os.path.basename(glb), 'ancho': ANCHO, 'alto': ALTO, 'vistas': list(VISTAS),
                   'min': [round(v, 4) for v in mn], 'max': [round(v, 4) for v in mx], 'piezas': datos},
                  f, ensure_ascii=False, indent=1)
    exportar_vista(salida)
    aviso('OK', f'{len(ps)} piezas')


def vistas_sin(glb, salida, ruta_ocultas):
    """Las mismas 4 vistas (mismo encuadre y numeración de piezas) pero sin las piezas ocultas en la ventana:
    así se ve, y se puede elegir, lo que estaba detrás."""
    import numpy as np
    with open(ruta_ocultas, encoding='utf-8-sig') as f:
        ocultas = set(json.load(f))
    os.makedirs(salida, exist_ok=True)
    ps = importar(glb)
    mn, mx = caja(ps)
    for o in ps:
        o.hide_render = o.name in ocultas
    renderizar_vistas(ps, mn, mx, salida, None)
    aviso('OK', f'{len(ocultas)} ocultas')


def renderizar_vistas(ps, mn, mx, salida, datos):
    """Imagen (vista_X.png) y mapa de piezas (vista_X.ids) de cada vista. datos: si se pasa, se le agrega el
    rectángulo de cada pieza en cada vista."""
    import numpy as np
    preparar_workbench()
    sc = bpy.context.scene
    sh = sc.display.shading
    colores = {o.name: tuple(o.color) for o in ps}
    from bpy_extras.object_utils import world_to_camera_view
    for nombre in VISTAS:
        aviso('PROGRESO', f'Vista {nombre}')
        cam = camara(nombre, mn, mx)
        # Rectángulo de cada pieza en esta vista (píxeles, y hacia abajo) y su profundidad: con esto la ventana
        # lista todas las piezas bajo un punto, también las de atrás (menú del clic derecho)
        for o, d in zip(ps, datos or []):
            pts = [world_to_camera_view(sc, cam, o.matrix_world @ Vector(c)) for c in o.bound_box]
            d.setdefault('rects', {})[nombre] = [
                round(min(p.x for p in pts) * ANCHO, 1), round((1 - max(p.y for p in pts)) * ALTO, 1),
                round(max(p.x for p in pts) * ANCHO, 1), round((1 - min(p.y for p in pts)) * ALTO, 1),
                round(sum(p.z for p in pts) / 8, 4)]
        # Imagen
        sc.display.render_aa = '8'
        sh.light = 'STUDIO'
        sh.color_type = 'TEXTURE'
        sc.view_settings.exposure = 1.2
        for o in ps:
            o.color = colores[o.name]
        foto(os.path.join(salida, f'vista_{nombre}.png'))
        sc.view_settings.exposure = 0
        # Mapa de piezas: cada pieza con un color plano que codifica su número (5 bits por canal)
        sc.display.render_aa = 'OFF'
        sh.light = 'FLAT'
        sh.color_type = 'OBJECT'
        for k, o in enumerate(ps, start=1):
            canales = ((k >> 10) & 31, (k >> 5) & 31, k & 31)
            o.color = tuple(srgb_a_lineal((v * 8 + 4) / 255) for v in canales) + (1.0,)
        ruta_ids = os.path.join(salida, f'_ids_{nombre}.png')
        foto(ruta_ids)
        img = bpy.data.images.load(ruta_ids)
        px = np.array(img.pixels[:], dtype=np.float32).reshape(ALTO, ANCHO, 4)[::-1]  # fila 0 = arriba
        bytes_ = np.rint(px * 255).astype(np.int32)
        cod = lambda c: np.clip(np.rint((bytes_[:, :, c] - 4) / 8), 0, 31).astype(np.int32)
        ids = (cod(0) << 10) | (cod(1) << 5) | cod(2)
        ids[bytes_[:, :, 3] < 128] = 0
        ids[ids > len(ps)] = 0
        ids.astype('<u2').tofile(os.path.join(salida, f'vista_{nombre}.ids'))
        bpy.data.images.remove(img)
        os.remove(ruta_ids)
    for o in ps:
        o.color = colores[o.name]


USDZ_TRIANGULOS = 350000   # el USDZ no se comprime: más que esto lo hace pesado para Quick Look


def exportar_usdz(ruta):
    """USDZ animado para iPhone. A diferencia del GLB (Draco), el USDZ guarda la malla sin comprimir: si la pieza
    pasa de USDZ_TRIANGULOS se simplifican solo las mallas pesadas (producto, tapas…) al exportar, y las texturas
    van a 1024 px. Las piezas planas (paneles, gráficas) no se tocan. El .blend no cambia."""
    aviso('PROGRESO', 'Exportando USDZ (iPhone)')
    sc = bpy.context.scene
    mallas = [o for o in sc.objects if o.type == 'MESH']
    tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in mallas}
    total = sum(tris.values())
    pesadas = [o for o in mallas if tris[o.name] > 1500]
    resto = total - sum(tris[o.name] for o in pesadas)
    mods = []
    if total > USDZ_TRIANGULOS and pesadas:
        ratio = max(0.12, min(1.0, (USDZ_TRIANGULOS - resto) / max(1, sum(tris[o.name] for o in pesadas))))
        aviso('PROGRESO', f'USDZ: simplificando {len(pesadas)} piezas pesadas al {round(ratio * 100)} %')
        for o in pesadas:
            m = o.modifiers.new('USDZ_LIGERO', 'DECIMATE')
            m.ratio = ratio
            mods.append((o, m))
    fotograma = sc.frame_current
    sc.frame_set(1)
    bpy.ops.wm.usd_export(
        filepath=ruta, export_animation=True, export_cameras=False, export_lights=False, convert_world_material=False,
        convert_orientation=True, export_global_up_selection='Y', export_global_forward_selection='NEGATIVE_Z',
        usdz_downscale_size='1024', evaluation_mode='RENDER')
    for o, m in mods:
        o.modifiers.remove(m)
    sc.frame_set(fotograma)


def usdz(salida):
    """Solo vuelve a exportar el USDZ de un .blend ya animado (abierto con: blender archivo.blend --python …)."""
    exportar_usdz(salida)
    aviso('OK', f'{os.path.getsize(salida) / 1048576:.1f} MB')


def exportar_vista(salida):
    """vista.glb para la vista previa en vivo del navegador: las mismas piezas con los mismos nombres que usa
    la receta (Mesh_0…), el mapeo ya corregido y sin animación."""
    aviso('PROGRESO', 'Modelo para la vista previa en vivo')
    bpy.ops.export_scene.gltf(
        filepath=os.path.join(salida, 'vista.glb'), export_format='GLB', use_visible=True,
        export_cameras=False, export_lights=False, export_animations=False,
        export_draco_mesh_compression_enable=False, export_yup=True)


def vista(glb, salida):
    importar(glb)
    exportar_vista(salida)
    aviso('OK', 'vista previa lista')


# ── generar ──────────────────────────────────────────────────────
LADOS = {'izquierda': Vector((-1, 0, 0)), 'derecha': Vector((1, 0, 0)),
         'frente': Vector((0, -1, 0)), 'atras': Vector((0, 1, 0))}
DESDE = {'arriba': Vector((0, 0, 1)), 'abajo': Vector((0, 0, -1)), **LADOS}


def tiempos(receta):
    """Inicio y fin (s) de cada grupo, y los pasos (grupos que empiezan "después del anterior")."""
    t_ini = float(receta.get('inicio', 1.0))
    pausa = float(receta.get('pausa', 0.33))
    grupos = receta['grupos']
    res, pasos = [], []
    fin_paso = t_ini
    for i, g in enumerate(grupos):
        dur = max(0.1, float(g.get('duracion', 1.0)))
        ret = max(0.0, float(g.get('retraso', 0.0)))
        if i == 0 or g.get('empieza', 'despues') == 'despues':
            ini = (t_ini if i == 0 else fin_paso + pausa) + ret
            pasos.append({'nombre': g.get('nombre') or f'Paso {len(pasos) + 1}', 'inicio': ini, 'fin': ini + dur})
        else:
            ini = res[-1][0] + ret
        fin = ini + dur
        pasos[-1]['fin'] = max(pasos[-1]['fin'], fin)
        fin_paso = pasos[-1]['fin']
        res.append((ini, fin))
    total = (max(f for _, f in res) if res else t_ini) + float(receta.get('final', 2.0))
    return res, pasos, total


def fotograma(t):
    return int(round(t * FPS)) + 1


def nombre_eje(i, g):
    limpio = ''.join(ch if ch.isalnum() else '_' for ch in (g.get('nombre') or 'grupo'))
    return f'G{i + 1:02d}_{limpio}'[:60]


def generar(proyecto):
    with open(os.path.join(proyecto, 'animacion.json'), encoding='utf-8-sig') as f:   # acepta el BOM de Windows
        receta = json.load(f)
    pieza = receta['pieza']
    glb = os.path.join(proyecto, '02_IMPORTAR', pieza + '.glb')
    aviso('PROGRESO', 'Leyendo la pieza')
    ps = importar(glb)
    por_nombre = {o.name: o for o in ps}
    sc = bpy.context.scene

    # Todo centrado en planta y apoyado en Z = 0: en AR aparece donde se toca
    mn, mx = caja(ps)
    corr = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    for o in [o for o in sc.objects if o.parent is None]:
        o.location -= corr
    bpy.context.view_layer.update()

    def soltar(o):
        # Saca la pieza de su padre (bloques de Rhino) sin moverla
        if o.parent:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw

    def colgar(o, eje):
        soltar(o)
        o.parent = eje
        o.matrix_parent_inverse = Matrix.Translation(eje.location).inverted()

    coleccion = bpy.data.collections.new('ANIMACION')
    sc.collection.children.link(coleccion)

    def nuevo_eje(nombre, pos, tipo='PLAIN_AXES'):
        e = bpy.data.objects.new(nombre, None)
        e.empty_display_type = tipo
        e.empty_display_size = 0.05
        e.location = pos
        coleccion.objects.link(e)
        return e

    def clave(o, f, prop, i, valor):
        getattr(o, prop)[i] = valor
        o.keyframe_insert(prop, index=i, frame=f)

    tiempos_g, pasos, total = tiempos(receta)
    fin = fotograma(total)
    sc.render.fps = FPS
    sc.frame_start, sc.frame_end = 1, fin

    usados = set()
    nodos = {}   # id del grupo → (eje, posición de reposo), para los movimientos encadenados
    for i, g in enumerate(receta['grupos']):
        objs = [por_nombre[n] for n in g.get('piezas', []) if n in por_nombre and n not in usados]
        if not objs:
            aviso('AVISO', f'El grupo «{g.get("nombre")}» no tiene piezas: se omite')
            continue
        usados.update(o.name for o in objs)
        a, b = caja(objs)
        f0, f1 = fotograma(tiempos_g[i][0]), fotograma(tiempos_g[i][1])
        tipo = g.get('tipo', 'entrar')
        if tipo in ('bisagra', 'girar'):
            if g.get('pivote') and g.get('eje'):
                # Bisagra puesta con el gumball de la vista previa: cualquier punto y cualquier dirección
                pos, eje = Vector(g['pivote']), Vector(g['eje']).normalized()
            elif tipo == 'bisagra':
                d = LADOS[g.get('lado', 'izquierda')]
                z = b.z if g.get('borde') == 'arriba' else a.z
                pos = Vector(((a.x if d.x < 0 else b.x) if d.x else (a.x + b.x) / 2,
                              (a.y if d.y < 0 else b.y) if d.y else (a.y + b.y) / 2, z))
                eje = Vector((0, 0, 1)).cross(d)       # girar sobre él lleva lo de arriba hacia "d"
            else:
                pos, eje = Vector(((a.x + b.x) / 2, (a.y + b.y) / 2, a.z)), Vector((0, 0, 1))
            e = nuevo_eje(nombre_eje(i, g), pos, 'ARROWS')
            for o in objs:
                colgar(o, e)
            # Eje y ángulo: el ángulo se interpola tal cual (sirve para más de media vuelta) y el eje no cambia
            e.rotation_mode = 'AXIS_ANGLE'
            ang = math.radians(float(g.get('angulo', 90)))
            for f, valor in ((f1, 0.0), (f0, ang)):
                e.rotation_axis_angle = (valor, eje.x, eje.y, eje.z)
                e.keyframe_insert('rotation_axis_angle', frame=f)
        else:  # entrar
            pos = Vector(((a.x + b.x) / 2, (a.y + b.y) / 2, a.z))
            e = nuevo_eje(nombre_eje(i, g), pos)
            for o in objs:
                colgar(o, e)
            if g.get('desplazamiento'):
                inicio = pos + Vector(g['desplazamiento'])   # punto de partida puesto con el gumball
            else:
                inicio = pos + DESDE[g.get('desde', 'arriba')] * (float(g.get('distancia', 30)) / 100)
            for k in range(3):
                clave(e, f1, 'location', k, pos[k])
                clave(e, f0, 'location', k, inicio[k])
            if g.get('aparece', True) and f0 > 1:   # si arranca en el fotograma 1 ya está a la vista
                for k in range(3):
                    clave(e, f0 - 1, 'scale', k, MINIMA)
                    clave(e, f0, 'scale', k, 1)
        if g.get('id'):
            nodos[g['id']] = (e, pos.copy())

    # «Se mueve con»: el eje del grupo cuelga del eje de su padre, así viaja con él (los laterales pegados al
    # espaldar se levantan con él) y su propio movimiento se suma encima. Las bisagras y los desplazamientos se
    # definieron con todo armado, donde el padre no tiene giro: basta con compensar su posición de reposo
    padres = {g['id']: g.get('padre') for g in receta['grupos'] if g.get('id')}
    for gid, pid in padres.items():
        if gid not in nodos or pid not in nodos:
            continue
        x, visto = pid, {gid}
        while x and x not in visto:   # sin círculos
            visto.add(x)
            x = padres.get(x)
        if x:
            aviso('AVISO', 'Movimiento encadenado en círculo: se ignora')
            continue
        hijo, _ = nodos[gid]
        padre, pos_padre = nodos[pid]
        hijo.parent = padre
        hijo.matrix_parent_inverse = Matrix.Translation(pos_padre).inverted()

    # Lo que no está en ningún grupo queda fijo; sus claves en el primer y último fotograma hacen que la
    # animación exportada conserve el inicio quieto y la pausa final
    fijo = nuevo_eje('FIJO', Vector((0, 0, 0)))
    for o in ps:
        if o.name not in usados:
            colgar(o, fijo)
    for f in (1, fin):
        fijo.keyframe_insert('location', frame=f)
    # Lo que no es malla (nodos vacíos de Rhino) también va al eje fijo
    for o in [o for o in sc.objects if o.parent is None and o.type == 'EMPTY' and o.name not in coleccion.objects]:
        mw = o.matrix_world.copy()
        o.parent = fijo
        o.matrix_parent_inverse = Matrix.Identity(4)
        o.matrix_world = mw

    for p in pasos:
        sc.timeline_markers.new(p['nombre'], frame=fotograma(p['inicio']))
    sc.timeline_markers.new('FINAL', frame=fotograma(max(p['fin'] for p in pasos)) if pasos else 1)

    # Cámara y luz para el video de vista previa (no se exportan)
    mn2, mx2 = caja(ps)
    preparar_workbench()
    sc.display.render_aa = '8'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.view_settings.exposure = 1.2
    cam = camara('frente_iso', mn2, mx2, 900, 700)
    cam.name = 'CAM_PREVIEW'
    cam.data.ortho_scale *= 1.35  # que quepa el troquel abierto

    carpeta_prev = os.path.join(proyecto, '06_PREVIEW')
    os.makedirs(carpeta_prev, exist_ok=True)
    for viejo in os.listdir(carpeta_prev):
        if viejo.startswith('paso_') and viejo.endswith('.png'):
            os.remove(os.path.join(carpeta_prev, viejo))
    momentos = [('paso_00_inicio', 1)] + [(f'paso_{n + 1:02d}', fotograma(p['fin'])) for n, p in enumerate(pasos)]
    for nombre, f in momentos:
        aviso('PROGRESO', f'Imagen {nombre}')
        sc.frame_set(f)
        foto(os.path.join(carpeta_prev, nombre + '.png'))

    sol = bpy.data.objects.new('LUZ_PREVIEW', bpy.data.lights.new('LUZ_PREVIEW', 'SUN'))
    sol.data.energy = 3
    sol.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    sc.collection.objects.link(sol)
    sc.render.film_transparent = False
    sc.view_settings.exposure = 0
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    if sc.world is None:
        sc.world = bpy.data.worlds.new('Mundo')
    sc.world.color = (0.8, 0.8, 0.8)
    fondo = sc.world.node_tree.nodes.get('Background') if sc.world.node_tree else None
    if fondo:
        fondo.inputs['Color'].default_value = (0.8, 0.8, 0.8, 1)
        fondo.inputs['Strength'].default_value = 0.6
    try:
        sc.render.image_settings.media_type = 'VIDEO'
        sc.render.image_settings.file_format = 'FFMPEG'
        sc.render.ffmpeg.format = 'MPEG4'
        sc.render.ffmpeg.codec = 'H264'
    except Exception:
        pass
    sc.render.filepath = '//../06_PREVIEW/' + pieza + '_ANIM_'
    for scr in bpy.data.screens:
        for area in scr.areas:
            if area.type == 'VIEW_3D':
                area.spaces[0].shading.type = 'MATERIAL'

    sc.frame_set(1)
    os.makedirs(os.path.join(proyecto, '04_BLENDER'), exist_ok=True)
    aviso('PROGRESO', 'Guardando el archivo de Blender')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(proyecto, '04_BLENDER', pieza + '_ANIM.blend'))

    aviso('PROGRESO', 'Exportando GLB')
    os.makedirs(os.path.join(proyecto, '05_EXPORTAR', 'GLB'), exist_ok=True)
    os.makedirs(os.path.join(proyecto, '05_EXPORTAR', 'USDZ'), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=os.path.join(proyecto, '05_EXPORTAR', 'GLB', pieza + '_ANIM.glb'), export_format='GLB',
        use_visible=True, export_cameras=False, export_lights=False, export_animations=True,
        export_animation_mode='ACTIVE_ACTIONS', export_force_sampling=True,
        export_optimize_animation_size=False,  # si no, borra las claves quietas de FIJO y se pierde el final
        export_draco_mesh_compression_enable=False, export_yup=True)
    exportar_usdz(os.path.join(proyecto, '05_EXPORTAR', 'USDZ', pieza + '_ANIM.usdz'))

    resumen = {'duracion': round(total, 3), 'fotogramas': fin, 'fps': FPS,
               'pasos': [{'nombre': p['nombre'], 'inicio': round(p['inicio'], 3), 'fin': round(p['fin'], 3)} for p in pasos],
               'fijas': len(ps) - len(usados), 'animadas': len(usados)}
    with open(os.path.join(proyecto, '05_EXPORTAR', 'resumen.json'), 'w', encoding='utf-8') as f:
        json.dump(resumen, f, ensure_ascii=False, indent=1)
    aviso('OK', f'{len(pasos)} pasos · {total:.1f} s')


if __name__ == '__main__':
    try:
        sys.stdout.reconfigure(encoding='utf-8')   # nombres con tildes en los mensajes para la ventana
    except Exception:
        pass
    args = sys.argv[sys.argv.index('--') + 1:]
    try:
        if args[0] == 'analizar':
            analizar(args[1], args[2])
        elif args[0] == 'generar':
            generar(args[1])
        elif args[0] == 'vista':
            vista(args[1], args[2])
        elif args[0] == 'usdz':
            usdz(args[1])
        elif args[0] == 'vistas_sin':
            vistas_sin(args[1], args[2], args[3])
        else:
            raise RuntimeError('Comando desconocido: ' + args[0])
    except Exception as ex:
        aviso('ERROR', str(ex))
        raise

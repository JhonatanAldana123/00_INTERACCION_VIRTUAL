<#
.SYNOPSIS
  Optimiza una pieza .glb exportada desde Rhino y la guarda dentro de su campaña.

.DESCRIPTION
  Estructura de models\ (cada carpeta raíz es una campaña; cada subcarpeta, una pieza):
    models\<Campaña>\<Pieza>\<Pieza>.glb             optimizado (se publica)
    models\<Campaña>\<Pieza>\_original\<Pieza>.glb   exportación de Rhino (no se sube a GitHub)

  1. Guarda una copia del original en models\<Campaña>\<Pieza>\_original\
  2. Repara el mapeo (UV) de objetos copiados, borra datos sin usar y une los objetos que
     comparten material (reparar-uv.js + prune + dedup + flatten + join)
  3. Limita las texturas a 2048 px (resize)
  4. Convierte las texturas PNG a JPEG, salvo que haya materiales transparentes (jpeg)
  5. Comprime la geometría con Draco (draco)
  6. Actualiza colecciones.json (scripts\generar-colecciones.js) y muestra los enlaces para el QR

  Usa gltf-transform (se descarga solo la primera vez con npx). Requiere Node.js.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\optimizar-modelo.ps1 "C:\Exportes\Exhibidor01.glb" -Campana "Campaña Alpina"

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\optimizar-modelo.ps1 "C:\Exportes\export.glb" -Campana "Campaña Alpina" -Nombre Bandeja01
#>
param(
  [Parameter(Mandatory = $true)][string]$Entrada,   # .glb exportado desde Rhino
  [string]$Campana,                                 # carpeta de la campaña (se pregunta si falta)
  [string]$Nombre,                                  # nombre de la pieza (por defecto, el del archivo)
  [int]$MaxTextura = 2048,                          # lado máximo de las texturas en píxeles
  [int]$CalidadJpeg = 85,                           # 1-100
  [switch]$SinPreguntas                             # desde Flujo AR: nunca esperar respuesta en la consola
)

$ErrorActionPreference = 'Stop'
# UTF-8 para leer bien la salida de node (nombres de campaña con tildes y ñ)
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$GltfTransform = '@gltf-transform/cli@4.5.1'   # versión fija para que el resultado sea siempre igual
$UrlBase = 'https://jhonatanaldana123.github.io/00_INTERACCION_VIRTUAL/'
# La dirección del sitio publicado se configura en sitio.json
try {
  $sitio = Get-Content (Join-Path (Split-Path -Parent $PSScriptRoot) 'sitio.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  if ($sitio.url -like 'http*') { $UrlBase = $sitio.url.TrimEnd('/') + '/' }
} catch { }

$Proyecto = Split-Path -Parent $PSScriptRoot
$Models = Join-Path $Proyecto 'models'
$Analizador = Join-Path $PSScriptRoot 'analizar-glb.js'
$Generador = Join-Path $PSScriptRoot 'generar-colecciones.js'

function Analizar($ruta) { node $Analizador $ruta | ConvertFrom-Json }
function MB($bytes) { '{0:N2} MB' -f ($bytes / 1MB) }
function Paso($texto) { Write-Host "`n>> $texto" -ForegroundColor Cyan }
function Ejecutar($argumentos) {
  & npx --yes $GltfTransform @argumentos
  if ($LASTEXITCODE -ne 0) { throw "Falló gltf-transform: $($argumentos -join ' ')" }
}
# Nombres libres (tildes y espacios valen) pero que sirvan como nombre de carpeta
function Validar($valor, $que) {
  $v = "$valor".Trim().TrimEnd('.')
  if (-not $v) { throw "Falta el nombre de la $que." }
  if ($v.StartsWith('_') -or $v.StartsWith('.')) { throw "El nombre de la $que no puede empezar con _ ni con punto: '$v'." }
  if ($v.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw "El nombre de la $que tiene caracteres no permitidos (\ / : * ? `" < > |): '$v'." }
  return $v
}

# ── Validaciones ───────────────────────────────────────────
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
  throw 'No se encontró Node.js. Instálalo desde https://nodejs.org (versión LTS) y vuelve a intentar.'
}
if (-not (Test-Path $Entrada)) { throw "No existe el archivo: $Entrada" }
$Entrada = (Resolve-Path $Entrada).Path
if ([IO.Path]::GetExtension($Entrada) -ne '.glb') { throw 'El archivo debe ser .glb' }
if (-not $Nombre) { $Nombre = [IO.Path]::GetFileNameWithoutExtension($Entrada) }
$Nombre = Validar $Nombre 'pieza'

if (-not $Campana -and $SinPreguntas) { throw 'Falta el nombre de la campaña.' }
if (-not $Campana) {
  # Desde optimizar.bat (arrastrar y soltar) se pregunta en la ventana de consola
  $existentes = @(Get-ChildItem $Models -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -notmatch '^[_.]' } | ForEach-Object Name)
  if ($existentes) { Write-Host "Campañas existentes: $($existentes -join ', ')" }
  $Campana = Read-Host "¿En qué campaña va la pieza '$Nombre'? (escribe el nombre; Enter = '$Nombre')"
  if (-not $Campana) { $Campana = $Nombre }
}
$Campana = Validar $Campana 'campaña'

$CarpetaPieza = Join-Path (Join-Path $Models $Campana) $Nombre
$Originales = Join-Path $CarpetaPieza '_original'
$Salida = Join-Path $CarpetaPieza "$Nombre.glb"
$Respaldo = Join-Path $Originales "$Nombre.glb"
$Temp = Join-Path $env:TEMP ("optimizar-glb-" + [guid]::NewGuid().ToString('N'))

$antes = Analizar $Entrada
if ($antes.draco) {
  throw 'Este archivo ya está optimizado (tiene compresión Draco). Usa el .glb original exportado desde Rhino.'
}

Write-Host "Pieza:    $Entrada"
Write-Host "Campaña:  $Campana"
Write-Host "Destino:  models\$Campana\$Nombre\$Nombre.glb"
Write-Host "Inicial:  $(MB $antes.bytes), $($antes.triangulos) triangulos, $($antes.texturas.Count) texturas"

# ── 1. Respaldo ────────────────────────────────────────────
Paso "1/5 Guardando copia del original en models\$Campana\$Nombre\_original\"
New-Item -ItemType Directory -Force $Originales | Out-Null
if ($Entrada -ne $Respaldo) { Copy-Item $Entrada $Respaldo -Force }
New-Item -ItemType Directory -Force $Temp | Out-Null

# ── 2. Mapeo de copias y datos sin usar ────────────────────
# Rhino a veces exporta el mapeo (UV) solo en uno de varios objetos copiados: se copia a los demás
Paso '2/5 Reparando mapeo de copias, borrando datos sin usar y uniendo objetos'
$reparacion = node (Join-Path $PSScriptRoot 'reparar-uv.js') $Respaldo "$Temp\0.glb" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Falló la reparación del mapeo de texturas.' }
if ($reparacion.reparados -gt 0) { Write-Host "   Mapeo reparado en $($reparacion.reparados) objetos copiados" -ForegroundColor Green }
if ($reparacion.lineasQuitadas -gt 0) { Write-Host "   Se quitaron $($reparacion.lineasQuitadas) lineas o puntos sueltos (el AR no los acepta)" -ForegroundColor Green }
Ejecutar @('prune', "$Temp\0.glb", "$Temp\p.glb")
# Une los objetos que comparten material: la forma no cambia, pero miles de objetos sueltos
# inflan el archivo (una ficha por objeto) y obligan al celular a dibujarlos uno por uno
Ejecutar @('dedup', "$Temp\p.glb", "$Temp\d.glb")
Ejecutar @('flatten', "$Temp\d.glb", "$Temp\f.glb")
Ejecutar @('join', "$Temp\f.glb", "$Temp\j.glb")
# Centra la pieza en el origen y apoya su base en el piso (y = 0): en AR aparece donde el cliente
# toca, sin quedar corrida ni hundida en el suelo
Ejecutar @('center', "$Temp\j.glb", "$Temp\1.glb", '--pivot', 'below')

# ── 3. Tamaño de texturas ──────────────────────────────────
Paso "3/5 Limitando texturas a $MaxTextura px"
Ejecutar @('resize', "$Temp\1.glb", "$Temp\2.glb", '--width', $MaxTextura, '--height', $MaxTextura)

# ── 4. PNG → JPEG ──────────────────────────────────────────
# JPEG no guarda transparencia: si algún material la usa, se dejan las texturas en PNG
if ($antes.transparentes.Count -gt 0) {
  Paso '4/5 Se omite la conversion a JPEG: hay materiales con transparencia'
  Write-Host "   Materiales: $($antes.transparentes -join ', ')" -ForegroundColor Yellow
  Copy-Item "$Temp\2.glb" "$Temp\3.glb" -Force
} else {
  Paso "4/5 Convirtiendo texturas PNG a JPEG (calidad $CalidadJpeg)"
  Ejecutar @('jpeg', "$Temp\2.glb", "$Temp\3.glb", '--formats', 'png', '--quality', $CalidadJpeg)
}

# ── 5. Draco ───────────────────────────────────────────────
Paso '5/5 Comprimiendo geometria con Draco'
Ejecutar @('draco', "$Temp\3.glb", $Salida)
Remove-Item $Temp -Recurse -Force

# ── Campañas: actualiza colecciones.json ───────────────────
$resumen = node $Generador | ConvertFrom-Json
$colecciones = Get-Content (Join-Path $Proyecto 'colecciones.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$enlace = $null; $idPieza = $null; $total = 0
foreach ($p in $colecciones.PSObject.Properties) {
  if ($p.Value.carpeta -eq $Campana) {
    $enlace = $p.Name
    $total = @($p.Value.modelos).Count
    $idPieza = (@($p.Value.modelos) | Where-Object { $_.nombre -eq $Nombre } | Select-Object -First 1).id
  }
}

# ── Reporte ────────────────────────────────────────────────
$despues = Analizar $Salida
$ahorro = [math]::Round((1 - $despues.bytes / $antes.bytes) * 100)
$texMax = ($despues.texturas | ForEach-Object { [math]::Max($_.ancho, $_.alto) } | Measure-Object -Maximum).Maximum

Write-Host "`n===================== RESULTADO =====================" -ForegroundColor Green
Write-Host "Peso:        $(MB $antes.bytes)  ->  $(MB $despues.bytes)   (-$ahorro %)"
Write-Host "Triangulos:  $($despues.triangulos)"
Write-Host "Texturas:    $(MB $despues.bytesTexturas) en $($despues.texturas.Count) imagenes (maximo $texMax px)"
Write-Host "Archivo:     models\$Campana\$Nombre\$Nombre.glb"
if ($enlace) {
  Write-Host "Campaña:     $Campana ($total pieza$(if ($total -ne 1) { 's' }))"
  # Dirección limpia si el generador creó la página (carpeta <enlace>\index.html)
  $limpia = Test-Path (Join-Path (Join-Path $Proyecto $enlace) 'index.html')
  Write-Host "QR campaña:  $(if ($limpia) { "$UrlBase$enlace/" } else { "$UrlBase`?coleccion=$enlace" })"
  if ($total -gt 1 -and $idPieza) {
    Write-Host "QR pieza:    $(if ($limpia) { "$UrlBase$enlace/$idPieza/" } else { "$UrlBase`?coleccion=$enlace&pieza=$idPieza" })"
  }
}
foreach ($a in @($resumen.avisos)) { if ($a) { Write-Host "AVISO: $a" -ForegroundColor Yellow } }

# Objetos sin material en Rhino: el exportador les pone negro metálico y se ven oscuros
$sinMaterial = @($antes.sinMaterial)
if ($sinMaterial.Count -gt 0) {
  $tris = ($sinMaterial | Measure-Object -Property triangulos -Sum).Sum
  Write-Host "AVISO: hay objetos sin material asignado en Rhino ($tris triangulos): se veran negros. Asignales un material (con Metalico en 0) y exporta de nuevo." -ForegroundColor Yellow
}

# Objetos con textura pero sin mapeo (UV) que no se pudieron reparar (no son copias de otro con mapeo)
if ($reparacion.sinReparar -gt 0) {
  Write-Host "AVISO: $($reparacion.sinReparar) objetos tienen textura pero no tienen mapeo (UV): su imagen no se vera. En Rhino, aplicales el mapeo de textura y exporta de nuevo." -ForegroundColor Yellow
}

# Avisos si el modelo sigue pesado: esto ya se corrige en Rhino, no en el script
if ($despues.triangulos -gt 100000) {
  Write-Host "`nAVISO: mas de 100.000 triangulos. Baja la densidad de malla en Rhino (ver docs\OPTIMIZAR_MODELOS.md)." -ForegroundColor Yellow
}
if ($despues.bytes -gt 5MB) {
  Write-Host "`nAVISO: pesa mas de 5 MB. Revisa la malla y las texturas en Rhino." -ForegroundColor Yellow
}
Write-Host "`nSiguiente paso: revisa la pieza en el visor y publicala con GitHub Desktop (commit + push)."

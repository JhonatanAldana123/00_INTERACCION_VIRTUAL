<#
.SYNOPSIS
  Prepara la página de prueba en celulares de una pieza animada (la llama el animador de Flujo AR).

.DESCRIPTION
  Toma lo que exportó el animador en <Proyecto>\05_EXPORTAR\ y deja en pruebas\<Enlace>\:
    <Enlace>.glb    comprimido (texturas de 2048 px como máximo, JPEG y Draco). No se unen objetos ni se
                    centra: eso rompería la animación (el animador ya dejó la pieza centrada y en el piso)
    <Enlace>.usdz   el de Blender, animado, para iPhone
    index.html      desde herramientas\plantillas\prueba-animacion.html, con los pasos de resumen.json
  Esta página no la usa ningún QR de campaña: sirve para probar el AR antes de llevar la animación al visor.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\preparar-prueba-animacion.ps1 -Proyecto "001_ANIMACIÓN\01_Bandeja_Yogo" -Enlace bandeja-yogo -Titulo "Bandeja Yogo"
#>
param(
  [Parameter(Mandatory = $true)][string]$Proyecto,   # carpeta del animador (con animacion.json)
  [Parameter(Mandatory = $true)][string]$Enlace,     # nombre de la carpeta en pruebas\ (sin tildes ni espacios)
  [string]$Titulo,
  [int]$MaxTextura = 2048,
  [int]$CalidadJpeg = 85
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
$GltfTransform = '@gltf-transform/cli@4.5.1'
$Raiz = Split-Path -Parent $PSScriptRoot
$Utf8 = New-Object Text.UTF8Encoding $false

function Ejecutar($argumentos) {
  & npx --yes $GltfTransform @argumentos
  if ($LASTEXITCODE -ne 0) { throw "Falló gltf-transform: $($argumentos -join ' ')" }
}

if ($Enlace -notmatch '^[a-z0-9][a-z0-9-]*$') { throw "El enlace solo puede tener minúsculas, números y guiones: '$Enlace'." }
$Proyecto = (Resolve-Path $Proyecto).Path
$receta = Get-Content (Join-Path $Proyecto 'animacion.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$pieza = $receta.pieza
if (-not $Titulo) { $Titulo = $pieza }
$glb = Join-Path $Proyecto "05_EXPORTAR\GLB\${pieza}_ANIM.glb"
$usdz = Join-Path $Proyecto "05_EXPORTAR\USDZ\${pieza}_ANIM.usdz"
$rutaResumen = Join-Path $Proyecto '05_EXPORTAR\resumen.json'
foreach ($f in $glb, $usdz, $rutaResumen) {
  if (-not (Test-Path $f)) { throw "Falta $f. Genera la animación primero." }
}
$resumen = Get-Content $rutaResumen -Raw -Encoding UTF8 | ConvertFrom-Json

$destino = Join-Path $Raiz "pruebas\$Enlace"
New-Item -ItemType Directory -Force $destino | Out-Null
$temp = Join-Path $env:TEMP ("prueba-anim-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null

try {
  Write-Host ">> Comprimiendo el GLB (texturas $MaxTextura px, JPEG, Draco)"
  Ejecutar @('resize', $glb, "$temp\r.glb", '--width', $MaxTextura, '--height', $MaxTextura)
  $analisis = node (Join-Path $PSScriptRoot 'analizar-glb.js') $glb | ConvertFrom-Json
  if (@($analisis.transparentes).Count -gt 0) {
    Write-Host '   Hay materiales con transparencia: las texturas quedan en PNG'
    Copy-Item "$temp\r.glb" "$temp\j.glb"
  } else {
    Ejecutar @('jpeg', "$temp\r.glb", "$temp\j.glb", '--formats', 'png', '--quality', $CalidadJpeg)
  }
  Ejecutar @('draco', "$temp\j.glb", (Join-Path $destino "$Enlace.glb"))
  Copy-Item $usdz (Join-Path $destino "$Enlace.usdz") -Force
}
finally {
  Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

# Versión: cambia cuando cambian los archivos, para que ningún celular use una copia vieja
$sha = [Security.Cryptography.SHA1]::Create()
$bytes = [IO.File]::ReadAllBytes((Join-Path $destino "$Enlace.glb")) + [IO.File]::ReadAllBytes((Join-Path $destino "$Enlace.usdz"))
$version = ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').Substring(0, 10).ToLower()

function JsTexto($s) { "'" + ($s -replace '\\', '\\' -replace "'", "\'") + "'" }
function Html($s) { [Net.WebUtility]::HtmlEncode($s) }
$inv = [Globalization.CultureInfo]::InvariantCulture
$pasos = @("[0, 'Inicio']")
$n = 0
foreach ($p in @($resumen.pasos)) {
  $n++
  $pasos += "[" + ([double]$p.inicio).ToString('0.###', $inv) + ", " + (JsTexto "Paso $n · $($p.nombre)") + "]"
}
if ($n -gt 0) {
  $fin = (@($resumen.pasos) | ForEach-Object { [double]$_.fin } | Measure-Object -Maximum).Maximum
  $pasos += "[" + $fin.ToString('0.###', $inv) + ", 'Lista para exhibir']"
}

$plantilla = [IO.File]::ReadAllText((Join-Path $Raiz 'herramientas\plantillas\prueba-animacion.html'), [Text.Encoding]::UTF8)
$html = $plantilla.Replace('__TITULO__', (Html $Titulo)).Replace('__VERSION__', $version).Replace('__ARCHIVO__', $Enlace).Replace('__PASOS__', '[' + ($pasos -join ', ') + ']')
[IO.File]::WriteAllText((Join-Path $destino 'index.html'), $html, $Utf8)

$pesoGlb = '{0:N2} MB' -f ((Get-Item (Join-Path $destino "$Enlace.glb")).Length / 1MB)
$pesoUsdz = '{0:N2} MB' -f ((Get-Item (Join-Path $destino "$Enlace.usdz")).Length / 1MB)
Write-Host ">> Listo: pruebas\$Enlace\ (GLB $pesoGlb · USDZ $pesoUsdz)"
Write-Host "@@PAGINA pruebas/$Enlace/"

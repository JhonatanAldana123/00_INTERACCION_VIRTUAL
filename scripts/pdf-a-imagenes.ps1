<#
  Convierte cada página de un PDF en una imagen JPG (pagina-01.jpg, pagina-02.jpg…).
  Lo usa el paso «Tarjetas AR» de Flujo AR para que la presentación en PDF se vea en el celular
  como una secuencia de imágenes, sin cargar un lector de PDF en la página.

  Usa el lector de PDF que trae Windows (Windows.Data.Pdf): no hay que instalar nada.

    powershell -ExecutionPolicy Bypass -File scripts\pdf-a-imagenes.ps1 -Pdf C:\ruta\archivo.pdf -Destino tarjetas\1

  Escribe en la salida "PAGINAS=<n>" al terminar bien.
#>
param(
  [Parameter(Mandatory = $true)][string]$Pdf,
  [Parameter(Mandatory = $true)][string]$Destino,
  [int]$Lado = 1600,        # píxeles del lado más largo de cada página
  [int]$Maximo = 30,        # páginas como mucho
  [int]$Calidad = 86
)
$ErrorActionPreference = 'Stop'

try {
  $Pdf = (Resolve-Path -LiteralPath $Pdf).Path
  New-Item -ItemType Directory -Force -Path $Destino | Out-Null
  $Destino = (Resolve-Path -LiteralPath $Destino).Path

  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  Add-Type -AssemblyName System.Drawing
  $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
  $null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
  $null = [Windows.Data.Pdf.PdfPageRenderOptions, Windows.Data.Pdf, ContentType = WindowsRuntime]
  $null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]

  # Las funciones de Windows son asíncronas: se esperan con AsTask
  $metodos = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 }
  $conResultado = $metodos | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
  $sinResultado = $metodos | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' } | Select-Object -First 1
  function Esperar($operacion, [Type]$tipo) {
    $tarea = $conResultado.MakeGenericMethod($tipo).Invoke($null, @($operacion))
    $tarea.Wait(-1) | Out-Null
    $tarea.Result
  }
  function EsperarAccion($operacion) {
    $tarea = $sinResultado.Invoke($null, @($operacion))
    $tarea.Wait(-1) | Out-Null
  }

  $archivo = Esperar ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Pdf)) ([Windows.Storage.StorageFile])
  $doc = Esperar ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($archivo)) ([Windows.Data.Pdf.PdfDocument])
  $total = [Math]::Min([int]$doc.PageCount, $Maximo)
  if ($total -lt 1) { throw 'El PDF no tiene páginas.' }

  $jpg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
  $ajustes = New-Object System.Drawing.Imaging.EncoderParameters 1
  $ajustes.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), ([long]$Calidad)

  # Páginas de una conversión anterior
  Get-ChildItem -LiteralPath $Destino -Filter 'pagina-*.jpg' | Remove-Item -Force -Confirm:$false

  for ($i = 0; $i -lt $total; $i++) {
    $pagina = $doc.GetPage([uint32]$i)
    $opciones = New-Object Windows.Data.Pdf.PdfPageRenderOptions
    if ($pagina.Size.Width -ge $pagina.Size.Height) { $opciones.DestinationWidth = [uint32]$Lado }
    else { $opciones.DestinationHeight = [uint32]$Lado }
    $flujo = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
    EsperarAccion ($pagina.RenderToStreamAsync($flujo, $opciones))
    $net = [System.IO.WindowsRuntimeStreamExtensions]::AsStream($flujo)
    $imagen = [System.Drawing.Image]::FromStream($net)
    # Fondo blanco (el JPG no tiene transparencia)
    # Windows multiplica el tamaño pedido por la escala de la pantalla (en una pantalla al 250 % salían páginas de
    # 4000 px, que en el celular pesan 36 MB cada una y traban la presentación): aquí se lleva al tamaño exacto.
    $factor = [Math]::Min(1.0, $Lado / [Math]::Max($imagen.Width, $imagen.Height))
    $ancho = [int][Math]::Round($imagen.Width * $factor); $alto = [int][Math]::Round($imagen.Height * $factor)
    $lienzo = New-Object System.Drawing.Bitmap $ancho, $alto, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($lienzo)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::White)
    $g.DrawImage($imagen, 0, 0, $ancho, $alto)
    $g.Dispose()
    $lienzo.Save((Join-Path $Destino ('pagina-{0:00}.jpg' -f ($i + 1))), $jpg, $ajustes)
    $lienzo.Dispose(); $imagen.Dispose(); $net.Dispose(); $flujo.Dispose(); $pagina.Dispose()
  }
  Write-Output "PAGINAS=$total"
  exit 0
}
catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}

<#
  Convierte un video a MP4 H.264 (el formato que reproducen los celulares) con el codificador que trae Windows.
  Lo usa scripts\animar-presentacion.py: OpenCV solo sabe escribir un formato intermedio que los navegadores no abren.

    powershell -ExecutionPolicy Bypass -File scripts\video-a-h264.ps1 -Entrada temp.avi -Salida final.mp4
#>
param(
  [Parameter(Mandatory = $true)][string]$Entrada,
  [Parameter(Mandatory = $true)][string]$Salida,
  [int]$Bitrate = 2600000,
  [int]$Ancho = 1280,
  [int]$Alto = 720,
  [int]$Fps = 30
)
$ErrorActionPreference = 'Stop'
try {
  $Entrada = (Resolve-Path -LiteralPath $Entrada).Path
  $carpeta = Split-Path -Parent ([IO.Path]::GetFullPath($Salida))
  $nombre = Split-Path -Leaf $Salida

  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
  $null = [Windows.Storage.StorageFolder, Windows.Storage, ContentType = WindowsRuntime]
  $null = [Windows.Media.Transcoding.MediaTranscoder, Windows.Media.Transcoding, ContentType = WindowsRuntime]
  $null = [Windows.Media.MediaProperties.MediaEncodingProfile, Windows.Media.MediaProperties, ContentType = WindowsRuntime]

  $metodos = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 }
  $conResultado = $metodos | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
  $conAvance = $metodos | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncActionWithProgress`1' } | Select-Object -First 1
  function Esperar($operacion, [Type]$tipo) {
    $tarea = $conResultado.MakeGenericMethod($tipo).Invoke($null, @($operacion))
    $tarea.Wait(-1) | Out-Null
    $tarea.Result
  }

  $origen = Esperar ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Entrada)) ([Windows.Storage.StorageFile])
  $dir = Esperar ([Windows.Storage.StorageFolder]::GetFolderFromPathAsync($carpeta)) ([Windows.Storage.StorageFolder])
  $destino = Esperar ($dir.CreateFileAsync($nombre, [Windows.Storage.CreationCollisionOption]::ReplaceExisting)) ([Windows.Storage.StorageFile])

  $perfil = [Windows.Media.MediaProperties.MediaEncodingProfile]::CreateMp4([Windows.Media.MediaProperties.VideoEncodingQuality]::HD720p)
  $perfil.Audio = $null
  $perfil.Video.Width = [uint32]$Ancho
  $perfil.Video.Height = [uint32]$Alto
  $perfil.Video.Bitrate = [uint32]$Bitrate
  $perfil.Video.FrameRate.Numerator = [uint32]$Fps
  $perfil.Video.FrameRate.Denominator = [uint32]1

  $transcodificador = New-Object Windows.Media.Transcoding.MediaTranscoder
  $transcodificador.HardwareAccelerationEnabled = $true
  $listo = Esperar ($transcodificador.PrepareFileTranscodeAsync($origen, $destino, $perfil)) ([Windows.Media.Transcoding.PrepareTranscodeResult])
  if (-not $listo.CanTranscode) { throw "Windows no puede convertir este video (motivo: $($listo.FailureReason))." }
  $tarea = $conAvance.MakeGenericMethod([double]).Invoke($null, @($listo.TranscodeAsync()))
  $tarea.Wait(-1) | Out-Null
  Write-Output "H264=$((Get-Item -LiteralPath (Join-Path $carpeta $nombre)).Length)"
  exit 0
}
catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}

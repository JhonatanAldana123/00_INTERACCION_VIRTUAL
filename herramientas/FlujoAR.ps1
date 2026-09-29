<#
  Abre la ventana de Flujo AR. Se ejecuta desde FlujoAR.bat (en la raíz del proyecto).

  El código C# de herramientas\FlujoAR\ se compila en memoria con Add-Type: así no hay un .exe
  sin firma que Smart App Control de Windows pueda bloquear.
#>
$ErrorActionPreference = 'Stop'
$Proyecto = Split-Path -Parent $PSScriptRoot

try {
  $fuentes = Get-ChildItem (Join-Path $PSScriptRoot 'FlujoAR') -Filter *.cs | ForEach-Object { $_.FullName }
  Add-Type -Path $fuentes -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Web.Extensions
  [FlujoAR.Programa]::Main(@($Proyecto))
}
catch {
  Add-Type -AssemblyName System.Windows.Forms
  [System.Windows.Forms.MessageBox]::Show("No se pudo abrir Flujo AR:`n`n$($_.Exception.Message)", 'Flujo AR') | Out-Null
  exit 1
}

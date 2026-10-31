$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Compilador .NET Framework não encontrado.' }
$source = Join-Path $PSScriptRoot 'AioAssist.cs'
$output = Join-Path $PSScriptRoot 'AIO-Assist-0.6.1.exe'
$offlineSource = Join-Path $PSScriptRoot 'OfflineCompare.cs'
$parameterSource = Join-Path $PSScriptRoot 'AioParameters.cs'
$parameterTests = Join-Path $PSScriptRoot 'AioParameterTests.cs'
$map = Join-Path $PSScriptRoot 'references\aio-b1.json'
$translations = Join-Path $PSScriptRoot 'references\aio-pt.json'
$offlineView = Join-Path $PSScriptRoot 'AioOfflineView.cs'
$offlineViewTests = Join-Path $PSScriptRoot 'AioOfflineViewTests.cs'
$crossSource = Join-Path $PSScriptRoot 'AioCrossView.cs'
$crossTests = Join-Path $PSScriptRoot 'AioCrossTests.cs'
$crossMap = Join-Path $PSScriptRoot 'references\aio-nac-cross.json'
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 "/out:$output" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/resource:$map,aio-b1.json" "/resource:$translations,aio-pt.json" "/resource:$crossMap,aio-nac-cross.json" $source $offlineSource $parameterSource $parameterTests $offlineView $offlineViewTests $crossSource $crossTests
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
Copy-Item -LiteralPath $output -Destination (Join-Path $PSScriptRoot 'AIO-Consulta-Offline-0.6.1.exe') -Force
Write-Output $output

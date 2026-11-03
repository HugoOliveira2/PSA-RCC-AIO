param(
    [string]$ArduinoRoot = "${env:ProgramFiles(x86)}\Arduino",
    [ValidateSet('all','modulo_c4','modulo_c4_hibrido','teste_montagem_c4')]
    [string]$Sketch = 'all'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$firmwareRoot = Join-Path $repoRoot 'firmware'
$builder = Join-Path $ArduinoRoot 'arduino-builder.exe'
if (-not (Test-Path -LiteralPath $builder)) { throw 'Informe -ArduinoRoot com a pasta da IDE Arduino 1.8.x.' }
$sketches = if ($Sketch -eq 'all') { @('modulo_c4','modulo_c4_hibrido','teste_montagem_c4') } else { @($Sketch) }
New-Item -ItemType Directory -Force -Path (Join-Path $firmwareRoot 'bin') | Out-Null
foreach ($name in $sketches) {
    $buildPath = Join-Path $firmwareRoot ('.build/' + $name)
    New-Item -ItemType Directory -Force -Path $buildPath | Out-Null
    & $builder -compile -hardware (Join-Path $ArduinoRoot 'hardware') `
        -tools (Join-Path $ArduinoRoot 'tools-builder') `
        -tools (Join-Path $ArduinoRoot 'hardware/tools/avr') `
        -built-in-libraries (Join-Path $ArduinoRoot 'libraries') `
        -fqbn arduino:avr:uno -build-path $buildPath `
        (Join-Path $firmwareRoot ($name + '/' + $name + '.ino'))
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar $name." }
    Copy-Item -LiteralPath (Join-Path $buildPath ($name + '.ino.hex')) `
        -Destination (Join-Path $firmwareRoot ('bin/' + $name + '.hex')) -Force
}

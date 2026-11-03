param([string]$VcVars, [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$firmwareRoot = Join-Path $repoRoot 'firmware'
$outputPath = Join-Path $firmwareRoot '.build/host-tests'
if (-not $VcVars) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Informe -VcVars com o caminho de vcvars64.bat (MSVC C++17).' }
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'Compilador MSVC C++ nao encontrado.' }
    $VcVars = Join-Path $installation 'VC/Auxiliary/Build/vcvars64.bat'
}
foreach ($generated in @('modulo_c4_hibrido','teste_montagem_c4')) {
    $path = Join-Path $firmwareRoot ('.build/' + $generated + '/sketch/' + $generated + '.ino.cpp')
    if (-not (Test-Path -LiteralPath $path)) { throw 'Execute tools/build_can.ps1 antes dos testes.' }
}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$checks = @(
    @{Folder='mcp-mode';Source='test';Output='mcp-mode'},
    @{Folder='host';Source='test';Output='diagnostico'},
    @{Folder='host';Source='integration';Output='integracao'},
    @{Folder='host';Source='bench';Output='bancada'},
    @{Folder='host';Source='seed_vectors';Output='seed_vectors'}
)
foreach ($check in $checks) {
    $testPath = Join-Path $firmwareRoot ('tests/' + $check.Folder)
    $source = Join-Path $testPath ($check.Source + '.cpp')
    $exe = Join-Path $outputPath ($check.Output + '.exe')
    $obj = Join-Path $outputPath ($check.Output + '.obj')
    $command = 'call "' + $VcVars + '" >nul && cl /nologo /std:c++17 /EHsc /W3 /wd4244 /wd4267 /I"' +
        $testPath + '" /I"' + (Join-Path $firmwareRoot 'modulo_c4_hibrido') + '" /I"' +
        (Join-Path $firmwareRoot 'teste_montagem_c4') + '" "' + $source + '" /Fe:"' + $exe + '" /Fo:"' + $obj + '"'
    & $env:ComSpec /d /s /c $command
    if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar teste $($check.Output)." }
    if ($check.Output -ne 'seed_vectors') {
        & $exe
        if ($LASTEXITCODE -ne 0) { throw "Falha no teste $($check.Output)." }
    }
}
& $Python (Join-Path $firmwareRoot 'tests/host/check_seed_vectors.py') (Join-Path $outputPath 'seed_vectors.exe')
if ($LASTEXITCODE -ne 0) { throw 'Falha na comparacao independente do seed/key.' }

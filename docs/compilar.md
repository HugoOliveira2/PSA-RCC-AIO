# Compilar e verificar

Requisitos: Windows e compilador C# do .NET Framework 4.x. A compilação não precisa de NuGet ou internet. Na raiz do repositório:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\build.ps1
$teste = Start-Process -FilePath .\src\AIO-Assist-0.6.1.exe -ArgumentList @('--self-test', 'test-results-public.txt') -WindowStyle Hidden -Wait -PassThru
if ($teste.ExitCode -ne 0) { throw 'Falha nos testes; consulte test-results-public.txt.' }
Get-Content .\test-results-public.txt
```

Os testes usam transporte simulado, não abrem porta serial e não comprovam funcionamento na central física. O pacote histórico informa 1407 verificações; o resultado da compilação pública fica em `validation/test-results-public.txt`.

Para verificar dados e hashes, com Python 3:

```powershell
python tools/verify_publication.py
```

Para regenerar o mapa, instale PyYAML no seu ambiente e execute `python tools/convert_aio_map.py`. A compilação C# usa os JSON já incluídos e não depende desse passo.

## Firmware do módulo CAN

Incluí os sketches finais em `firmware/`, com o driver MCP2515 local e os `.hex` em `firmware/bin/`. Uso Uno/ATmega328P, cristal dos MCPs de 8 MHz e SPI de 1 MHz. Não é preciso instalar Thread ou escolher entre bibliotecas MCP2515 globais.

Na IDE Arduino, abra o `.ino` dentro da pasta do sketch e selecione Arduino Uno. Para reproduzir a compilação com IDE Arduino 1.8.x no Windows, execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build_can.ps1
```

Se a IDE estiver em outro local, informe `-ArduinoRoot 'PASTA_DA_IDE'`. Posso selecionar apenas um sketch com `-Sketch modulo_c4_hibrido`. Os objetos intermediários ficam em `firmware/.build/`, fora do Git; o script atualiza os `.hex` em `firmware/bin/`.

Depois da compilação, executo os testes de host com Visual Studio Build Tools C++17 e Python 3:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test_can.ps1
```

O script encontra MSVC por `vswhere`; posso informar `-VcVars 'CAMINHO_DE_VCVARS64.BAT'` e `-Python 'CAMINHO_DO_PYTHON'`. Os testes executam o driver real com registradores SPI simulados, o transporte de diagnóstico, mensagens B1 longas, botão/indicador, processamento junto do clima e 135.175 vetores seed/key comparados com uma implementação independente.

Registrei o resultado em [firmware/validation/resultados-2026-11-03.txt](../firmware/validation/resultados-2026-11-03.txt). A confirmação de campo da 3.2 e os limites dos ensaios estão no [registro da montagem](retrofits/modulo-can-clima.md). Os testes não abrem a serial nem se conectam ao veículo.

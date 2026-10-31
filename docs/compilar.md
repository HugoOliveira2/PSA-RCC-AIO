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

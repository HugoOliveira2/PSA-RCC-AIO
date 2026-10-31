# Pesquisa de VIN na AIO

Esta documentação distingue o procedimento manual estudado das funções do AIO Assist. A versão 0.6.1 consulta B0, mas não escreve VIN.

A referência comunitária é o [relato no Mittns Peugeot Forum](https://www.mittns.de/thread/2253-how-to-change-vin-in-continentals-aio-infotainment/?pageNo=2), posts 25, 35, 37 e 40; o post 44 está na página 3. O tutorial local registra confirmação de cessação dos bipes por danielvieira em 28/03/2024, numa AIO Continental de Citroën instalada em um Peugeot 308. Trata-se de relato de uma unidade, não de validação universal.

O roteiro estudado usa CAN 764/664, KWP sessão C0, acesso de segurança pelo sketch Arduino e escrita da zona B0 com 17 bytes ASCII do VIN correto do veículo. A [documentação do sketch](https://github.com/ludwig-v/arduino-psa-diag) descreve os comandos. Não confunda VIN em ASCII hexadecimal com a conversão de um número inteiro.

A sequência referida na pesquisa é selecionar `>764:664`, obter resposta coerente de sessão e identificação, usar o atalho `:D91C:83:C0`, confirmar o desbloqueio e enviar `3BB0` seguido dos 34 dígitos hexadecimais. Essa sequência deve ser conferida contra a referência e a unidade real antes de uso. Nenhum comando é enviado por este documento ou pelas ferramentas de conversão do repositório.

Uma leitura `21B0` deve ser preservada antes e depois quando suportada. A resposta histórica local `61B0` continha 17 caracteres ASCII. No relato do fórum, a releitura e o retorno positivo específico `7BB0` não foram demonstrados como etapas documentadas de sucesso; são critérios de verificação propostos. `50C0` comprova sessão, não desbloqueio. `6784` é o aceite do acesso de segurança esperado no procedimento.

O objetivo é associar a central ao VIN correto do veículo. O procedimento não altera a identificação física do carro. Corrigir o VIN não comprova compatibilidade de câmera, idioma, relógio ou rede CAN. Não executar escrita genérica em A0: os valores de rastreabilidade precisam ser comprovados para a variante.

Não publique o VIN anterior ou novo em logs, capturas ou comandos preparados. Confira persistência e efeito após o ciclo normal de desligamento. Se a resposta for incerta, preserve o log e determine o estado por leitura antes de qualquer nova escrita.

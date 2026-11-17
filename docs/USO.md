# Guia de uso

[Voltar ao README](../README.md)

## Abrir e configurar

Requisitos: Windows, SDK .NET 9 (9.0.301 ou posterior da série 9), FFmpeg com `libx264` e `ffprobe.exe` na mesma pasta. O FFmpeg não faz parte do repositório.

Abra **Iniciar.cmd** com duplo clique na raiz do projeto. Ele compila em Release e abre o painel. Também é possível abrir diretamente `bin/Release/net9.0-windows/JiraVideoCompressor.exe` depois de compilar, sem console adicional. Pelo terminal:

```powershell
dotnet build -c Release
dotnet run -c Release --no-build
```

Na janela, escolha a pasta de entrada, a pasta de resultados e a pasta do FFmpeg/FFprobe. Clique em **Salvar e iniciar**. As escolhas ficam em `appsettings.json`, dentro do projeto, e são restauradas ao reabrir. O formato JSON permite manter as configurações de execução e as escolhas da janela no mesmo arquivo.

O exemplo público usa `tools/ffmpeg/bin`. Você também pode selecionar uma instalação existente em outra pasta. Mantenha as ferramentas fora de `bin` e `obj`, que são diretórios de compilação e podem ser removidos por uma limpeza. O FFmpeg não precisa estar no PATH. Se a pasta padrão tools/ffmpeg/bin ainda não tiver as ferramentas, o aplicativo as prepara automaticamente pela internet ao iniciar, validando o SHA-256 do fornecedor. Uma instalação existente selecionada na janela continua sendo usada sem mudanças.

Se `appsettings.json` ainda não existir, a janela usa `appsettings.example.json` como ponto de partida e salva a configuração ao iniciar. Também é possível copiar o exemplo manualmente na primeira configuração. Não substitua uma configuração pessoal existente pelo exemplo.

Sem `--config`, a aplicação procura a raiz do projeto a partir do executável; em uma distribuição sem o projeto, usa a pasta do executável. Caminhos relativos são resolvidos a partir da pasta da configuração. Para usar um arquivo explícito:

```powershell
dotnet run -- --config "C:\caminho\JiraVideoCompressor\appsettings.json"
```

## Uso pelo time

1. Abra o painel, confira as pastas e clique em **Salvar e iniciar**.
2. Copie os vídeos para a entrada escolhida (padrão: `Entrada`). Arquivos já presentes também são descobertos. Subpastas não são monitoradas.
3. Acompanhe fila, etapa, percentual, resultados e erros na grade. **Abrir resultados** abre a saída no Explorer.
4. Fechar, minimizar ou usar **Ocultar na bandeja** mantém o monitor ativo. Dê dois cliques no ícone próximo ao relógio (possivelmente em ícones ocultos) ou use **Abrir painel** no menu do ícone para voltar.
5. Use **Parar** para interromper e liberar a edição das pastas. Para encerrar o aplicativo, use **Sair** no menu da bandeja. Parar/Sair cancela o FFmpeg e preserva os originais.

O monitoramento inicia pelo botão, não automaticamente ao abrir. Não há serviço nem início automático com o Windows. Uma segunda janela usando a mesma configuração é impedida.

O ícone de rolo compactador identifica o executável, a janela, a barra de tarefas e a bandeja. Ao terminar a cópia ou compressão de cada vídeo e liberar o resultado validado, a aplicação solicita uma notificação nativa do Windows com o nome do original e do resultado. Clicar no aviso reabre o painel. Avisos de conclusões próximas são enfileirados com intervalo de 7 segundos. Duplicatas reconhecidas, falhas e cancelamentos não geram aviso de sucesso.

As notificações dependem das configurações do Windows: o modo Não perturbe ou notificações desativadas podem impedir sua exibição. A grade e o histórico continuam registrando as conclusões. Os avisos são enviados pelo modo com janela/bandeja, inclusive quando a janela está oculta.

O monitor verifica tamanho e modificação a cada 2 segundos e exige 10 segundos sem mudanças. Antes de processar, abre o original impedindo escrita/exclusão e reconfirma os dados. Um arquivo bloqueado aguarda sem impedir que outros avancem. Hash, análise, cópia e compressão são sequenciais; novas chegadas continuam sendo detectadas durante a compressão.

Se um produtor fecha o arquivo entre gravações, uma pausa pode parecer o fim. Nesse caso, grave fora da entrada e mova o arquivo concluído para ela no mesmo volume, ou configure `StableSeconds` acima da maior pausa prevista.

## Resultados e segurança dos arquivos

Arquivos de até **10.000.000 bytes, inclusive**, são copiados integralmente. Os maiores são comprimidos em MP4/H.264, sem áudio (`-an`), em duas passagens, mirando **9.500.000 bytes** com margem para o contêiner. Podem ficar menores conforme o conteúdo. Há até três tentativas com bitrate menor. Vídeos longos demais para o mínimo de 50 kb/s são rejeitados e precisam ser divididos.

Antes de liberar, o programa valida tamanho positivo, limite máximo e presença/duração do vídeo com FFprobe. Só então move o resultado completo para a raiz da saída. `.work` contém arquivos internos; nunca os anexe. Após encerramento forçado podem sobrar subpastas ali, removíveis com o programa parado.

Os novos resultados usam o nome original com `_compressed`: `evidencia.mov` comprimido vira `evidencia_compressed.mp4`. Arquivos copiados mantêm a extensão e também recebem o sufixo. Se outro conteúdo ocupar o nome, usa `_compressed_2`, `_compressed_3` etc., sem sobrescrever.

O SHA-256 continua sendo usado internamente para evitar duplicatas, mesmo renomeadas e após reiniciar. `.state` guarda a associação e o hash do resultado por conteúdo e pasta de destino. Preserve essa pasta junto com a saída; não execute instâncias com estados diferentes apontando para a mesma saída. Trocar a saída permite preparar os vídeos na nova pasta sem perder o registro da anterior. Resultados antigos com nomes de hash são conferidos e renomeados para o nome original com `_compressed`, sem recodificar ou criar outra cópia. Se o resultado antigo foi apagado, ele é reconstruído com o nome amigável. A migração atualiza o registro, respeita nomes ocupados e pode ser retomada após interrupção.

Resultados existentes são conferidos pelo hash. Um resultado apagado é reconstruído. Se for alterado ou o registro estiver corrompido, aparece um erro e o arquivo não é sobrescrito. Revise e restaure o arquivo/registro ou remova ambos para reprocessar. Não apague o estado como rotina.

Uma falha não impede os outros vídeos. O erro permanece na grade e nos registros. Para tentar novamente, corrija o problema e pare/inicie o monitor; alterações no arquivo também permitem nova tentativa. Originais nunca são movidos ou excluídos. A ferramenta pressupõe entradas sem áudio: a cópia mantém exatamente o original, enquanto a compressão remove áudio.

## Configuração

| Campo | Finalidade / padrão |
| --- | --- |
| `ToolsDirectory` | Pasta contendo `ffmpeg.exe` e `ffprobe.exe` |
| `InputDirectory` | Entrada monitorada (`Entrada`) |
| `OutputDirectory` | Resultados validados (`Prontos para Jira`) |
| `StateDirectory` | Registros persistentes e bloqueio (`.state`) |
| `TargetBytes` | Alvo decimal: `9500000` |
| `LimitBytes` | Máximo permitido: `10000000`; pode ser reduzido |
| `PollSeconds` | Intervalo de varredura: `2` |
| `StableSeconds` | Janela sem mudanças: `10` |
| `Extensions` | Extensões aceitas, sem diferenciar maiúsculas |

Entrada, saída e estado devem ser pastas diferentes. Use pastas locais dedicadas e não as renomeie durante a execução. Para editar configurações, pare o monitor; alterações valem no próximo início.

Modos sem janela, disponíveis pelo terminal:

```powershell
dotnet run -- --console
dotnet run -- --once
dotnet run -- --manual "C:\Videos\evidencia.mp4"
dotnet run -- --manual
```

`--console` monitora continuamente e permite Ctrl+C; `--once` aguarda os arquivos atuais ficarem estáveis/disponíveis e termina após tratá-los. Erros retornam código 1; sucesso, 0. O manual usa a mesma configuração de saída, validação e controle de duplicatas.


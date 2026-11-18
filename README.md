<p align="center">
  <img src="Assets/roller.svg" alt="Rolo compactador amarelo" width="112" />
</p>

# Jira Video Compressor

**Da pasta de gravações para um vídeo pronto para anexar no Jira.**

Aplicativo para Windows que monitora uma pasta, aguarda a gravação terminar e prepara um vídeo por vez. Tem seleção de pastas, progresso na janela, acesso pela bandeja e notificações ao concluir — mantendo os arquivos originais intactos.

**Windows · C# / Windows Forms · .NET 9 · FFmpeg**

[ZIP portátil](#usar-o-zip-portátil) · [Primeiro uso](#primeiro-uso) · [Guia completo](docs/USO.md) · [Status para widgets](docs/STATUS.md) · [Desenvolvimento](docs/DESENVOLVIMENTO.md)

## O que ele faz

| Recurso | Comportamento |
| --- | --- |
| Monitoramento | Descobre arquivos novos e os que já estavam na entrada |
| Espera pela gravação | Confere estabilidade e disponibilidade antes de processar |
| Fila sequencial | Processa um vídeo por vez, com etapa e percentual |
| Arquivos pequenos | Copia sem recodificar quando têm até **10.000.000 bytes** |
| Arquivos maiores | Comprime em MP4/H.264, sem áudio, mirando **9.500.000 bytes**, em modo rápido ou preciso |
| Movavi | Ignora o MKV provisório no monitor e aguarda o vídeo exportado |
| Liberação validada | Confere o limite antes de colocar o resultado na saída |
| Nomes legíveis | Usa o nome original com `_compressed`, sem sobrescrever outro arquivo |
| Reinício seguro | Usa hash e registros persistentes para reconhecer duplicatas |
| Bandeja do Windows | Mantém o monitor funcionando com a janela oculta |
| Notificações | Avisa qual vídeo foi copiado ou comprimido |
| Integração futura | Salva fila, progresso e histórico em JSON / JSON Lines |

> O perfil foi pensado para evidências **sem áudio**. O limite de 10.000.000 bytes é a regra deste projeto; confira o limite configurado no Jira da sua organização.

## Usar o ZIP portátil

Baixe `JiraVideoCompressor-windows-x64.zip` em [Releases](https://github.com/OVinicius1995/Compressor/releases). Ele inclui aplicativo, runtime .NET e Windows Forms. O FFmpeg/FFprobe são preparados automaticamente a partir do fornecedor na primeira execução, com conferência de SHA-256. **Não exige instalar nada manualmente.**

1. Extraia todo o ZIP para uma pasta com permissão de escrita no Windows x64.
2. Abra `Iniciar.cmd` ou `JiraVideoCompressor.exe` dentro da pasta extraída.
3. Aguarde a preparação automática dos recursos e escolha as pastas de entrada e saída.
4. Clique em **Salvar e iniciar**. Depois da preparação inicial, o processamento é local.

Mantenha os arquivos do pacote juntos; o executável usa os componentes ao seu lado. O pacote contém uma configuração nova, sem vídeos, histórico ou preferências pessoais de quem o gerou. A preparação inicial do FFmpeg exige internet; depois, o processamento é local.

O ZIP é disponibilizado como arquivo de uma Release do GitHub após os testes do workflow. O download automático **Source code (zip)** do GitHub contém o código-fonte e não equivale ao pacote portátil. Veja como gerar e validar o pacote em [Distribuição](docs/DISTRIBUICAO.md).

## Primeiro uso

As instruções abaixo são para compilar a partir do código-fonte. Quem usa o ZIP portátil pode seguir diretamente a seção anterior.

### 1. Prepare as ferramentas

- **Windows**, com acesso de escrita à pasta do projeto e às pastas escolhidas.
- **SDK .NET 9**, versão 9.0.301 ou posterior da série 9. O SDK é necessário para compilar e para o inicializador. [Download oficial do .NET 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0).
- **Internet na primeira abertura**, para a preparação automática dos recursos de vídeo.

Baixe ou clone o código. Os recursos são instalados automaticamente em `tools/ffmpeg/bin`, dentro do projeto, e ficam fora do Git. A janela solicita apenas as pastas dos vídeos.

### 2. Abra o aplicativo

Dê dois cliques em **`Iniciar.cmd`**. Ele compila a aplicação em Release e abre o painel.

Ou execute no PowerShell, na pasta do projeto:

```powershell
dotnet build -c Release
dotnet run -c Release --no-build
```

Encerre uma instância anterior usando **Sair**, no menu da bandeja, antes de recompilar ou atualizar.

### 3. Escolha as pastas

No painel, defina:

1. **Pasta de entrada** — onde você coloca as gravações.
2. **Prontos para Jira** — onde quer receber os resultados validados.

Clique em **Salvar e iniciar**. As escolhas ficam no `appsettings.json` do projeto e são restauradas ao abrir novamente. Se o arquivo ainda não existir, a janela usa o exemplo como ponto de partida e o cria ao salvar.

### 4. Adicione os vídeos

Coloque as gravações na entrada e acompanhe a fila. Por padrão, o monitor consulta a pasta a cada **2 segundos** e exige **10 segundos sem alterações** antes de processar. Apenas a pasta escolhida é monitorada, sem subpastas.

Quando aparecer **Pronto**, o vídeo já pode ser encontrado em **Abrir resultados** e anexado ao Jira.

```text
Entrada                         Prontos para Jira
gravacao-login.mp4         →     gravacao-login_compressed.mp4
evidencia.mov (comprimida)  →     evidencia_compressed.mp4
```

Se outro conteúdo ocupar o nome, o resultado recebe `_compressed_2`, `_compressed_3` etc. Mesmo conteúdo com outro nome é reconhecido como duplicata e reutiliza o resultado existente.

## Movavi e velocidade

O painel vem com **Movavi: ignorar MKV provisório no monitor** ativado. Nesse modo, `.mkv` não entra na fila, mesmo quando já ficou estável: o programa aguarda o arquivo exportado em outro formato, como MP4. Isso evita processar tanto o pré-vídeo quanto o resultado final, mesmo se tiverem nomes diferentes. Os arquivos MKV são preservados.

Se você exporta o resultado final em MKV, desmarque a opção ou use o modo manual, que continua aceitando esse formato.

- **Rápido (padrão):** uma passagem, preset `veryfast`, com validação do limite ao terminar.
- **Preciso:** duas passagens, preset `medium`, para priorizar o controle de tamanho e a eficiência de compressão.

As **três tentativas são um teto de recuperação**, não três compressões obrigatórias. Um resultado válido encerra o trabalho na primeira tentativa. Se o modo rápido ultrapassar 10.000.000 bytes, o bitrate é recalculado a partir do tamanho obtido e a recuperação usa duas passagens; nada acima do limite é liberado. `MaxCompressionAttempts` permite reduzir o teto, aceitando que o vídeo seja rejeitado se não couber.

O modo rápido pode reduzir a qualidade em relação ao preciso no mesmo orçamento de bytes. O ganho depende do vídeo e do computador. As escolhas do painel ficam salvas no `appsettings.json`; configurações antigas sem os novos campos assumem modo Movavi e perfil rápido.

## Janela, bandeja e alertas

- **Fechar ou minimizar a janela** mantém o aplicativo na bandeja, próximo ao relógio, possivelmente em ícones ocultos.
- **Dois cliques no rolo compactador** reabrem o painel. O menu do ícone também permite abrir, iniciar, parar e sair.
- **Parar** interrompe o monitor e libera a edição das pastas. **Sair** encerra o aplicativo.
- Ao concluir um vídeo, uma notificação informa a operação, o nome original e o resultado. Clicar nela reabre o painel. A exibição depende das configurações de notificação do Windows.

O monitor começa pelo botão **Salvar e iniciar**. O aplicativo não instala serviço nem inicia automaticamente com o Windows.

## Preservação dos arquivos

Os originais não são movidos nem excluídos. Somente resultados com tamanho positivo, dentro do limite e com faixa/duração de vídeo reconhecidas são liberados na raiz da saída.

Preserve a pasta **`.state`**: ela guarda os registros de duplicatas. Resultados antigos nomeados por hash são conferidos e migrados para o nome amigável. Não anexe os arquivos internos de `.work`.

O alvo de 9,5 MB não é um tamanho exato: o resultado pode ficar menor. Vídeos muito longos podem precisar ser divididos. Se o gravador fecha e reabre o arquivo durante a captura, prefira gravar fora da entrada e mover o arquivo concluído para ela. Mais detalhes e recuperação de falhas no [guia de uso](docs/USO.md).

## Outros modos de execução

Para os modos sem janela, crie primeiro o `appsettings.json` pela interface ou copie o exemplo, ajuste as pastas de entrada e saída.

```powershell
# Monitorar pelo terminal
dotnet run -- --console

# Processar a entrada atual e encerrar
dotnet run -- --once

# Processar um único vídeo
dotnet run -- --manual "C:\Videos\evidencia.mp4"

# Usar uma configuração específica
dotnet run -- --config "C:\Configuracoes\appsettings.json" --once
```

## Documentação

| Documento | Conteúdo |
| --- | --- |
| [Guia de uso](docs/USO.md) | Operação, configuração, limites e recuperação de falhas |
| [Status e widgets](docs/STATUS.md) | Contrato do `status.json` e histórico em `history.jsonl` |
| [Desenvolvimento e testes](docs/DESENVOLVIMENTO.md) | Compilação, testes de integração, interface e ícone |
| [Interface e experiência de uso](docs/INTERFACE.md) | Tema compartilhado, acessibilidade e validação visual |
| [Como contribuir](CONTRIBUTING.md) | Preparação do ambiente e descrição de alterações |
| [Preparação para publicação](docs/PUBLICACAO.md) | Arquivos que entram no Git e revisão da primeira publicação |
| [Distribuição portátil](docs/DISTRIBUICAO.md) | ZIP completo, geração, validação e uso sem SDK |
| [Histórico de alterações](CHANGELOG.md) | Funcionalidades preparadas para a primeira versão |

## Sobre esta versão

O código usa .NET 9; a situação dessa dependência e as instruções de desenvolvimento estão documentadas no [guia de desenvolvimento](docs/DESENVOLVIMENTO.md#base-de-execução) e na [revisão de publicação](docs/PUBLICACAO.md).

O aplicativo usa [Apache License 2.0](LICENSE), seguindo o mesmo modelo de licença do projeto [singularidade-data-bridge](https://github.com/singularidade-digital-dev/singularidade-data-bridge). Os avisos estão em [NOTICE](NOTICE). O FFmpeg é gratuito e permanece sob a licença de sua própria distribuição; seus executáveis não são incluídos no repositório nem no ZIP público.

Se a preparação falhar, um alerta explica como corrigir. O monitoramento permanece bloqueado; use **Tentar preparar novamente** após corrigir a conexão ou a pasta de instalação. Veja [Uso](docs/USO.md).

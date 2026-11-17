# Preparação para publicação no GitHub

[Voltar ao README](../README.md)

Este guia prepara a revisão. Nenhum envio ao GitHub é realizado por ele ou pelo aplicativo.

## Destino definido pelo responsável

- Repositório: [OVinicius1995/Compressor](https://github.com/OVinicius1995/Compressor).
- URL de `origin`: `https://github.com/OVinicius1995/Compressor.git`.
- Branch pretendida: `main`.
- Publicação no repositório autorizada pelo responsável nesta conversa.

O README completo já está preparado. Não é necessário executar `echo "# Compressor" >> README.md`, pois esse comando apenas acrescentaria outro título ao documento existente.

## Licenças sem complicação

A licença do seu código informa o que outras pessoas podem fazer com ele. O GitHub não exige escolher uma licença para hospedar o código. Sem conceder uma licença, continuam valendo os direitos autorais padrão, além das permissões de visualização e fork previstas pelo GitHub para repositórios públicos. [Explicação do GitHub](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository).

O FFmpeg é um componente de terceiros. Distribuir um ZIP que contém seus executáveis exige respeitar as condições da build incluída, independentemente da decisão sobre a licença do aplicativo. Por isso a publicação do código e a distribuição do pacote completo têm revisões separadas. Após a indicação do projeto de referência, o aplicativo adotou o mesmo modelo Apache License 2.0, preservando as licenças dos componentes de terceiros.

## Conteúdo do repositório

| Incluir | Manter local |
| --- | --- |
| Código `.cs` e projetos `.csproj` | `bin/`, `obj/`, executáveis e DLLs |
| `Assets/`: SVG, ICO, prévia e gerador | FFmpeg/FFprobe e pacotes de download |
| README, guias, contribuição e changelog | Vídeos, originais e resultados |
| `appsettings.example.json` | `appsettings.json` e configurações pessoais |
| `global.json`, `NuGet.Config`, `Iniciar.cmd` | `.state/`, `.work/` e arquivos de bloqueio |
| `.gitignore`, `.gitattributes`, testes | `status.json`, `history.jsonl`, logs e artefatos de testes |

O exemplo usa `tools/ffmpeg/bin`. A configuração pessoal existente não precisa mudar. O `.gitignore` também cobre ferramentas nesse caminho e artefatos dos projetos de teste.

## Revisão antes do primeiro commit

- [ ] Revisar o README e executar o primeiro uso com uma configuração nova.
- [ ] Compilar em Release e executar os testes indicados no guia de desenvolvimento.
- [ ] Confirmar que nomes de saída, migração legada, bandeja e notificações funcionam.
- [ ] Revisar a base .NET 9, atualmente fora de suporte segundo a [página oficial](https://dotnet.microsoft.com/en-us/download/dotnet/9.0), e planejar a atualização antes de uma versão pública estável.
- [x] Adotar Apache License 2.0, seguindo o modelo do projeto de referência, com LICENSE e NOTICE.
- [x] Definir o destino: `OVinicius1995/Compressor`, branch `main`.
- [ ] Revisar descrição e visibilidade do repositório antes de publicar.
- [ ] Conferir que arquivos selecionados não contêm nomes de usuários, caminhos locais, gravações ou configurações pessoais.
- [ ] Revisar a lista de arquivos e o diff antes de confirmar o commit.

Sugestão de descrição para o repositório:

> Aplicativo Windows para preparar vídeos para o Jira: monitoramento de pasta, compressão com FFmpeg, bandeja, notificações e progresso persistido.

## Preparar o Git local após a revisão

Se esta pasta ainda não for um repositório, inicialize-o na raiz:

```powershell
git init -b main
```

Selecione explicitamente os arquivos públicos:

```powershell
git add .gitignore .gitattributes README.md CONTRIBUTING.md CHANGELOG.md LICENSE NOTICE
git add JiraVideoCompressor.csproj Program.cs MainForm.cs MonitorService.cs StatusStore.cs VideoProcessor.cs ToolBootstrapper.cs
git add appsettings.example.json global.json NuGet.Config Iniciar.cmd
git add Assets docs tests scripts
git status --short
git diff --cached --stat
git diff --cached
```

Os comandos acima só devem ser executados quando você decidir iniciar o versionamento. Eles não criam um repositório remoto. Se já houver um histórico Git, revise também `git ls-files`: o `.gitignore` não retira arquivos que já estavam rastreados.

Confira algumas exclusões com:

```powershell
git check-ignore appsettings.json status.json history.jsonl
git ls-files
```

Depois de revisar o conteúdo selecionado, o commit local pode ser criado com:

```powershell
git commit -m "Prepara primeira versão do Jira Video Compressor"
```

O destino já foi informado. Depois da revisão, se ainda não houver `origin`, a configuração e o envio usarão:

```powershell
git remote add origin https://github.com/OVinicius1995/Compressor.git
git push -u origin main
```

Se `origin` já existir, confira `git remote -v` antes de alterar qualquer destino. Este preparo documenta os comandos, sem executá-los. Um remoto com histórico deve ser integrado antes de enviar; não use `--force` como atalho.

## Distribuição de executáveis

O código-fonte exige SDK e FFmpeg separado para o desenvolvimento. Para o usuário final, o script [Build-Portable.ps1](../scripts/Build-Portable.ps1) prepara um ZIP autocontido de Windows x64, incluindo runtime e preparação automática das ferramentas, além de avisos de licença, manifesto de hashes e checksum do arquivo final. Veja o [guia de distribuição](DISTRIBUICAO.md).

O ZIP e seu `.sha256` devem ser anexados como arquivos de uma Release, mantendo os binários fora do histórico Git. A [documentação do GitHub sobre Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases) descreve essa distribuição de binários.

O pacote público usa Apache License 2.0 para o aplicativo e prepara FFmpeg diretamente do fornecedor na primeira execução, com validação do SHA-256. Seus executáveis não são anexados ao ZIP público. A variante offline opcional continua exigindo a revisão de redistribuição GPL descrita em [Distribuição](DISTRIBUICAO.md).

Ainda não há instalador, atualização automática ou Release publicada. Gerar os arquivos localmente não envia nada ao GitHub.

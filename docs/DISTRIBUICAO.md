# Distribuição portátil para Windows x64

[Voltar ao README](../README.md)

## Baixar e usar

Baixe `JiraVideoCompressor-windows-x64.zip` em [Releases](https://github.com/OVinicius1995/Compressor/releases), extraia todo o conteúdo e abra `Iniciar.cmd`. Aguarde a preparação dos recursos, escolha entrada e saída e clique em **Salvar e iniciar**.

O runtime .NET e Windows Forms já acompanham o pacote. Na primeira vez, o aplicativo prepara FFmpeg e FFprobe automaticamente pela internet. Ele consulta a distribuição do fornecedor, baixa por HTTPS, confere o tamanho e o SHA-256 e instala as ferramentas em `tools/ffmpeg`. A licença do fornecedor fica em `tools/ffmpeg/LICENSE.txt`; a origem e o hash, em `ORIGIN.json`.

Não é necessário instalar SDK, executar comandos ou configurar o PATH. A primeira preparação exige internet e pode baixar cerca de 200 MB; as próximas execuções reutilizam as ferramentas. O processamento dos vídeos é local. Não há seleção de executáveis no painel. Configurações antigas são migradas automaticamente para a pasta gerenciada. Se houver falha, o alerta explica a correção e o monitoramento fica bloqueado até usar **Tentar preparar novamente** com sucesso.

Mantenha os arquivos do ZIP juntos e use uma pasta com permissão de escrita. As preferências ficam no `appsettings.json` ao lado do executável. Para atualizar, encerre pela bandeja e preserve sua configuração e `.state`. O pacote é destinado a Windows x64.

## Como a Release é produzida

O workflow [Build and release Windows](../.github/workflows/release.yml) usa um runner Windows, compila, testa a preparação automática, gera o pacote e testa o executável portátil. Só depois cria a Release e anexa o ZIP e o SHA-256.

- O primeiro envio a `main` prepara `v0.1.0`.
- Se essa versão já existir, ela é preservada.
- Para outra versão, envie uma tag `vX.Y.Z` ou execute o workflow manualmente com a versão desejada.

O código e os avisos do aplicativo usam [Apache License 2.0](../LICENSE), com atribuições em [NOTICE](../NOTICE). O modelo de Releases foi inspirado no [projeto de referência](https://github.com/singularidade-digital-dev/singularidade-data-bridge/blob/main/RELEASING.md); não há código de acesso a bancos incorporado.

## Gerar o ZIP localmente

Na máquina de desenvolvimento, com SDK .NET 9:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-Portable.ps1
```

O script publica com `--self-contained true`, para `win-x64`, usando os pacotes oficiais do runtime 9.0.23. O primeiro publish precisa acessar o NuGet. A situação da base .NET 9 está descrita em [Desenvolvimento](DESENVOLVIMENTO.md#base-de-execução).

Os arquivos ficam em uma pasta nova, sem apagar artefatos anteriores:

```text
artifacts/portable-DATA-ID/
├── JiraVideoCompressor-windows-x64/
│   ├── JiraVideoCompressor.exe
│   ├── Iniciar.cmd
│   ├── LEIA-ME.txt
│   ├── appsettings.json
│   ├── package-manifest.json
│   ├── LICENSE
│   ├── NOTICE
│   ├── Licenses/
│   └── ... componentes do aplicativo e do runtime
├── JiraVideoCompressor-windows-x64.zip
└── JiraVideoCompressor-windows-x64.zip.sha256
```

O manifesto identifica plataforma, runtime, licença e hashes. A modalidade pública é `download-on-first-run`: o ZIP não redistribui os executáveis do FFmpeg. Nenhuma configuração pessoal, estado de execução ou vídeo é copiado. Símbolos de depuração são removidos.

## Testar

```powershell
dotnet run --project tests/Bootstrap/Bootstrap.csproj -- .
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Portable.ps1 -PackagePath "artifacts\portable-DATA-ID\JiraVideoCompressor-windows-x64.zip"
```

O primeiro teste usa respostas de rede simuladas e verifica hash, tamanho, endereço, licença, origem, cancelamento e reutilização. O segundo extrai o ZIP, confere os hashes, prepara FFmpeg pelo fornecedor e executa o aplicativo diretamente, com a busca por runtime externo desativada. Confere cópia, compressão real, limite, originais e reinício sem duplicatas.

Em ambiente de testes sem acesso de rede, `COMPRESSOR_TEST_FFMPEG` pode apontar para uma instalação de teste. Ela é copiada apenas para a extração temporária do teste, sem mudar o ZIP. Esse modo é usado junto com o teste separado de preparação automática.

Não distribua a extração usada nos testes: ela já contém configurações e dados de teste. Distribua o ZIP original.

## Ambientes com publish separado

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-Portable.ps1 -PublishedAppDirectory "C:\Build\publish" -RuntimePackagesDirectory "C:\Cache\NuGet"
```

O cache NuGet precisa conter os avisos dos pacotes da versão efetivamente publicada. Sem o parâmetro, o script consulta o cache padrão. Uma compilação comum em `bin/Release` não é um publish autocontido e será rejeitada.

## Pacote offline

`-IncludeFFmpeg -FFmpegDirectory "C:\Ferramentas\ffmpeg\bin"` permite preparar um pacote offline para revisão. Essa variante incorpora os executáveis e sua licença; antes de redistribuí-la, é necessário atender às condições da build GPL, incluindo fontes correspondentes e instruções de compilação. Ela não é a modalidade usada pelo workflow público. [Referência do FFmpeg](https://ffmpeg.org/legal.html).

O aplicativo Apache 2.0 e os componentes de terceiros mantêm suas licenças separadas. O FFmpeg não é uma ferramenta paga nem exige comprar uma licença para este download; isso não elimina suas condições de distribuição.

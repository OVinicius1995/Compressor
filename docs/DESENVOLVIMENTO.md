# Desenvolvimento e testes

[Voltar ao README](../README.md)

Execute os comandos abaixo na raiz do projeto, com o aplicativo encerrado pelo menu **Sair** da bandeja.

## Base de execução

O projeto usa `net9.0-windows` com Windows Forms. `global.json` seleciona o SDK 9.0.301 com avanço permitido dentro da série 9. Instalar apenas um SDK de outra versão principal não substitui esse requisito.

A [página oficial do .NET 9](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) informa que essa versão está fora de suporte. Esta preparação documenta a base existente; a migração de framework ainda precisa ser planejada e validada antes de uma versão pública estável.

Para configurar um ambiente novo sem usar a janela:

```powershell
Copy-Item appsettings.example.json appsettings.json
# Edite ToolsDirectory e as pastas no arquivo criado antes de executar os testes.
```

Não substitua um `appsettings.json` existente se quiser manter suas preferências. O exemplo usa `tools/ffmpeg/bin`; um caminho absoluto para outra instalação também é aceito. FFmpeg e FFprobe precisam estar disponíveis antes da integração, mas não são necessários para compilar o C#. A pasta padrão pode ser preparada automaticamente pelo aplicativo, incluindo no modo `--prepare-tools`.

## Organização do código

| Arquivo | Responsabilidade |
| --- | --- |
| [Program.cs](../Program.cs) | Entrada, seleção dos modos e localização da configuração |
| [MainForm.cs](../MainForm.cs) | Janela, seleção de pastas, bandeja e notificações |
| [MonitorService.cs](../MonitorService.cs) | Descoberta, estabilidade, fila e ciclo de execução |
| [VideoProcessor.cs](../VideoProcessor.cs) | Configuração, hash, registros, migração de nomes, FFmpeg e validação |
| [ToolBootstrapper.cs](../ToolBootstrapper.cs) | Preparação automática do FFmpeg, validação de tamanho/hash e origem |
| [StatusStore.cs](../StatusStore.cs) | Snapshot, contadores, histórico e fila de conclusões |
| [tests/Integration.ps1](../tests/Integration.ps1) | Cenários com vídeos sintéticos e FFmpeg real |
| [tests/UiSmoke](../tests/UiSmoke/UiSmoke.csproj) | Verificação da interface em uma sessão Windows interativa |

## Compilação e testes

```powershell
dotnet build -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Integration.ps1 -Configuration Release
```

A integração usa FFmpeg real e vídeos sintéticos em `.test-artifacts`, sem usar evidências do time. Cobre cópia exata, limite exato e um byte acima, compressão/decodificação, ausência de áudio, originais, reinício/duplicatas, cópia pausada, fila, instância única, entrada inválida, recuperação, proteção contra sobrescrita, nomes e colisões. Também verifica percentual real, processamento sequencial e o contrato de status/histórico. Usa cerca de 100 MB por execução e mantém os artefatos para inspeção, fora do Git.

Os testes de nomes incluem registros legados com resultado existente ou apagado, migração de um vídeo já comprimido sem alterar seu conteúdo, reconstrução sem reutilizar o hash como nome, colisões e retomada da renomeação após interrupção.

Para testar a janela em uma sessão Windows interativa, após a integração, use a configuração de teste informada ao final:

```powershell
dotnet build tests/UiSmoke/UiSmoke.csproj -c Release
dotnet tests/UiSmoke/bin/Release/net9.0-windows/UiSmoke.dll bin/Release/net9.0-windows/JiraVideoCompressor.dll ".test-artifacts/ID-INFORMADO/appsettings.json"
```

O teste da janela verifica início/parada, cancelamento durante compressão sem liberar resultado parcial, persistência das pastas, bandeja, ocultar/reabrir, grade e restauração das escolhas em nova instância. Salva `ui-preview.png` junto à configuração de teste. Altera apenas essa configuração.

Também confere o ícone incorporado ao executável, o uso do mesmo ícone na janela/bandeja e a emissão dos avisos corretos para cópia e compressão reais, sem aviso de sucesso para duplicatas ou cancelamentos. A decisão final de exibir o aviso pertence ao Windows.

O ícone tem versões de 16 a 256 pixels em `Assets/roller.ico`. O desenho original está em `Assets/roller.svg`; para regenerar o ICO e a prévia usando apenas System.Drawing, execute `powershell -NoProfile -ExecutionPolicy Bypass -File Assets/Generate-Icon.ps1`.

Sem pacotes externos: `NuGet.Config` mantém a restauração sem fontes externas e `global.json` seleciona o SDK 9. É necessário o pacote de referência Windows Desktop do SDK.


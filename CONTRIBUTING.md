# Como contribuir

Obrigado pelo interesse em melhorar o Jira Video Compressor. O aplicativo é distribuído sob Apache License 2.0.

## Preparar o ambiente

1. Instale os requisitos descritos no [README](README.md#primeiro-uso).
2. Abra `Iniciar.cmd`, selecione as pastas e salve a configuração, ou copie `appsettings.example.json` para `appsettings.json` e ajuste os caminhos.
3. Use uma entrada de testes, separada dos seus vídeos de trabalho.
4. Encerre o aplicativo pelo menu **Sair** da bandeja antes de recompilar.

## Validar uma alteração

```powershell
dotnet build -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests\Integration.ps1 -Configuration Release
```

Os testes geram seus próprios vídeos. Para mudanças na janela ou na bandeja, execute também o teste de interface conforme o [guia de desenvolvimento](docs/DESENVOLVIMENTO.md).

Mantenha estas regras do processamento:

- Preservar os originais e processar um vídeo por vez.
- Validar o resultado antes de liberá-lo na saída.
- Manter nomes legíveis sem sobrescrever arquivos de outro conteúdo.
- Preservar compatibilidade com registros existentes ou fornecer uma migração testada.
- Atualizar a documentação ao mudar configuração, estados, notificações ou comandos.

## Descrever uma proposta

Ao relatar um problema, informe o comportamento esperado, o observado, a versão do Windows e a etapa que falhou. Use nomes e caminhos fictícios ao compartilhar mensagens de erro. Não envie evidências de trabalho, vídeos pessoais, `appsettings.json`, `.state`, `status.json` ou `history.jsonl`.

Uma proposta de alteração deve explicar o problema, a mudança de comportamento e como foi validada. O código usa Apache License 2.0, com avisos de terceiros preservados; veja a [preparação para publicação](docs/PUBLICACAO.md).

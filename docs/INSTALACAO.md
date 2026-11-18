# Instalar o Compressor no Windows

Execute `Compressor-0.2.0-Setup-x64.exe` e siga o assistente em português. A instalação é para seu usuário, sem exigir instalação manual do .NET ou do FFmpeg.

Mantenha a internet conectada até a conclusão. O instalador inclui o runtime e prepara os recursos de vídeo diretamente do fornecedor, conferindo tamanho e SHA-256. Esse preparo pode baixar aproximadamente 200 MB. Após concluir, abra o atalho **Jira Video Compressor**, confira as pastas e clique em **Salvar e iniciar**. As pastas iniciais ficam em Documentos/Jira Video Compressor.

O assistente cria um atalho no menu Iniciar e oferece outro na área de trabalho. Não configura início automático com o Windows. Fechar a janela mantém o aplicativo na bandeja; use **Sair** para encerrá-lo.

## Falhas e atualizações

Se o preparo falhar, o instalador explica como tentar novamente. Abra o aplicativo pelo atalho e use **Tentar preparar novamente**, ou execute o instalador outra vez com internet. O monitoramento permanece bloqueado até os recursos funcionarem.

Encerre o aplicativo pelo menu da bandeja antes de atualizar. As preferências, ferramentas e registros de duplicatas ficam em `%LOCALAPPDATA%\JiraVideoCompressor`, separados do programa. Reinstalar preserva esses dados; desinstalar também os mantém. Os vídeos originais e os resultados nas pastas escolhidas não são removidos pelo desinstalador.

O programa fica em `%LOCALAPPDATA%\Programs\JiraVideoCompressor`. O instalador ainda não possui assinatura digital; o Windows pode identificar o editor como desconhecido. Obtenha o arquivo somente da distribuição do projeto e confira seu SHA-256.

## Gerar o instalador

Para desenvolvimento, instale [Inno Setup 6.7.4](https://jrsoftware.org/inno-setup/downloads) e execute:

```powershell
./scripts/Build-Installer.ps1 -Version 0.2.0
```

O script gera o pacote autocontido e valida seu manifesto antes de criar o EXE e seu `.sha256` em `artifacts`. Para reaproveitar um pacote público já gerado:

```powershell
./scripts/Build-Installer.ps1 -Version 0.2.0 -PackageDirectory "artifacts/pacote/JiraVideoCompressor-windows-x64" -CompilerPath "C:/Program Files (x86)/Inno Setup 6/ISCC.exe"
```

O instalador não incorpora FFmpeg; os executáveis são obtidos no computador de destino. Assim como no portátil público, os componentes mantêm suas próprias licenças. A construção usa instalação por usuário com `PrivilegesRequired=lowest`, conforme a [documentação do Inno Setup](https://jrsoftware.org/is6help/topic_setup_privilegesrequired.htm).

## Validação

`tests/InstallerPayload.ps1` extrai o instalador com um unpacker informado pelo desenvolvedor, confere os hashes dos arquivos, runtime e ausência de configurações pessoais. Executa a preparação e o aplicativo extraídos com FFmpeg real, vídeo sintético e configuração isolada; confere cópia, original e ausência de duplicatas após reinício. O teste reutiliza uma instalação de FFmpeg conhecida e não simula um download bem-sucedido.

Esse teste não instala atalhos nem registra o desinstalador. A instalação completa, atualização, download em uma máquina nova e desinstalação devem ser verificadas em uma máquina Windows de teste antes de publicar a Release.

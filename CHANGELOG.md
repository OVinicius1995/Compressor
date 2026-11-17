# Histórico de alterações

## Em preparação — primeira publicação

### Funcionalidades

- Monitoramento de pasta com espera por estabilidade e disponibilidade do arquivo.
- Fila sequencial, cópia dentro do limite e compressão H.264 sem áudio em duas passagens.
- Alvo de 9.500.000 bytes e validação do máximo de 10.000.000 bytes antes da liberação.
- Preservação dos originais e reconhecimento de duplicatas por conteúdo.
- Janela para selecionar pastas e acompanhar etapas e percentual.
- Preferências persistidas no `appsettings.json` local.
- Ícone de rolo compactador no executável, janela, barra de tarefas e bandeja.
- Notificações de conclusão por vídeo, com fila de avisos.
- Snapshot `status.json` e histórico `history.jsonl` para acompanhamento e integração.
- Modos de console, execução única e processamento manual.

### Correções e recuperação

- Nome original com `_compressed` e numeração para resolver colisões.
- Migração de resultados legados com nomes em hash, sem recodificar.
- Reconstrução com nome amigável quando o resultado antigo foi apagado.
- Retomada de migrações interrompidas e proteção contra sobrescrita de resultados alterados.
- Cancelamento sem liberar resultado parcial.

### Documentação e validação

- README, guia de uso, contrato de status e instruções de desenvolvimento.
- Testes com FFmpeg real e teste da interface Windows.
- Configuração de exemplo independente da máquina e exclusões de arquivos locais no Git.

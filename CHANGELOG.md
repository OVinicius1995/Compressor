# Histórico de alterações

## v0.4.0 — 2026-11-19

- Cópia integral de vídeos de até 20.000.000 bytes, inclusive. Acima desse limite, a compressão mira 19.000.000 bytes.
- Migração automática das configurações com o limite e o alvo padrão anteriores.
- Exclusão agendada para uma data e um horário específicos, com execução única, além da opção diária.

## v0.3.0 — 2026-11-18

- Aviso de atualização pelo painel e acesso à versão estável no GitHub.
- Limpeza dos vídeos concluídos no grid, preservando os arquivos e o histórico.
- Agendamento diário de exclusão dos originais, dos resultados ou de ambos, com Lixeira ou exclusão permanente confirmada e registrada.

## v0.2.0

- Instalador Windows x64 por usuário, com runtime, atalhos, desinstalador e preparação validada dos recursos de vídeo durante a instalação.
- Preferências e histórico separados do programa, preservados em atualizações e desinstalação.
- Geração do instalador com manifesto e SHA-256, teste do conteúdo extraído e inclusão no fluxo de distribuição.

- Tema escuro compartilhado, campos com rótulos acima e ações agrupadas, preservando o rolo compactador.
- Alertas com o mesmo tema, foco de teclado visível, adaptação ao alto contraste e rolagem para conteúdo ampliado.
- Atualizações da fila preservam seleção e rolagem; testes visuais de tamanho mínimo e geometria ampliada.

- Interface sem escolha de FFmpeg/FFprobe: instalação automática em pasta fixa, com migração da configuração antiga.
- Validação dos recursos ao abrir e antes de monitorar; alerta claro, bloqueio e botão de recuperação em caso de falha.

- Modo Movavi ativo por padrão para ignorar MKV provisórios no monitor e evitar trabalho duplicado.
- Perfil rápido com uma passagem/veryfast; perfil preciso anterior disponível no painel.
- Recuperação só quando o tamanho exceder o limite, com bitrate recalculado e teto configurável de tentativas.
- Preferências de Movavi e compressão salvas/restauradas com as pastas.
- Testes de exportação Movavi, modo manual MKV, filtros, perfis, recuperação e limite final.

## v0.1.0 — 2026-11-17

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

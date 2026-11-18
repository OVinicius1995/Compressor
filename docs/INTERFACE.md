# Interface e experiência de uso

O tema compartilhado está em `VisualTheme.cs`. Novas telas devem reutilizar seus tokens e componentes: fundo escuro, campos com contraste, Segoe UI e verde reservado à ação principal. A marca continua sendo o rolo compactador do Compressor.

As configurações apresentam rótulos acima das pastas. Ações ficam agrupadas à direita, com foco visível e estados desabilitados. A preparação de recursos bloqueia o início com orientação textual. Os alertas usam o mesmo tema e podem ser concluídos com Enter ou Esc. A seleção de pastas continua usando o diálogo nativo do Windows.

O progresso combina texto e barra; a fila mantém estados por escrito. Atualizações preservam a seleção e a rolagem da tabela. Mensagens longas podem ser consultadas pelo tooltip. Fechar a janela mantém o acesso pela bandeja.

O alto contraste do Windows tem prioridade sobre as cores da marca. As dimensões são lógicas e a janela permite rolagem quando o conteúdo ampliado não cabe. A barra de título usa o tema escuro quando o Windows oferece suporte; em outros ambientes mantém a aparência nativa.

## Verificação

`tests/UiSmoke` verifica os fluxos existentes, foco dos campos, acesso dos controles pelo teclado, tamanho mínimo, ausência de sobreposição do rodapé e geometria ampliada em 125%, 150% e 200%. Também gera prévias da janela e do alerta em `.test-artifacts`.

As ampliações são simulações de layout, não testes de mudança real do DPI do monitor. Antes da distribuição, conferir manualmente troca entre monitores, escalas reais, alto contraste e leitor de tela. A renderização do título, dos seletores nativos e das barras de rolagem depende do Windows.

O documento privado de referência visual fica fora do Git e dos pacotes de distribuição.

# Status e integração com widgets

[Voltar ao README](../README.md)

## Exemplo do snapshot

Exemplo ilustrativo de um vídeo durante a segunda passagem. Os nomes e caminhos abaixo são fictícios:

```json
{
  "schemaVersion": 1,
  "updatedAt": "2026-11-17T12:00:00+00:00",
  "running": true,
  "processId": 1234,
  "waiting": 0,
  "compressing": 1,
  "processing": 1,
  "completed": 0,
  "failed": 0,
  "jobs": [
    {
      "sourcePath": "C:\\Videos\\Entrada\\evidencia.mp4",
      "fileName": "evidencia.mp4",
      "state": "compressing",
      "percent": 62.4,
      "message": "Tentativa 1/3 · passagem 2/2.",
      "outputPath": null,
      "updatedAt": "2026-11-17T12:00:00+00:00"
    }
  ]
}
```

## Status para o futuro widget

Dois arquivos são gravados **ao lado do appsettings.json**, independentemente das pastas escolhidas:

- `status.json`: fotografia atual, substituída usando temporário e movimento atômico. Contém `schemaVersion: 1`, `updatedAt` em UTC, `running`, `processId`, contadores e `jobs`.
- `history.jsonl`: histórico persistente, um objeto JSON por linha para cada mudança de estado ou etapa. Não é apagado ao reiniciar. Não contém todas as amostras de percentual; para progresso atual, leia `status.json`.

Campos de cada trabalho: `sourcePath`, `fileName`, `state`, `percent`, `message`, `outputPath` e `updatedAt`. Estados estáveis: `waiting`, `hashing`, `analyzing`, `compressing`, `copying`, `validating`, `completed`, `skipped`, `failed`, `canceled`, `removed`. `outputPath` é preenchido ao concluir ou reconhecer resultado existente.

Contadores: `waiting` são vídeos aguardando estabilidade/disponibilidade ou sua vez; `compressing` vale 0 ou 1; `processing` inclui hash, análise, compressão, cópia e validação, também no máximo 1; `completed` inclui `completed` e `skipped`; `failed` conta erros. Os contadores/lista representam a sessão e são reconstruídos a cada início. O histórico preserva sessões anteriores.

O percentual usa `out_time_us` do FFmpeg: primeira passagem de 0 a 49,5%, segunda de 49,5 a 99%. A cópia usa bytes transferidos. 100% só é registrado após validação e liberação. Uma nova tentativa de bitrate reinicia o percentual e informa a tentativa em `message`; não é uma estimativa de tempo restante.

O snapshot é atualizado nas mudanças de etapa e, durante progresso, no máximo a cada 500 ms, além de heartbeat durante monitoramento. O widget pode consultar a cada segundo. Em .NET, abra com `FileShare.ReadWrite | FileShare.Delete` para permitir a substituição; outros leitores devem fechar o arquivo após cada leitura. Após encerramento forçado, `running` pode permanecer verdadeiro: verifique o PID e a idade de `updatedAt`, considerando `PollSeconds`.

Não edite esses arquivos com o monitor ativo. O histórico não tem rotação automática; arquive-o com o aplicativo parado quando necessário. Ambos contêm caminhos locais e ficam fora do Git.


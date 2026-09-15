# API de sincronização

A `ScanFace.SyncApi` é opcional. Ela persiste um payload opaco por identificador de cofre e não executa descriptografia.

## Executar com Docker

```powershell
$env:SCANFACE_SYNC_TOKEN = "gere-um-token-longo-e-aleatorio"
docker compose up --build -d
Invoke-RestMethod http://localhost:8080/health
```

O volume `scanface-data` preserva o SQLite. Variáveis aceitas:

| Variável | Descrição | Padrão |
|---|---|---|
| `SCANFACE_SYNC_TOKEN` | token exigido no cabeçalho `X-ScanFace-Token` | obrigatório fora de desenvolvimento |
| `SCANFACE_DB_PATH` | caminho do SQLite da API | `/data/scanface-sync.db` na imagem |
| `ASPNETCORE_URLS` | endereço de escuta | `http://+:8080` na imagem |

## Contrato HTTP

### Saúde

`GET /health` não exige token.

### Obter snapshot

`GET /api/v1/vaults/{vaultId}` com `X-ScanFace-Token`. Retorna `404` quando ainda não há cópia remota.

### Criar ou atualizar snapshot

`PUT /api/v1/vaults/{vaultId}` com:

```json
{
  "payload": "BASE64_DO_SNAPSHOT_CIFRADO"
}
```

Para atualizar uma versão existente, envie `If-Match` com o número retornado pela leitura anterior. A API responde `409 Conflict` quando a versão já mudou. O limite por payload é 20 MB.

## Produção

Coloque a API atrás de um proxy HTTPS, gere um token aleatório com pelo menos 32 bytes e restrinja acesso ao volume. O token autentica a implantação inteira neste MVP; para vários usuários, um trabalho futuro deve introduzir tokens por cofre, rotação, rate limiting e auditoria sem conteúdo sensível.

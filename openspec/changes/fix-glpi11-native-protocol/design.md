## Context

Dois agentes compartilham o mesmo protocolo GLPI nativo: o agente Go
(`internal/transport/server/`, um binário para linux/mac/windows) e o agente
.NET (`dotnet-glpi-agent/`, foco Windows/dotnet). Ambos implementam CONTACT +
inventário JSON com fallback para XML/PROLOG legado.

Em teste de campo contra um GLPI 11 de exemplo (`https://glpi.example.com`),
o agente Go falhava em duas etapas:

1. `CONTACT` → `400 "JSON not well formed!"`. Causa: o corpo era comprimido em
   zlib (`application/x-compress-zlib`) por padrão e o endpoint
   `/front/inventory.php` do GLPI 11 não o inflava; além disso o CONTACT não
   enviava `version`. O erro 400 disparava o fallback legado, e o Nginx passava a
   registrar o User-Agent `FusionInventory-Agent...` — escondendo o erro real.
2. Inventário → `500 "keys ignored: accountinfo"`. Causa: o JSON colocava a `tag`
   em `content.accountinfo`, mas o schema nativo (`inventory.schema.json`) não
   tem essa chave; `tag` é um campo de raiz.

Um `curl` com `application/json` puro, incluindo `version`, retornou `200 OK`,
confirmando o diagnóstico. As correções foram aplicadas no Go e validadas
(`native CONTACT accepted`, `native JSON inventory sent`, 216862 bytes, 200).

Estado do .NET: já exclui `content.accountinfo` (comentado no
`InventoryContentMapper`) e já envia `tag` na raiz (`NativeInventoryMessage`).
Porém o `ContactMessage` não tem `version` e `GlpiProtocolOptions.Compression`
tem default `Auto`, que em `Compress()` vira zlib para o nativo — reproduzindo os
mesmos dois gaps do Go antes do fix.

## Goals / Non-Goals

**Goals:**
- Fazer o inventário nativo ser aceito por GLPI 11+ nos dois agentes.
- CONTACT nativo sempre com `version` e `name`.
- Protocolo nativo (CONTACT + inventário) enviado como `application/json` sem
  compressão por padrão nos dois agentes.
- Inventário sem chaves fora do schema (`accountinfo`); `tag` na raiz.
- Não mascarar erros JSON nativos com fallback legado.
- Paridade de comportamento entre agente Go e .NET.

**Non-Goals:**
- Alterar o fluxo legado XML/PROLOG (continua comprimido em zlib).
- Suportar compressão negociada (gzip/deflate) no protocolo nativo — reservado a
  trabalho futuro caso um GLPI aceite corpo comprimido.
- Mudanças em coletores de inventário ou no schema de dados interno.
- Scheduling lazy avançado a partir da resposta do CONTACT.

## Decisions

- **Protocolo nativo sempre `application/json` (sem compressão).** Alternativa
  considerada: manter zlib como padrão e só desligar via `no-compression`. Rejeitada
  porque o GLPI 11 rejeita o corpo comprimido no CONTACT e o objetivo é
  funcionar out-of-the-box. A compressão do nativo passa a ser um não-uso; o flag
  `no-compression` deixa de ter efeito no nativo (documentado). No Go, `postJSON`
  chama `post(..., "application/json", ..., compress=false)`. No .NET, o
  CONTACT/inventário nativos usam `CompressionKind.None` (ou o default de
  `GlpiProtocolOptions.Compression` passa a `None`), mantendo `Zlib` só no legado.

- **`version` no CONTACT.** Go: campo `Version` em `contactMessage`, preenchido de
  `version.Version`. .NET: parâmetro `version` no `ContactMessage`, preenchido da
  identidade/versão do agente (mesma fonte do `User-Agent`). Sem isso o GLPI 11
  responde 400.

- **`tag` na raiz, sem `accountinfo`.** Go: remover o campo `AccountInfo` de
  `jsonContent` e adicionar `Tag` em `jsonMessage`, preenchido de `inv.Tag`. O
  fluxo XML legado continua usando `ACCOUNTINFO/TAG`. .NET: já correto — apenas
  cobrir com teste de regressão.

- **Sem fallback legado em erro JSON nativo.** Go: `post` retorna um erro tipado
  `serverError{status, body}`; `Send` só faz fallback quando o erro não é um erro
  JSON nativo (corpo não começa com `{`). .NET: `IsExplicitLegacyResponse` já é
  estrito (só degrada em 404/405/415/501 ou "unsupported protocol"); manter e
  cobrir com teste.

- **Um único change para Go e .NET.** Alternativa: dois changes separados.
  Rejeitada porque é a mesma mudança de protocolo e a paridade Go↔.NET é um
  requisito recorrente do projeto; um change mantém os dois alinhados.

## Risks / Trade-offs

- **[Inventário nativo não comprimido pode ser maior no fio]** (216 KB no host de
  teste) → Mitigação: `client_max_body_size` do Nginx recomendado é 80M; payloads
  reais ficam muito abaixo. A compressão nativa pode ser reintroduzida via
  negociação se um servidor a exigir.
- **[GLPI 10 (não 11) poderia esperar zlib]** → Mitigação: GLPI 10 nativo também
  aceita `application/json` não comprimido (mesmo endpoint/handler); risco baixo.
  Sem ambiente GLPI 10 para regressão automatizada.
- **[Sem VM de GLPI 11 no CI]** → Mitigação: validação manual já feita no Go
  (200 OK); testes unitários cobrem a forma do JSON (presença de `version`, `tag`
  na raiz, ausência de `accountinfo`) e a decisão de fallback. Dump via
  `GFI_DUMP_JSON` permite validar offline contra `inventory.schema.json`.
- **[`no-compression` vira no-op no nativo]** → Mitigação: documentar; o flag
  continua válido conceitualmente mas o nativo já é o comportamento sem
  compressão.

## Migration Plan

1. Go: mudanças já aplicadas em `json.go`, `client.go`, `json_test.go`
   (validadas contra GLPI 11).
2. .NET: adicionar `version` ao `ContactMessage` e enviar nativo sem compressão;
   adicionar/ajustar testes de protocolo (CONTACT com version, inventário sem
   accountinfo, tag na raiz, sem fallback em erro JSON nativo).
3. Rodar `go test ./...` e a suíte .NET (`dotnet test`).
4. Commit + push; opcionalmente cortar release (pacotes linux/mac/windows/dotnet).
5. Rollback: reverter o commit; o protocolo anterior volta ao comportamento com
   zlib (que falha em GLPI 11, mas funciona em servidores que aceitam zlib).

## Open Questions

- Algum servidor GLPI no parque exige corpo nativo comprimido? Se sim, avaliar
  negociação de compressão em vez de desabilitar de vez. (Assunção atual: não.)
- A `version` do CONTACT deve ser a versão pura (`0.5.0`) ou a string de
  User-Agent? Decisão: versão pura, alinhada ao payload de `curl` que retornou 200.

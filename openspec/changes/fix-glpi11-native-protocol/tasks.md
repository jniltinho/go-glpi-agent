## 1. Agente Go (linux/mac/windows) — já implementado, validado contra GLPI 11

- [x] 1.1 `json.go`: adicionar campo `version` ao `contactMessage` e preenchê-lo de `version.Version` em `BuildContactJSON`; garantir `name` sempre presente.
- [x] 1.2 `json.go`: remover `AccountInfo` de `jsonContent` e adicionar `Tag` na raiz de `jsonMessage`, preenchido de `inv.Tag`.
- [x] 1.3 `client.go`: `postJSON` envia sempre `application/json` sem compressão (zlib restrito ao `postXML` legado).
- [x] 1.4 `client.go`: erro tipado `serverError{url,status,body}`; `Send` não faz fallback legado quando o corpo do erro é JSON nativo (começa com `{`).
- [x] 1.5 `json_test.go`: `TestBuildContactJSON` cobrindo `version`, `name` e tasks.
- [x] 1.6 `gofmt`, `go vet`, `go test ./...` verdes; binário linux/amd64 rebuildado (`make build-all`).

## 2. Agente .NET (dotnet-glpi-agent)

- [x] 2.1 `NativeJsonSerializer.cs`: `ContactMessage` ganhou `version` (JsonPropertyName `version`), preenchido de `snapshot.Identity.AgentVersion`.
- [x] 2.2 `GlpiClient.cs`: caminho nativo com `Compression = Auto` (default) passa a enviar `application/json` sem compressão; `Zlib` só no fluxo legado; override explícito (Gzip/Zlib) ainda honrado.
- [x] 2.3 Regressão adicionada (`SerializeInventory_PutsTagAtRootAndOmitsAccountinfo`): `tag` na raiz e `content` sem `accountinfo` (mapper já correto).
- [x] 2.4 Coberto por `SubmitAsync_SchemaError_IsCategorizedWithoutLegacyFallback`: erro JSON nativo (400/500) NÃO dispara fallback legado.

## 3. Testes e verificação

- [x] 3.1 `dotnet test` verde: Protocol 28/28, Core 54/54, Windows 44/44, Integration 12/12 (SDK .NET 10.0.301). Novos casos: CONTACT com `version`/`name`, `Auto → application/json`, `tag` na raiz sem `accountinfo`.
- [x] 3.2 JSON validado contra o schema: golden fixture `native-inventory.json` + `GlpiSchemaValidatorTests` (apenas chaves previstas). Go via `GFI_DUMP_JSON` disponível.
- [x] 3.3 Paridade Go↔.NET confirmada: CONTACT com `deviceid`/`name`/`version`/tasks e envelope de inventário com `tag` na raiz sem `accountinfo` nos dois agentes.

## 4. Documentação e entrega

- [ ] 4.1 Atualizar `CHANGELOG` com o fix do protocolo GLPI 11 (CONTACT `version`, nativo sem compressão, `tag` na raiz, sem fallback em erro nativo).
- [ ] 4.2 Documentar que `no-compression` é no-op no protocolo nativo (já é o padrão) onde o flag estiver documentado.
- [ ] 4.3 Commit + push das mudanças Go e .NET; opcionalmente cortar release com pacotes linux/mac/windows/dotnet.
- [ ] 4.4 Arquivar o change (`/opsx:archive`) após implementação e verificação.

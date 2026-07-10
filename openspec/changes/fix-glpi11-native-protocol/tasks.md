## 1. Agente Go (linux/mac/windows) — já implementado, validado contra GLPI 11

- [x] 1.1 `json.go`: adicionar campo `version` ao `contactMessage` e preenchê-lo de `version.Version` em `BuildContactJSON`; garantir `name` sempre presente.
- [x] 1.2 `json.go`: remover `AccountInfo` de `jsonContent` e adicionar `Tag` na raiz de `jsonMessage`, preenchido de `inv.Tag`.
- [x] 1.3 `client.go`: `postJSON` envia sempre `application/json` sem compressão (zlib restrito ao `postXML` legado).
- [x] 1.4 `client.go`: erro tipado `serverError{url,status,body}`; `Send` não faz fallback legado quando o corpo do erro é JSON nativo (começa com `{`).
- [x] 1.5 `json_test.go`: `TestBuildContactJSON` cobrindo `version`, `name` e tasks.
- [x] 1.6 `gofmt`, `go vet`, `go test ./...` verdes; binário linux/amd64 rebuildado (`make build-all`).

## 2. Agente .NET (dotnet-glpi-agent)

- [ ] 2.1 `NativeJsonSerializer.cs`: adicionar campo `version` ao `ContactMessage` (JsonPropertyName `version`), preenchido da versão do agente (mesma fonte do User-Agent em `snapshot.Identity`).
- [ ] 2.2 `GlpiClient.cs`/`GlpiProtocolOptions.cs`: enviar CONTACT e inventário nativos sem compressão (`application/json`) por padrão — deixar `Zlib` apenas no fluxo legado. Ajustar o default de `Compression` ou forçar `None` no caminho nativo.
- [ ] 2.3 Confirmar (com teste de regressão) que o mapper não emite `content.accountinfo` e que `tag` sai na raiz do envelope de inventário.
- [ ] 2.4 Confirmar (com teste) que uma resposta de erro JSON nativo (400/500 com corpo `{...}`) NÃO dispara fallback legado.

## 3. Testes e verificação

- [ ] 3.1 `dotnet test` da suíte de protocolo verde (CONTACT com `version`, nativo sem compressão, inventário sem `accountinfo`, `tag` na raiz, sem fallback em erro JSON).
- [ ] 3.2 Validar offline o JSON gerado contra `inventory.schema.json` (Go via `GFI_DUMP_JSON`; .NET via writer local), garantindo apenas chaves previstas no schema.
- [ ] 3.3 Confirmar paridade Go↔.NET: mesmos campos de CONTACT e mesmo envelope de inventário.

## 4. Documentação e entrega

- [ ] 4.1 Atualizar `CHANGELOG` com o fix do protocolo GLPI 11 (CONTACT `version`, nativo sem compressão, `tag` na raiz, sem fallback em erro nativo).
- [ ] 4.2 Documentar que `no-compression` é no-op no protocolo nativo (já é o padrão) onde o flag estiver documentado.
- [ ] 4.3 Commit + push das mudanças Go e .NET; opcionalmente cortar release com pacotes linux/mac/windows/dotnet.
- [ ] 4.4 Arquivar o change (`/opsx:archive`) após implementação e verificação.

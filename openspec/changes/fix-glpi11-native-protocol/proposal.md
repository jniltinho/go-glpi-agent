## Why

O GLPI 11 rejeita o inventário enviado pelo agente Go: o `CONTACT` retornava
`400 "JSON not well formed!"` e o inventário retornava `500 "keys ignored:
accountinfo"`. Duas causas foram confirmadas em campo (host real contra
um GLPI 11 de exemplo `https://glpi.example.com`): (1) o corpo nativo era comprimido em
zlib e o endpoint `/front/inventory.php` não o inflava, além de faltar o campo
`version` no CONTACT; (2) a chave `content.accountinfo` não existe no schema
nativo e a `tag` da entidade é um campo de raiz, não de `content`. O agente
ainda caía no fallback legado a cada 400, mascarando o erro real do servidor.

As correções já foram aplicadas e validadas no agente Go (`native CONTACT
accepted`, `native JSON inventory sent`, HTTP 200). Esta proposta consolida o
ajuste como requisito e o propaga ao agente .NET, que compartilha o mesmo
protocolo e ainda tem os mesmos gaps (CONTACT sem `version`, compressão nativa
`Auto`→zlib por padrão).

## What Changes

- **CONTACT nativo passa a incluir `version`** (versão do agente) e sempre um
  `name` — sem esses campos o GLPI 11 responde `400 "JSON not well formed!"`.
- **O protocolo nativo passa a enviar `application/json` sem compressão por
  padrão** (CONTACT e inventário). A compressão zlib fica reservada ao fluxo
  legado (XML/PROLOG). **BREAKING** para o requisito atual "corpo comprimido com
  zlib por padrão" do protocolo nativo.
- **O inventário JSON não inclui `content.accountinfo`** (chave inexistente no
  schema, rejeitada com `500`); a `tag` da entidade é enviada como campo de raiz
  do envelope, ao lado de `deviceid`/`action`/`itemtype`/`content`.
- **Sem fallback legado quando o servidor devolve um erro JSON nativo** (ex.: 400
  "JSON not well formed", 500 de schema): o erro real do GLPI é propagado em vez
  de disparar uma tentativa XML/PROLOG que falharia igual e confundiria o log
  (User-Agent legado no Nginx).
- Propagação das mesmas garantias ao **agente .NET** (linux/mac/windows/dotnet),
  que compartilha o protocolo: adicionar `version` ao CONTACT e enviar o nativo
  sem compressão por padrão. (`accountinfo` e `tag` na raiz já estão corretos no
  .NET.)

## Capabilities

### New Capabilities

_(nenhuma)_

### Modified Capabilities

- `glpi-native-protocol`: a requisição CONTACT ganha `version` e `name`
  obrigatórios; a serialização de inventário exclui chaves fora do schema
  (`accountinfo`) e envia `tag` na raiz; a regra de headers/compressão passa a
  ser `application/json` sem compressão por padrão no protocolo nativo; e a
  detecção nativo-vs-legado não faz fallback quando o servidor responde com um
  erro JSON nativo.

## Impact

- **Go (linux/mac/windows — mesmo código de transporte):**
  - `internal/transport/server/json.go` — CONTACT com `version`; `content` sem
    `accountinfo`; `tag` na raiz do inventário. _(já implementado)_
  - `internal/transport/server/client.go` — `postJSON` sempre `application/json`;
    `serverError` tipado e sem fallback legado em erro JSON nativo. _(já
    implementado)_
  - `internal/transport/server/json_test.go` — cobertura do CONTACT. _(já
    implementado)_
- **.NET (dotnet-glpi-agent):**
  - `src/DotnetGlpiAgent.Protocol/Serialization/NativeJsonSerializer.cs` —
    `ContactMessage` ganha `version`.
  - `src/DotnetGlpiAgent.Protocol/Transport/GlpiClient.cs` /
    `GlpiProtocolOptions.cs` — protocolo nativo enviado sem compressão por
    padrão (a compressão `Auto` deixa de aplicar zlib ao CONTACT/inventário).
  - Testes de protocolo correspondentes.
- **Compatibilidade:** o alvo é GLPI 11+; GLPI 10 nativo também aceita JSON não
  comprimido, então não há regressão esperada. O flag `no-compression` deixa de
  ter efeito no protocolo nativo (já é o padrão).
- **Sem mudança** no fluxo legado XML/PROLOG (`glpi-transport`), que continua
  comprimindo em zlib.

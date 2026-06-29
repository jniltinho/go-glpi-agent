# Análise do GLPI Agent (Perl) — melhorias para o projeto Go

Notas da leitura de `glpi-agent/` (GLPI Agent 1.19, sucessor do fusioninventory-agent).
Objetivo: identificar o que vale incorporar no `go-fusioninventory-agent`.

## Achado central: existem DOIS protocolos

`lib/GLPI/Agent/Task/Inventory.pm:284` decide o destino por `isGlpiServer()`:

| Protocolo | Cliente Perl | Formato | Quando |
|---|---|---|---|
| **GLPI nativo** (moderno) | `HTTP/Client/GLPI.pm` | **JSON** (`Cpanel::JSON::XS`) | GLPI 10/11 com inventário nativo |
| **OCS/FusionInventory** (legado) | `HTTP/Client/OCS.pm` | **XML** (PROLOG→INVENTORY) | plugin glpiinventory antigo |

Nosso Go hoje implementa **apenas o legado (XML + PROLOG)**.

### Detecção: CONTACT (não PROLOG) no fluxo moderno
O moderno **não usa PROLOG** — usa uma requisição **CONTACT** JSON primeiro
(`Protocol/Contact.pm`, `action="contact"`), enviando `deviceid`,
`installed-tasks`, `enabled-tasks`, `tag`. O servidor responde com suporte a
tasks/config. Só então envia o inventário (`action="inventory"`). O PROLOG/XML
é **fallback puramente legado** (`Agent.pm:355`), usado quando o CONTACT falha
ou o alvo não é GLPI. Confirma: **PROLOG não é o caminho do GLPI atual.**

### Formato: JSON é o PADRÃO
`Task/Inventory.pm:174`: `my $format = 'json';` — só vira `'xml'` quando o alvo
**não** é GLPI server. Content-Type: `application/json` (sem compressão) ou
`application/x-compress-zlib` / `-gzip`.

### Protocolo nativo (JSON)
- `Protocol/Inventory.pm:165`: mensagem com campos `deviceid`, `action`, `content`, `itemtype`, `partial`.
- Há um **schema JSON** server-side (`inventory.schema.json`).
- `HTTP/Client/GLPI.pm`: envia POST com corpo comprimido; header **`GLPI-Agent-ID`** (UUID) e `GLPI-Request-ID`.

## agentid (UUID) — novidade vs fusioninventory
`Agent.pm:219` `agentid => uuid_to_string(...)`. É um **UUID v4 separado do deviceid**, enviado no header `GLPI-Agent-ID`. O deviceid continua `{name}-{YYYY-MM-DD-HH-MM-SS}` (`Agent.pm:651` confirma exatamente nosso formato).

Persistência: o GLPI agent salva em **`{vardir}/GLPI-Agent.dump`** (Storable) tanto o deviceid quanto o agentid. Implicação para nós: na migração, além de `FusionInventory-Agent.dump`, devemos também procurar `GLPI-Agent.dump` para importar deviceid (e agentid, se presente).

## User-Agent difere por protocolo
- Nativo GLPI: `GLPI-Agent_v{VERSION}`
- Config dir padrão moderno: `/etc/glpi-agent/agent.cfg`
Para o protocolo nativo devemos usar o User-Agent `GLPI-Agent`; para o legado, manter `FusionInventory-Agent`.

## OAuth2 (GLPI 11) — detalhes
Endpoint `/api.php/token`, grant `client_credentials`, scope `inventory`, com auto-refresh do Bearer (`HTTP/Client.pm:404`).

## Compressão
`HTTP/Client.pm:98`: ordem de preferência **zlib → gzip → none** (`no-compression` desativa). Usamos zlib fixo — ok, mas poderíamos negociar.

## OAuth (GLPI 11)
Config tem `oauth-client-id` / `oauth-client-secret`; `HTTP/Client.pm` trata Bearer token. GLPI 11 usa OAuth2 para autenticar o agente.

## Chaves de config novas relevantes
`json`, `glpi-version`, `itemtype`, `assetname-support`, `ssl-fingerprint`, `ssl-cert-file`/`ssl-key-file`/`ssl-keystore` (mTLS cliente), `logfile-maxsize`, `full-inventory-postpone`, `remote`/`remote-scheduling`/`remote-workers` (inventário remoto agentless via SSH/WinRM).

## Recomendações priorizadas para o Go

### P1 — alto valor, baixo/médio custo
1. **agentid UUID + header `GLPI-Agent-ID`**: gerar/persistir um UUID e enviá-lo. Necessário para GLPI moderno reconhecer o agente. Barato.
2. **Suporte ao protocolo nativo JSON** (`/front/inventory.php`): serializar o inventário em JSON conforme o schema do GLPI, POST com `GLPI-Agent-ID`. **Resolve de vez a questão PROLOG** e é o caminho do GLPI 10/11. Esforço médio (já temos o modelo de dados; falta um serializador JSON paralelo ao XML).
3. **Detecção de servidor (GET de capabilities)** para escolher JSON-nativo vs XML-legado automaticamente.

### P2 — melhorias incrementais
4. **Negociação de compressão** (zlib/gzip/none) e `no-compression`.
5. **Opções SSL extras**: `ssl-fingerprint`, mTLS cliente (`ssl-cert-file`/`ssl-key-file`).
6. **`logfile-maxsize`** (rotação simples do log em arquivo).

### P3 — escopo maior (v2+)
7. **OAuth2** (GLPI 11): `oauth-client-id/secret` + Bearer.
8. **Inventário remoto agentless** (`remote`): coletar de hosts via SSH — feature poderosa do GLPI agent.
9. Tasks adicionais (NetInventory, ESX) — já fora do escopo v1.

## Conclusão
O protocolo nativo JSON + agentid UUID (P1.1–P1.3) é **obrigatório para v1** — alvo de servidor é GLPI 10+. Isso alinha o Go ao GLPI atual, elimina a dependência do PROLOG no caminho principal e usa o endpoint oficial `/front/inventory.php`. O XML/PROLOG permanece como fallback automático para servidores com o plugin legado e para `--local`.

## Context

O FusionInventory Agent Perl usa 367 módulos `.pm` e depende de um ambiente Perl com CPAN instalado. A arquitetura de coleta é modular: cada módulo implementa `isEnabled()` + `doInventory()`, e o motor principal os descobre dinamicamente.

O alvo de servidor é **GLPI 10+** com inventário nativo: HTTP/JSON via `/front/inventory.php`, handshake **CONTACT**, header `GLPI-Agent-ID` (UUID). O protocolo legado HTTP/XML (OCS/FusionInventory + PROLOG) permanece como **fallback automático** para instalações com plugin antigo. O modo `--local` continua gerando XML para comparação com o Perl. Paridade bit-a-bit com o Perl é desejável para testes golden, mas não é requisito rígido.

A nova implementação Go vive na raiz do repositório; os projetos de referência em Perl ficam em `base/` (`base/perl/` = FusionInventory legado, `base/glpi-agent/` = GLPI Agent). Essa estrutura facilita comparar comportamento entre as implementações e manter CI unificado.

## Goals / Non-Goals

**Goals:**
- Binário único estático, sem dependências de runtime
- **Protocolo nativo GLPI 10+** (CONTACT + JSON + `GLPI-Agent-ID`) como caminho padrão em `--server`
- Fallback automático para XML/PROLOG em servidores legados (plugin FusionInventory/OCS)
- Paridade v1 com coletores Linux de uso frequente (~70% dos campos em host típico)
- Leitura do `agent.cfg` existente sem migração de configuração
- Coleta concorrente para reduzir tempo total de inventário
- Suporte a modo `--local` (XML) e `--server` (JSON nativo por padrão)
- Executável como daemon (`--daemon`) ou via systemd timer
- Device ID e agentid compatíveis com Perl/GLPI Agent para migração in-place

**Non-Goals:**
- Windows, macOS, BSD, AIX, Solaris (v1 somente Linux)
- NetDiscovery, NetInventory, Deploy, WakeOnLan, ESX (tarefas futuras)
- Interface web embutida (porta 62354) — pode ser adicionada depois
- SNMP (sem isso no v1)
- GPU, monitores, impressoras, PCI, IPMI, RAID controllers (v2)
- Software Snap/Flatpak/Gentoo/Slackware/Nix (v2)
- Reescrever o agente Perl — os dois coexistem

## Decisions

### D1: Monorepo — agente Go na raiz, referências Perl em `base/`

**Decisão**: o mesmo repositório abriga o novo agente Go na raiz e os projetos de referência em Perl em `base/` (`base/perl/`, `base/glpi-agent/`).

**Rationale**: facilita comparação direta de comportamento (rodar os dois no mesmo host, diff de XML), CI pode testar os dois em um único pipeline, e o histórico git fica unificado. O Perl continua como referência de comportamento durante a transição.

**Alternativa descartada**: repositório Go separado — dificulta testes de compatibilidade e divide o histórico git.

---

### D2: Interface Collector como ponto de extensão central

**Decisão**: toda coleta de dados implementa uma interface única:

```go
type Collector interface {
    Name() string
    IsEnabled(cfg *config.Config) bool
    Collect(ctx context.Context, inv *inventory.Inventory) error
}
```

O motor registra coletores, filtra `IsEnabled()`, e executa em goroutines com timeout configurável.

**Rationale**: espelha o padrão Perl (`isEnabled` + `doInventory`) e permite adicionar novos coletores sem alterar o motor. Testabilidade unitária por coletor.

**Alternativa descartada**: funções livres registradas via `init()` — dificulta testes e controle de ordem.

---

### D3: Estrutura de pacotes por domínio

```
go-fusioninventory-agent/         ← raiz do repositório = agente Go
├── cmd/fusioninventory-agent/    # main.go (binário: fusioninventory-agent)
├── internal/
│   ├── config/                   # parser de agent.cfg
│   ├── logger/                   # backends: stderr, file, syslog
│   ├── agent/                    # motor principal, scheduler, daemon, storage
│   ├── inventory/                # tipos de dados do inventário
│   │   └── model.go              # structs CPU, RAM, Disk, Network...
│   ├── collector/
│   │   ├── collector.go          # interface + registry
│   │   ├── generic/              # coletores cross-cutting
│   │   │   ├── hostname.go       # gopsutil/host.Info()
│   │   │   ├── timezone.go       # /etc/timezone, timedatectl
│   │   │   ├── users.go          # /etc/passwd, /etc/group, who, last
│   │   │   └── processes.go      # gopsutil/process (scan-processes=1)
│   │   └── linux/                # coletores Linux
│   │       ├── cpu.go            # gopsutil/cpu
│   │       ├── memory.go         # gopsutil/mem + dmidecode (slots)
│   │       ├── drives.go         # gopsutil/disk + lsblk (detalhes físicos)
│   │       ├── networks.go       # gopsutil/net + /proc/net/route
│   │       ├── os.go             # gopsutil/host + /etc/os-release
│   │       ├── bios.go           # /sys/class/dmi/id/, dmidecode
│   │       ├── lvm.go            # lvs, vgs
│   │       ├── usb.go            # /sys/bus/usb/devices/
│   │       ├── software_dpkg.go  # dpkg-query
│   │       ├── software_rpm.go   # rpm -qa
│   │       └── software_pacman.go # pacman -Q
│   └── transport/
│       ├── transport.go          # interface
│       ├── server/               # HTTP client + CONTACT/JSON (nativo) + PROLOG/XML (legado)
│       └── local/                # escrita em arquivo XML
├── testdata/                     # XMLs golden gerados pelo Perl
├── go.mod                        # módulo: go-fusioninventory-agent
├── Makefile
├── base/                         ← projetos de referência (intactos)
│   ├── perl/                     # FusionInventory Agent legado
│   └── glpi-agent/               # GLPI Agent
└── openspec/                     ← planejamento (este arquivo)
```

**Rationale**: `internal/` evita que pacotes externos dependam diretamente da implementação. Separação `generic/` vs `linux/` espelha a hierarquia Perl e facilita adicionar outros SOs no futuro.

---

### D4: XML gerado via structs com tags, não templates

**Decisão**: usar `encoding/xml` da stdlib com structs anotadas para gerar o XML de inventário.

**Rationale**: o XML do protocolo OCS/FusionInventory tem estrutura fixa e bem documentada. Structs com tags `xml:` garantem correção de tipos, são fáceis de testar (marshal → comparar campos) e não dependem de dependência externa.

**Critério de compatibilidade**: o GLPI aceita o XML e os campos/valores coincidem com o Perl para os coletores implementados. Diff bit-a-bit é meta secundária para testes golden.

**Alternativa descartada**: templates de texto (como o Perl usa) — frágeis para escaping e difíceis de testar.

---

### D5: Coleta concorrente com goroutines + timeout por coletor

**Decisão**: executar coletores em paralelo via `errgroup` com context timeout por coletor. O timeout padrão é lido de `backend-collect-timeout` no `agent.cfg` (padrão Perl: **180s**).

```
┌─────────────────────────────────────────────┐
│              Inventory Engine               │
│                                             │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  │
│  │ CPU      │  │ Memory   │  │ Network  │  │
│  │ Collector│  │ Collector│  │ Collector│  │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  │
│       │             │             │         │
│  ┌────▼─────────────▼─────────────▼─────┐  │
│  │         Inventory (goroutine-safe)    │  │
│  │     CPU{} + RAM{} + Network[]        │  │
│  └───────────────────────────────────┘  │
└─────────────────────────────────────────────┘
```

O `Inventory` struct usa mutex interno para writes concorrentes.

**Alternativa descartada**: execução sequencial — perde o maior ganho de performance do Go em relação ao Perl.

---

### D6: Device ID e persistência compatíveis com Perl

**Decisão**: o device ID **não é UUID**. Segue o formato Perl:

```
{hostname}-{YYYY}-{MM}-{DD}-{HH}-{MM}-{SS}
```

Exemplo: `srv-web-2026-06-29-15-30-45`

Persistência em `{vardir}/FusionInventory-Agent.json` (JSON legível). Na primeira execução do Go em host com agente Perl/GLPI instalado, importa o `deviceid` existente de:

1. `FusionInventory-Agent.dump` (Storable do fusioninventory-agent), ou
2. `GLPI-Agent.dump` (Storable do glpi-agent) — também guarda o **agentid** (UUID), que importamos quando presente
3. Caso contrário, gera novo ID no formato acima

O `vardir` padrão é `/var/lib/fusioninventory/agent` (mesmo do Perl em modo sistema).

**Rationale**: preservar device ID e agentid evita que o GLPI trate a máquina como novo ativo na migração Perl → Go.

**Alternativa descartada**: UUID v4 como deviceid — incompatível com GLPI/Perl existente. (Note que o GLPI usa UUID para o *agentid*, que é separado do deviceid — ver D12.)

---

### D7: Compatibilidade de configuração com agent.cfg

**Decisão**: o parser de config lê o formato INI do `agent.cfg` Perl e mapeia para uma struct Go. Chaves desconhecidas são ignoradas com aviso em modo debug.

**Chaves v1 mapeadas:**

| Chave Perl | Uso no Go v1 |
|---|---|
| `server`, `local` | Destino do inventário |
| `delaytime`, `lazy`, `force` | Scheduling daemon |
| `backend-collect-timeout` | Timeout por coletor (default 180) |
| `timeout` | Timeout HTTP |
| `no-category` | Desabilitar coletores |
| `scan-processes` | Coleta de processos |
| `tag` | Tag de entidade no XML |
| `user`, `password` | Autenticação HTTP |
| `proxy` | Proxy HTTP |
| `no-ssl-check`, `ca-cert-file`, `ca-cert-dir` | TLS |
| `logger`, `logfile`, `logfacility`, `debug` | Logging |
| `include` | Inclusão de arquivos `conf.d/` |
| `vardir` | Diretório de persistência |

**Chaves ignoradas no v1** (sem erro): `html`, `scan-homedirs`, `scan-profiles`, `additional-content`, `no-httpd`, `no-task`, `tasks`, `no-p2p`, `no-compression`, `color`, `conf-reload-interval`

**Rationale**: permite que usuários existentes usem o mesmo arquivo de configuração sem migração.

---

### D8: Uso de gopsutil/v3 como fonte primária de coleta

**Decisão**: usar `github.com/shirou/gopsutil/v3` como biblioteca principal. Parsing manual de `/proc/` e `/sys/` é **fallback** apenas para campos que gopsutil não cobre.

**Cobertura por pacote gopsutil:**

| Pacote gopsutil | Coletor | O que fornece |
|---|---|---|
| `cpu` | `linux/cpu.go` | modelo, fabricante, frequência, cores, threads |
| `mem` | `linux/memory.go` | RAM total, disponível, usada; swap |
| `disk` | `linux/drives.go` | partições, filesystem, mount, total/livre |
| `net` | `linux/networks.go` | interfaces, IPs, MACs, flags |
| `host` | `linux/os.go` | distro, kernel, uptime, boot time |
| `process` | `generic/processes.go` | PID, nome, usuário, CPU, memória |

**O que gopsutil NÃO cobre** (ferramentas/sysfs):

| Coletor | Fonte alternativa |
|---|---|
| `generic/users.go` | `/etc/passwd`, `/etc/group`, `who`, `last` |
| `generic/timezone.go` | `/etc/timezone`, `/etc/localtime`, `timedatectl` |
| `linux/bios.go` | `/sys/class/dmi/id/`, `dmidecode` |
| `linux/memory.go` (slots) | `dmidecode` type 17 |
| `linux/lvm.go` | `lvs`, `vgs` |
| `linux/usb.go` | `/sys/bus/usb/devices/` |
| `linux/software_*.go` | `dpkg-query`, `rpm -qa`, `pacman -Q` |
| detalhes físicos de disco | `lsblk --json`, `/sys/block/` |
| gateway padrão | `/proc/net/route` (fallback se gopsutil insuficiente) |

**Rationale**: gopsutil elimina parsing frágil e prepara expansão multiplataforma. As specs referenciam `/proc` como fonte de **dados** (o que o GLPI espera), não como implementação obrigatória.

---

### D9: Handshake antes do inventário — CONTACT (nativo) ou PROLOG (legado)

**Decisão**: o handshake depende do protocolo detectado (ver D12):
- **GLPI nativo**: requisição **CONTACT** JSON (`action=contact`) antes do inventário. A resposta informa tasks suportadas e configuração do servidor.
- **Legado (OCS/FusionInventory)**: requisição **PROLOG** XML; `PROLOG_FREQ` ajusta o `delaytime` do daemon.

**Rationale**: a análise do GLPI Agent 1.19 mostrou que o moderno **não usa PROLOG** — `Task/Inventory.pm` e `Daemon.pm` fazem CONTACT primeiro; PROLOG é fallback puramente legado. Implementar só PROLOG quebraria contra GLPI 10/11 nativo.

**v1**: handshake + inventário push. Pull de tarefas adicionais (deploy, etc.) fica fora de escopo.

---

### D12: Protocolo dual — nativo JSON (padrão) + XML/PROLOG (fallback)

**Decisão**: o transporte server suporta dois protocolos, com detecção automática:

```
                 ┌─────────────────────────────┐
   --server URL  │  detecta capacidade do GLPI │
        │        │  (CONTACT / resposta)       │
        ▼        └──────────────┬──────────────┘
   ┌─────────┐      nativo?     │
   │ Target  │──────── sim ─────┼──► JSON  + GLPI-Agent-ID (UUID)
   │ server  │                  │    action=contact → action=inventory
   └─────────┘──────── não ─────┴──► XML   + PROLOG → INVENTORY (zlib)
```

| Aspecto | Nativo (GLPI 10/11) | Legado (plugin) |
|---|---|---|
| Formato | JSON | XML |
| Handshake | CONTACT (`action=contact`) | PROLOG |
| Header de identidade | `GLPI-Agent-ID: <uuid>` | — |
| Content-Type | `application/json` ou `application/x-compress-zlib` | `application/x-compress-zlib` |
| User-Agent | `GLPI-Agent_v<ver>` | `FusionInventory-Agent_v<ver>` |
| Endpoint típico | `/front/inventory.php` | `/plugins/fusioninventory/` |

**agentid**: UUID v4 gerado e persistido (junto do deviceid), enviado no header `GLPI-Agent-ID`. Importado de `GLPI-Agent.dump` na migração quando existir.

**Compressão**: negociar zlib (default) / gzip / `none` via `no-compression`.

**Rationale**: o JSON nativo é o caminho do GLPI atual e elimina a dependência do PROLOG legado; o XML permanece como fallback para servidores com o plugin FusionInventory. Isso torna o agente Go compatível tanto com instalações modernas quanto antigas.

**Implementação**: o modelo de dados (`internal/inventory`) é único; haverá dois serializadores (`transport/server` XML já existe; adicionar JSON) e a seleção do cliente por detecção. OAuth2 (GLPI 11) fica como item P3 separado.

**Status v1**: **obrigatório** — o protocolo nativo é o caminho padrão para GLPI 10+. XML/PROLOG já existe parcialmente e serve como fallback + `--local`. Ver tasks seção 2.

---

### D10: Estratégia de implementação — transporte antes de expandir coletores

**Decisão**: priorizar o protocolo nativo GLPI 10+ (JSON + CONTACT + agentid) como bloqueante para `--server`. Em paralelo, manter XML para `--local` e fallback legado. Capturar XMLs golden do Perl para validar coleta; validar JSON contra GLPI 10+ real em homologação.

**Rationale**: sem o protocolo nativo, o agente não funciona contra GLPI 10+ (que não usa PROLOG). O XML golden continua útil para paridade de coleta e modo local.

---

### D13: Modelo de deploy systemd

**Decisão**: suportar dois modelos equivalentes:
- **`--daemon`**: processo longo com loop interno, `delaytime` e SIGTERM gracioso (comportamento Perl)
- **systemd timer + oneshot**: `fusioninventory-agent.service` (Type=oneshot) + `fusioninventory-agent.timer` (OnCalendar=hourly)

O timer é a opção recomendada em pacotes; `--daemon` permanece para compatibilidade com setups existentes.

---

### D11: Binário e CI

**Decisão**:
- Binário: `fusioninventory-agent` (substitui o pacote Perl de mesmo nome)
- Módulo Go: `go-fusioninventory-agent` (nome do repositório, sem prefixo de host)
- CI: GitHub Actions em `.github/workflows/go.yml` para o projeto Go; CircleCI em `base/perl/.circleci/` permanece como referência do Perl

## Parity Matrix (Perl Linux → Go)

| Módulo Perl | Go v1 | Go v2 | Notas |
|---|---|---|---|
| CPU (x86_64) | ✅ | | via gopsutil |
| Memory + dmidecode slots | ✅ | | slots dependem de root/dmidecode |
| BIOS/DMI | ✅ | | |
| Drives + lsblk | ✅ | | |
| LVM | ✅ | | |
| USB | ✅ | | |
| Networks | ✅ | | |
| Distro/OSRelease | ✅ | | |
| Hostname | ✅ | | |
| Timezone | ✅ | | |
| Users (LOCAL_USERS/GROUPS/USERS) | ✅ | | três seções como Perl |
| Processes | ✅ | | `scan-processes=1` |
| Software dpkg/rpm/pacman | ✅ | | |
| Software Snap/Flatpak/Nix/Gentoo | | ✅ | |
| Videos/GPU | | ✅ | |
| Screen/Monitors | | ✅ | |
| Printers | | ✅ | `no-category=printer` no Perl |
| PCI (Controllers/Sounds) | | ✅ | |
| IPMI (Lan/Fru) | | ✅ | crítico em datacenter |
| Storages RAID (Megacli/etc.) | | ✅ | crítico em servidores |
| DockerMacvlan/FibreChannel | | ✅ | |
| Firewall | | ✅ | |
| Batteries | | ✅ | |
| Domains/SSH/Environment | | ✅ | |

## Risks / Trade-offs

**Compatibilidade JSON com schema GLPI 10+** → o GLPI nativo rejeita inventários com campos ou estrutura incorreta.
*Mitigação*: validar contra GLPI 10+ real em homologação; serializador JSON alimentado pelo mesmo modelo de dados do XML; referência ao GLPI Agent 1.19.

**Compatibilidade XML imperfeita (fallback)** → servidores legados rejeitam XML com campos faltando.
*Mitigação*: XML golden do Perl; testes de integração com plugin FusionInventory quando fallback for acionado.

**Coletores dependem de ferramentas externas** (dmidecode, lsblk, who) → dados incompletos se ausentes.
*Mitigação*: cada coletor verifica disponibilidade e falha graciosamente (como o Perl). Logger avisa quando ferramenta ausente.

**Divergência de cobertura** → o Go v1 não cobre RAID, IPMI, GPU, Snap, etc.
*Mitigação*: matriz de paridade documentada; README lista lacunas; Perl permanece para casos avançados.

**Migração de device ID** → formato Storable Perl não é nativo em Go.
*Mitigação*: importação na primeira execução via leitura do `.dump` (regex no binário ou helper Perl no postinst).

**Manutenção paralela** → dois agentes durante a transição.
*Mitigação*: Perl como referência; Go como challenger. Deprecar Perl para Linux quando Go atingir critério de produção.

**Inventário de software em escala** → milhares de pacotes podem demorar.
*Mitigação*: timeout configurável (default 180s do Perl); coleta concorrente não ajuda software (único coletor), mas timeout alto evita cancelamento prematuro.

## Resolved Questions

| Questão | Decisão |
|---|---|
| Nome do binário | `fusioninventory-agent` (substitui o pacote Perl de mesmo nome) |
| Nome do módulo/repositório Go | `go-fusioninventory-agent` |
| Protocolo padrão em `--server` | GLPI 10+ nativo (CONTACT + JSON); XML/PROLOG só em fallback |
| PROLOG no v1 | Sim — apenas no fallback legado (plugin FusionInventory/OCS) |
| Servidor alvo v1 | GLPI 10+ com inventário nativo habilitado |
| Daemon no v1 | `--daemon` com loop + SIGTERM; alternativa via systemd timer |
| Compatibilidade de inventário | JSON aceito pelo GLPI 10+; XML semântico para legado/local |
| Device ID | Formato Perl `{hostname}-{timestamp}`, não UUID |
| Agent ID | UUID v4 separado, header `GLPI-Agent-ID` |
| Timeout default | 180s (do `agent.cfg` Perl) |
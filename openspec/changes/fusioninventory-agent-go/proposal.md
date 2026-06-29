## Why

O FusionInventory Agent em Perl exige um runtime completo com dependências CPAN para rodar, tornando instalação e empacotamento complexos. Migrar o núcleo para Go elimina essa dependência, produzindo um binário estático auto-suficiente com startup mais rápido, coleta concorrente nativa e distribuição trivial via pacotes `.deb`/`.rpm` ou binário único.

O alvo de servidor é **GLPI 10+** com inventário nativo (`/front/inventory.php`). O protocolo legado XML/PROLOG (plugin FusionInventory/OCS) permanece apenas como fallback automático para instalações antigas.

## What Changes

- **Novo projeto** na raiz do repositório: implementação Go do agente, iniciando com suporte exclusivo a Linux x86_64
- **Protocolo primário GLPI 10+**: fluxo **CONTACT** + inventário **JSON** + header `GLPI-Agent-ID` (UUID) no endpoint `/front/inventory.php` — alinhado ao GLPI Agent 1.19 (ver `research-glpi-agent.md`)
- **Protocolo legado (fallback)**: XML/PROLOG para servidores com plugin FusionInventory/OCS; detecção automática nativo vs legado
- **Paridade v1 (núcleo Linux)**: coletores equivalentes aos módulos Perl de inventário local mais usados em desktops e servidores Linux básicos — não paridade total com os 367 módulos Perl
- **Configuração compatível**: lê o mesmo formato `agent.cfg` do agente Perl, incluindo chaves críticas de produção (`tag`, `lazy`, SSL, `include`)
- **Modo de execução**: suporte a `--local` (saída em arquivo XML), `--server` (envio HTTP para GLPI 10+ nativo por padrão)
- **Monorepo**: o novo agente Go vive na raiz do repositório; os projetos de referência em Perl ficam em `base/` (`base/perl/` = FusionInventory legado, `base/glpi-agent/` = GLPI Agent)
- O agente Perl existente **não é modificado** — os dois coexistem durante a transição

## Capabilities

### New Capabilities

- `agent-core`: motor principal — configuração, logger, daemon, device ID persistente compatível com Perl, agentid UUID, scheduling (`lazy`, `force`, resposta do servidor)
- `inventory-collector`: engine de coleta com interface `Collector` plugável e execução concorrente via goroutines
- `linux-hardware`: coleta de hardware Linux — CPU, RAM, BIOS/DMI, discos, LVM, USB
- `linux-network`: coleta de interfaces de rede, IPs, MACs, gateway padrão
- `linux-software`: inventário de pacotes instalados (dpkg, rpm, pacman)
- `linux-os`: informações do SO — distro, kernel, uptime, hostname, timezone, usuários locais/grupos/logados, processos
- `glpi-native-protocol`: **protocolo primário** — agentid UUID, CONTACT, inventário JSON, headers/compressão, detecção nativo vs legado (GLPI 10+)
- `glpi-transport`: **fallback legado** — serialização XML, PROLOG e envio ao plugin FusionInventory/OCS; também usado em `--local`

### Deferred to v2 (documented, not in v1 scope)

- GPU/vídeo, monitores, impressoras, PCI, IPMI, RAID controllers (Megacli/Adaptec/etc.)
- Software: Snap, Flatpak, Gentoo, Slackware, Nix
- Rede avançada: DockerMacvlan, FibreChannel
- Firewall, baterias, remote management, environment variables
- Saída HTML (`html = 1`), `scan-homedirs`, `scan-profiles`, `additional-content`
- OAuth2 GLPI 11 (`oauth-client-id`/`oauth-client-secret`) — P3
- mTLS cliente e `ssl-fingerprint` — P2

### Modified Capabilities

<!-- Nenhuma — o projeto Perl existente não é alterado -->

## Impact

- **Estrutura monorepo**: o novo agente Go ocupa a raiz; `base/` guarda os projetos de referência em Perl (`base/perl/`, `base/glpi-agent/`)
- **Dependências Go**: `github.com/shirou/gopsutil/v3` para coleta + `encoding/json`/`encoding/xml` da stdlib + UUID para agentid
- **Sistemas suportados no v1**: Linux x86_64
- **Servidor alvo v1**: GLPI 10+ com inventário nativo habilitado
- **Fora de escopo v1**: NetDiscovery, NetInventory, Deploy, WakeOnLan, ESX, Windows, macOS, SNMP, interface web (porta 62354)
- **Critério de compatibilidade**:
  - **GLPI 10+**: inventário JSON aceito e computador criado/atualizado em homologação
  - **Coleta**: ≥90% dos campos do Perl em host de teste representativo (não bit-a-bit)
  - **Legado**: XML aceito pelo plugin FusionInventory quando fallback for acionado
- **Binário**: `fusioninventory-agent` (substitui o agente Perl de mesmo nome; o repositório/módulo Go continua `go-fusioninventory-agent`)
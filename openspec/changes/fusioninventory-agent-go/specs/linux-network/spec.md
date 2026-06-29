## ADDED Requirements

> **Nota de implementação**: coletores usam `gopsutil/net` como fonte primária. DockerMacvlan e FibreChannel estão fora de escopo v1.

### Requirement: Coleta de interfaces de rede
O agente SHALL coletar todas as interfaces de rede via `gopsutil/net`. Para cada interface SHALL coletar: nome, endereços IPv4 e IPv6, máscara de rede, endereço MAC, status (up/down), velocidade, tipo (ethernet, wifi, loopback, virtual).

#### Scenario: Interface ethernet com IP
- **WHEN** existe interface `eth0` com endereço IPv4
- **THEN** coleta DESCRIPTION, IPADDRESS, IPMASK, MACADDR, STATUS, SPEED, TYPE

#### Scenario: Interface loopback
- **WHEN** existe interface `lo`
- **THEN** é incluída no inventário com TYPE=loopback

#### Scenario: Interface sem IP configurado
- **WHEN** interface existe mas sem endereço IP
- **THEN** é coletada com IPADDRESS vazio

---

### Requirement: Coleta de rotas de rede
O agente SHALL coletar o gateway padrão. Fonte primária: gopsutil/net. Fallback: `/proc/net/route`.

#### Scenario: Gateway padrão presente
- **WHEN** existe uma rota default no sistema
- **THEN** o gateway é incluído no campo DEFAULTGATEWAY do inventário

---

### Requirement: Resolução de hostname e domínio
O agente SHALL coletar o hostname completo (FQDN), hostname curto e domínio DNS via `gopsutil/host` complementado por `/etc/hostname` e `/etc/resolv.conf`.

#### Scenario: Hostname configurado
- **WHEN** `/etc/hostname` contém um hostname válido
- **THEN** NAME, WORKGROUP (se aplicável) e DNS domain são incluídos no inventário
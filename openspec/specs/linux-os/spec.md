# linux-os Specification

## Purpose

Coleta de sistema operacional em Linux: informações do SO, usuários locais,
usuários logados, timezone e processos em execução.

## Requirements

> **Nota de implementação**: informações de SO via `gopsutil/host` + `/etc/os-release`. Usuários seguem o modelo Perl com três seções distintas (não apenas usuários logados).

### Requirement: Informações do sistema operacional
O agente SHALL coletar informações do SO via `gopsutil/host` e `/etc/os-release`. Campos SHALL incluir: nome da distro, versão, kernel version, kernel release, arquitetura, uptime, boot time.

#### Scenario: Ubuntu com /etc/os-release
- **WHEN** `/etc/os-release` existe com `NAME="Ubuntu"` e `VERSION_ID="22.04"`
- **THEN** o inventário inclui OSNAME, OSVERSION, KERNEL_VERSION, BOOT_TIME

#### Scenario: Sistema sem /etc/os-release
- **WHEN** `/etc/os-release` não existe
- **THEN** tenta `/etc/issue` e `uname` como fallback

### Requirement: Usuários locais do sistema
O agente SHALL coletar usuários locais via `/etc/passwd` na seção `LOCAL_USERS`. Campos SHALL incluir: LOGIN, ID (UID), NAME (gecos), HOME, SHELL. O agente SHALL coletar grupos via `/etc/group` na seção `LOCAL_GROUPS` com ID, NAME e MEMBER.

#### Scenario: Usuários e grupos locais
- **WHEN** `/etc/passwd` e `/etc/group` existem
- **THEN** coleta LOCAL_USERS com LOGIN, ID, NAME, HOME, SHELL e LOCAL_GROUPS com ID, NAME, MEMBER

#### Scenario: Categoria local_user desabilitada
- **WHEN** `no-category = local_user`
- **THEN** LOCAL_USERS é omitido do XML

### Requirement: Usuários logados
O agente SHALL coletar usuários atualmente logados via `who` na seção `USERS`. O agente SHALL coletar último usuário logado via `last` nos campos `LASTLOGGEDUSER` e `DATELASTLOGGEDUSER` do hardware.

#### Scenario: Usuário logado
- **WHEN** `who` retorna sessões ativas
- **THEN** seção USERS contém LOGIN para cada usuário logado

#### Scenario: Último login
- **WHEN** `last` retorna histórico de login com data/hora
- **THEN** LASTLOGGEDUSER e DATELASTLOGGEDUSER são preenchidos no hardware com usuário e timestamp do último login real (excluindo pseudo-registros como `reboot`)

### Requirement: Timezone do sistema
O agente SHALL detectar o timezone via `/etc/timezone`, `/etc/localtime` (symlink), ou `timedatectl`. O nome IANA do timezone (ex: `America/Sao_Paulo`) SHALL ser incluído no inventário.

#### Scenario: Timezone via /etc/timezone
- **WHEN** `/etc/timezone` contém `America/Sao_Paulo`
- **THEN** TIMEZONE é coletado com o valor correto

### Requirement: Processos em execução
O agente SHALL coletar a lista de processos via `gopsutil/process` quando `scan-processes = 1` estiver configurado. Por padrão, coleta de processos SHALL estar desabilitada.

#### Scenario: Coleta de processos desabilitada (padrão)
- **WHEN** `scan-processes` não está configurado ou é `0`
- **THEN** a seção PROCESS é omitida do inventário XML

#### Scenario: Coleta de processos habilitada
- **WHEN** `scan-processes = 1`
- **THEN** coleta PID, NAME, USER, MEM, CPU para cada processo

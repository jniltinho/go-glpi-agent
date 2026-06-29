## ADDED Requirements

### Requirement: Inventário de pacotes Debian/Ubuntu (dpkg)
O agente SHALL coletar pacotes instalados via `dpkg-query` em sistemas Debian-based. Campos SHALL incluir: nome do pacote, versão, arquitetura, tamanho instalado, seção.

#### Scenario: Sistema Debian com pacotes instalados
- **WHEN** `dpkg-query` está disponível
- **THEN** retorna lista de pacotes com NAME, VERSION, ARCH para cada pacote instalado

---

### Requirement: Inventário de pacotes RPM (RedHat/CentOS/Fedora)
O agente SHALL coletar pacotes instalados via `rpm -qa` em sistemas RPM-based. Campos SHALL incluir: nome, versão, release, arquitetura, tamanho, data de instalação.

#### Scenario: Sistema RPM com pacotes
- **WHEN** `rpm` está disponível
- **THEN** retorna lista de pacotes com NAME, VERSION, FILESIZE, INSTALLDATE

---

### Requirement: Inventário de pacotes Pacman (Arch Linux)
O agente SHALL coletar pacotes via `pacman -Q` em sistemas Arch-based. Campos SHALL incluir: nome e versão.

#### Scenario: Sistema Arch com pacman
- **WHEN** `pacman` está disponível
- **THEN** retorna lista de pacotes com NAME e VERSION

---

### Requirement: Detecção automática do gerenciador de pacotes
O agente SHALL detectar automaticamente qual gerenciador de pacotes usar, sem configuração manual. A detecção SHALL ser baseada na presença dos binários (`dpkg-query`, `rpm`, `pacman`). Em sistemas com múltiplos gerenciadores, SHALL coletar de todos.

#### Scenario: Sistema com apenas dpkg
- **WHEN** apenas `dpkg-query` está disponível
- **THEN** coleta somente via dpkg, sem erros sobre rpm ausente

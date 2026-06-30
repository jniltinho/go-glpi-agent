## ADDED Requirements

> **Nota de implementação**: coletores usam `gopsutil/v3` como fonte primária. Parsing de `/proc/`, `/sys/` e ferramentas externas (`dmidecode`, `lsblk`) é fallback para campos que gopsutil não cobre. GPU, monitores, RAID controllers e PCI estão fora de escopo v1 (ver matriz de paridade em `design.md`).

### Requirement: Coleta de CPU
O agente SHALL coletar informações de CPU via `gopsutil/cpu`. Os campos coletados SHALL incluir: modelo, fabricante, frequência, número de núcleos físicos, número de threads, arquitetura.

#### Scenario: CPU Intel multi-core
- **WHEN** o sistema tem CPU Intel com múltiplos cores
- **THEN** o inventário inclui NAME, MANUFACTURER, SPEED, CORE, THREAD, ARCH

#### Scenario: Manufacturer e contagem de threads
- **WHEN** o `/proc/cpuinfo` reporta vendor `GenuineIntel` com 7 cores e 14 processadores lógicos
- **THEN** MANUFACTURER é normalizado para `Intel`, CORE=7 e THREAD=14 (total de threads no socket)

---

### Requirement: Coleta de memória RAM
O agente SHALL coletar memória total e disponível via `gopsutil/mem`. Quando `dmidecode` estiver disponível, SHALL também coletar slots de memória com fabricante, tipo (DDR4, etc.), capacidade por módulo e velocidade.

#### Scenario: Apenas gopsutil disponível
- **WHEN** `dmidecode` não está instalado
- **THEN** coleta TOTAL e FREE de memória via gopsutil, sem detalhes de slots

#### Scenario: dmidecode disponível
- **WHEN** `dmidecode` está instalado e acessível (geralmente requer root)
- **THEN** coleta detalhes de cada slot de memória físico

---

### Requirement: Coleta de BIOS e DMI
O agente SHALL coletar informações de BIOS via `/sys/class/dmi/id/` e, quando disponível, via `dmidecode`. Campos SHALL incluir: fabricante do sistema, modelo, número de série, UUID do sistema, fabricante e versão do BIOS, data do BIOS.

#### Scenario: /sys/class/dmi disponível
- **WHEN** `/sys/class/dmi/id/` existe (sistemas com firmware UEFI/ACPI)
- **THEN** coleta SMANUFACTURER, SMODEL, SSN, UUID, BMANUFACTURER, BVERSION, BDATE

#### Scenario: Sistema sem DMI (ex: VM sem passthrough)
- **WHEN** `/sys/class/dmi/id/` não existe ou está vazio
- **THEN** campos DMI são omitidos do inventário sem erro

---

### Requirement: Coleta de discos e partições
O agente SHALL coletar discos físicos e partições via `gopsutil/disk` complementado por `lsblk --json` e `/sys/block/` para detalhes físicos. Campos SHALL incluir: nome do dispositivo, tipo (HDD/SSD/NVMe), tamanho, fabricante, modelo, número de série. Partições SHALL incluir: ponto de montagem, sistema de arquivos, tamanho total e livre.

#### Scenario: Disco NVMe
- **WHEN** existe um dispositivo `/dev/nvme0n1`
- **THEN** é coletado com TYPE=NVMe, tamanho e modelo

#### Scenario: Partições montadas
- **WHEN** existem partições com ponto de montagem
- **THEN** coleta VOLUMN, FILESYSTEM, TOTAL, FREE, TYPE para cada partição

---

### Requirement: Coleta de dispositivos USB
O agente SHALL coletar dispositivos USB via `/sys/bus/usb/devices/`. Campos SHALL incluir: VendorID, ProductID, nome do fabricante, nome do produto, classe do dispositivo.

#### Scenario: Dispositivo USB conectado
- **WHEN** existe um dispositivo USB em `/sys/bus/usb/devices/`
- **THEN** coleta VENDORID, PRODUCTID, NAME, CLASS para cada dispositivo não-hub

---

### Requirement: Coleta de LVM
O agente SHALL coletar volumes LVM via `lvs` e `vgs` quando disponíveis. Campos SHALL incluir: nome do volume group, nome do logical volume, tamanho, atributos.

#### Scenario: LVM não instalado
- **WHEN** `lvs` não está no PATH
- **THEN** nenhum dado LVM é coletado e não há erro

#### Scenario: LVM com volumes ativos
- **WHEN** `lvs` retorna volumes
- **THEN** coleta VG, LV, SIZE para cada volume lógico
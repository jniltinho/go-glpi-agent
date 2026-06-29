# go-fusioninventory-agent

Reimplementação em Go do FusionInventory Agent, focada em **Linux**. Produz um
binário único estático, sem dependências de runtime, compatível com o protocolo
OCS/FusionInventory do servidor GLPI.

Convive com o agente Perl legado (em `base/perl/`) durante a transição.

## Status

v1 — somente Linux amd64. Coleta de hardware e software local.
Os agentes Go e Perl produzem inventário equivalente para os coletores abaixo.

## Build

```sh
make build          # binário local
make build-all      # binário linux/amd64 em dist/
make test           # testes unitários
```

## Uso

CLI em subcomandos (Cobra):

```sh
# saída em arquivo XML local
./fusioninventory-agent run --local /tmp/inventory

# envio para o GLPI 10+ (protocolo nativo JSON; fallback XML/PROLOG automático)
./fusioninventory-agent run --server http://glpi/front/inventory.php

# usa o mesmo agent.cfg do agente Perl
./fusioninventory-agent run --conf-file /etc/fusioninventory/agent.cfg

# modo daemon (ciclos periódicos)
./fusioninventory-agent daemon

# versão
./fusioninventory-agent version
```

Flags globais: `--server`, `--local`, `--conf-file`, `--debug`, `--force`,
`--no-category`.

## Coletores (v1)

| Categoria | Fonte | Status |
|---|---|---|
| CPU | gopsutil/cpu | ✅ |
| Memória + slots | gopsutil/mem, dmidecode | ✅ |
| BIOS/DMI | /sys/class/dmi, dmidecode | ✅ |
| Discos físicos | lsblk | ✅ |
| Sistemas de arquivos | gopsutil/disk | ✅ |
| LVM | lvs | ✅ |
| USB | /sys/bus/usb | ✅ |
| Rede | gopsutil/net, /proc/net/route | ✅ |
| SO/distro | gopsutil/host, /etc/os-release | ✅ |
| Hostname/domínio | gopsutil/host, /etc | ✅ |
| Timezone | /etc/timezone, /etc/localtime | ✅ |
| Usuários/grupos/logados | /etc/passwd, /etc/group, who, last | ✅ |
| Processos (`scan-processes=1`) | gopsutil/process | ✅ |
| Software dpkg/rpm/pacman | dpkg-query, rpm, pacman | ✅ |

## Lacunas v1 vs agente Perl (planejado para v2)

- GPU, monitores (EDID), impressoras, PCI controllers
- IPMI, RAID controllers (Megacli etc.) — relevante em servidores/datacenter
- Software Snap, Flatpak, Nix, Gentoo, Slackware
- Firewall, baterias, domínios, chaves SSH, variáveis de ambiente
- Tarefas NetDiscovery, NetInventory, Deploy, WakeOnLan, ESX
- Windows, macOS, BSD, AIX, Solaris

Para esses casos, use o agente Perl em `base/perl/`.

## Testes de integração

`test/` contém a infraestrutura para validar contra um GLPI real e em outras
distros, para rodar **em outra máquina** (não na de desenvolvimento):

- `test/glpi/` — GLPI 10 + MariaDB via `docker compose`
- `test/vagrant/` — VMs Rocky Linux 9 e Debian 12 (VirtualBox ou libvirt)

Veja `test/README.md` para o passo a passo.

## Notas

- Alguns campos (slots de memória via `dmidecode`, seriais de disco) exigem
  privilégios de root.
- O device ID segue o formato do Perl (`{hostname}-{timestamp}`) e importa o
  `FusionInventory-Agent.dump` existente na primeira execução, evitando que o
  GLPI trate a máquina como novo ativo.

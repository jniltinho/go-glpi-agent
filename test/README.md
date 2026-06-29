# Testes de integração — go-fusioninventory-agent

Infraestrutura para validar o agente Go contra o agente Perl e contra um
servidor GLPI real. **Nada disso roda na máquina de desenvolvimento** — é para
ser executado em outra máquina/host de testes quando for conveniente.

```
test/
├── glpi/                 # GLPI 10 + MariaDB via docker-compose
│   ├── docker-compose.yml
│   └── .env.example
├── vagrant/              # VMs Rocky Linux 9 e Debian 12
│   ├── Vagrantfile
│   └── provision.sh
└── README.md             # este arquivo
```

Fluxo geral:

```
   ┌─────────────────────────────┐
   │  Host de testes (com Docker  │
   │  + Vagrant/VirtualBox/KVM)   │
   │                             │
   │  ┌───────────────────────┐  │
   │  │ GLPI (docker compose) │  │  http://HOST:8080
   │  │  glpi/glpi + mariadb  │◄─┼──────────────┐
   │  └───────────────────────┘  │              │ POST /front/inventory.php
   │                             │              │
   │  ┌──────────┐  ┌──────────┐ │              │
   │  │ Rocky 9  │  │ Debian 12│ │──────────────┘
   │  │  (vagrant)  │ (vagrant)│ │  go-fusioninventory-agent
   │  └──────────┘  └──────────┘ │
   └─────────────────────────────┘
```

---

## 1. Subir o GLPI (Docker)

Requer Docker + plugin `docker compose`.

```sh
cd test/glpi
cp .env.example .env          # ajuste senhas se quiser
docker compose up -d
```

- Acesse **http://localhost:8080** (ou o IP do host de testes).
- Login inicial padrão do GLPI: **glpi / glpi** (troque a senha no primeiro acesso).
- A imagem `glpi/glpi:10.0` auto-instala o schema no MariaDB no primeiro start
  (aguarde ~1 min; veja `docker compose logs -f glpi`).

### Habilitar o inventário nativo

GLPI 10 traz inventário nativo (substitui o plugin FusionInventory). Pela UI:

> **Configurar → Geral → Inventário → "Habilitar inventário" = Sim**

O endpoint passa a aceitar POST em **`/front/inventory.php`**, que aceita o
formato XML FusionInventory que o agente Go gera.

> Segurança: em produção habilite autenticação básica HTTP no endpoint. Para
> testes locais pode deixar aberto.

### Apontar o agente para o GLPI

```sh
fusioninventory-agent \
  --server http://HOST_DO_GLPI:8080/front/inventory.php \
  --debug
```

Depois confira em **Ativos → Computadores** no GLPI se a máquina apareceu.

> **Nota sobre PROLOG (a validar nesta fase):** o agente Go faz o fluxo PROLOG
> antes do inventário (design D9), que é o protocolo do *plugin FusionInventory*
> legado. O **inventário nativo do GLPI 10** (`/front/inventory.php`) pode
> aceitar o POST do inventário direto, sem PROLOG. Se o GLPI rejeitar o PROLOG,
> os caminhos são: (a) adicionar uma flag `--no-prolog` que pula direto para o
> POST do inventário, ou (b) usar o endpoint do plugin FusionInventory se ele
> estiver instalado. Confirmar qual o GLPI de testes espera é um dos objetivos
> desta fase.

---

## 2. Subir VMs de teste (Vagrant)

Requer Vagrant + VirtualBox **ou** libvirt/KVM. Boxes usados:
`generic/rocky9` e `generic/debian12` (suportam ambos os provedores).

### Passo 1 — gerar o binário no host

```sh
make build-all                # gera dist/fusioninventory-agent
```

O Vagrant monta `dist/` em `/opt/gfi` dentro das VMs.

### Passo 2 — subir e provisionar

```sh
cd test/vagrant
vagrant up                    # sobe rocky9 e debian12
# ou individualmente:
vagrant up rocky9
vagrant up debian12
```

O `provision.sh` (executado automaticamente) faz em cada VM:
1. instala dependências de coleta: `dmidecode`, `util-linux`/`lsblk`, `lvm2`, `usbutils`
2. tenta instalar o `fusioninventory-agent` Perl como referência
3. roda o agente Go em modo `--local`
4. roda o Perl (se disponível) e **compara a contagem de seções** dos dois XMLs

Saída esperada (exemplo):

```
================ Comparação de seções (debian) ================
SEÇÃO              GO     PERL
HARDWARE           1      1
BIOS               1      1
OPERATINGSYSTEM    1      1
CPUS               1      1
...
===============================================================
```

> Em VMs reais (VirtualBox/KVM) a seção **BIOS** é preenchida via
> `/sys/class/dmi/id/` — diferente do WSL2 da máquina de dev, onde DMI não
> existe e o BIOS fica vazio.

### Passo 3 — validar contra o GLPI a partir da VM

Dentro da VM (`vagrant ssh rocky9`):

```sh
sudo /opt/gfi/fusioninventory-agent \
  --server http://IP_DO_HOST:8080/front/inventory.php --debug
```

### Limpeza

```sh
vagrant destroy -f
cd ../glpi && docker compose down -v   # remove também os volumes
```

---

## 3. Matriz de validação alvo

| Distro | dmidecode/BIOS | dpkg/rpm | Perl ref | GLPI aceita |
|---|---|---|---|---|
| Ubuntu 24.04 (dev/WSL) | sem DMI | dpkg ✅ | ✅ comparado | a validar |
| Debian 12 (vagrant) | ✅ | dpkg | a validar | a validar |
| Rocky Linux 9 (vagrant) | ✅ | rpm | a validar | a validar |

Itens a confirmar em cada distro:
- [ ] binário roda sem libs externas (estático)
- [ ] software via gestor correto (dpkg no Debian, rpm no Rocky)
- [ ] BIOS/DMI preenchido (root)
- [ ] discos físicos via `lsblk`
- [ ] GLPI aceita o POST e cria/atualiza o computador
- [ ] device ID estável entre execuções (e import do `.dump` do Perl)

> Estas tarefas correspondem a **2.1** (golden multi-host) e **10.2**
> (validação GLPI) do plano em `openspec/changes/fusioninventory-agent-go/`.

## Referências

- [GLPI no Docker (oficial)](https://www.glpi-project.org/en/run-glpi-with-docker/)
- [Imagem oficial glpi/glpi](https://hub.docker.com/r/glpi/glpi)
- [GLPI on Docker — Help Center](https://help.glpi-project.org/tutorials/procedures/running_glpi_on_docker)
- [Vagrant + VirtualBox/KVM no Rocky Linux 9](https://computingforgeeks.com/using-vagrant-with-virtualbox-kvm-on-rocky/)
- [Vagrant + VirtualBox/KVM no Debian 12](https://computingforgeeks.com/using-vagrant-with-virtualbox-and-kvm-on-debian/)

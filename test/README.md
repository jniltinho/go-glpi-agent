# Integration tests — go-glpi-agent

Infrastructure to validate the Go agent against a real **GLPI 10** and across
many Linux distributions. **None of this runs on the dev machine** — it is meant
for a separate test host.

```
test/
├── glpi/                 # GLPI 10 + MariaDB via docker compose
│   ├── docker-compose.yml
│   └── .env.example
├── vagrant/              # multi-distro VirtualBox matrix
│   ├── Vagrantfile
│   └── provision.sh
└── README.md             # this file
```

## Quickstart

```sh
# 1) build the linux/amd64 binary (copied into each VM)
make build-all
# optional: official glpi-agent AppImage used as a reference inside the VMs
make fetch-glpi-agent

# 2) start GLPI 10 and enable inventory (see below)
cd test/glpi && cp .env.example .env && docker compose up -d

# 3) bring up the VMs (provisioning runs the agent and sends to GLPI)
cd ../vagrant && vagrant up
```

The `Vagrantfile` and `provision.sh` are versioned; VM state (`.vagrant/`) and
the GLPI `.env` are gitignored and recreated locally.

## 1. GLPI (Docker)

```sh
cd test/glpi
cp .env.example .env          # adjust passwords if you want
docker compose up -d
```

- Open **http://localhost:8080** (default login: `glpi` / `glpi`).
- The `glpi/glpi:10.0` image auto-installs the schema on first start (~1 min).

### Enable native inventory

The native endpoint returns **403** until inventory is enabled. Via the UI:
**Setup → General → Inventory → "Enable inventory" = Yes**. Or from the DB
(faster for automation), then clear the cache:

```sh
docker compose exec -T db mysql -uglpi -pglpi glpi -e \
  "UPDATE glpi_configs SET value='1' WHERE context='inventory' AND name='enabled_inventory';"
docker compose exec -T glpi sh -lc 'cd /var/www/glpi && php bin/console cache:clear'
```

The agent then POSTs the native JSON inventory to `/front/inventory.php`
(validated against GLPI's `inventory.schema.json`); the legacy XML/PROLOG path is
an automatic fallback.

## 2. Distro matrix (Vagrant)

Requires Vagrant + VirtualBox. `Vagrantfile` defines the matrix
(`families`: `debian`=apt, `rhel`=dnf, `suse`=zypper, `arch`=pacman):

Rocky 9, RHEL 8/9, CentOS Stream 10, AlmaLinux 8/9, Oracle Linux 8/9, Fedora 42,
Debian 12/13, Ubuntu 24.04/26.04, Pop!_OS 20.04, openSUSE Leap 15, Arch Linux.

```sh
cd test/vagrant
vagrant up                # all VMs (or: vagrant up alma9 fedora42 ...)
vagrant provision         # re-run the agent on running VMs
vagrant destroy -f        # clean up
```

The Go binary and the glpi-agent AppImage are copied into each VM via a **file
provisioner** (SCP) — no VirtualBox guest additions required, so it works on
boxes that lack them (RHEL/CentOS). `provision.sh`:

1. installs collection deps (dmidecode, lsblk, lvm2, pciutils, usbutils);
2. runs the Go agent `run --local` and `run --server` (to GLPI at `10.0.2.2:8080`,
   the VirtualBox NAT gateway to the host);
3. installs the official **glpi-agent** (reference) and runs it the same way;
4. prints a per-section item-count comparison (Go vs glpi-agent).

Set `GLPI_URL` to override the server (default `http://10.0.2.2:8080/front/inventory.php`).

## 3. Windows (Vagrant)

Validates the Windows build (WMI + registry collectors) against the same GLPI 10
stack. Requires Vagrant + VirtualBox (or Hyper-V) and the
`gusztavvargadr/windows-server-2022-standard` box. Windows VMs are large (~10 GB)
and run as evaluation editions — keep this on a test host.

```sh
cd ../..                                  # repo root
make build-windows                        # produces dist/go-glpi-agent.exe
cd test/vagrant-windows
GLPI_SERVER=http://10.0.2.2/front/inventory.php vagrant up
vagrant provision                         # re-run the agent
vagrant destroy -f                        # clean up
```

The `.exe` and the `contrib/windows/` scripts are copied into the VM via a **file
provisioner** (WinRM). `provision.ps1`:

1. points `agent.cfg` at `GLPI_SERVER`;
2. runs `run --local` (XML sanity check) and `run` with `GFI_DUMP_JSON` set (so the
   native JSON can be validated offline against GLPI's `inventory.schema.json`);
3. runs `install.ps1`, which registers the hourly **Scheduled Task** (SYSTEM) and
   sends a native inventory — a `win-gfi-test` Computer asset then appears in GLPI.

Set `GLPI_SERVER` to override the target (default `http://10.0.2.2/front/inventory.php`,
the VirtualBox NAT gateway to the host).

## Notes

- On VirtualBox VMs the BIOS serial comes from the box image (often `0`, which
  the agent filters as junk); on real hardware the true serial is read.
- `centos/stream10` may have a transient NAT networking issue reaching the host.
- The Windows Scheduled Task runs as SYSTEM, so per-user `HKCU` software (other
  users' installs) is not enumerated in v1 — machine-wide software is complete.

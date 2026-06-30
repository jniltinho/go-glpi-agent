# go-glpi-agent on FreeBSD

Static `freebsd/amd64` binary — no runtime dependencies. This tarball contains:

| File | Purpose |
|---|---|
| `go-glpi-agent` | the binary |
| `agent.cfg` | configuration |
| `go_glpi_agent` | rc.d service script |

## Install

```sh
# 1) place the binary + config under /opt/go-glpi-agent
sudo mkdir -p /opt/go-glpi-agent/var
sudo install -m 0755 go-glpi-agent /opt/go-glpi-agent/go-glpi-agent
sudo cp -n agent.cfg /opt/go-glpi-agent/agent.cfg   # -n: keep existing config
sudo vi /opt/go-glpi-agent/agent.cfg                # set the `server` line

# 2a) run once now
sudo /opt/go-glpi-agent/go-glpi-agent run

# 2b) OR install the rc.d service (daemon mode, periodic cycles)
sudo install -m 0755 go_glpi_agent /usr/local/etc/rc.d/go_glpi_agent
sudo sysrc go_glpi_agent_enable=YES
sudo service go_glpi_agent start
```

## Scheduled run (cron) — the analog of a systemd timer

Instead of the daemon, an hourly one-shot via cron is often simpler:

```sh
echo '0 * * * * root /opt/go-glpi-agent/go-glpi-agent run --conf-file /opt/go-glpi-agent/agent.cfg' \
  | sudo tee /etc/cron.d/go-glpi-agent
```

## Collectors

OS/kernel, CPU, memory, BIOS/board/chassis/UUID (via `kenv smbios.*`), physical
disks (`geom`), filesystems (UFS/ZFS), network, USB (`usbconfig`), and installed
software (`pkg`). Some data needs no root; `pkg`/`geom` work as a normal user.

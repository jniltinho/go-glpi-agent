# go-glpi-agent on macOS

Static `darwin` binary — no runtime dependencies (unlike the official Perl agent,
which bundles a full Perl runtime). Ships as a `.pkg` (wrapped in a `.dmg`) for
both Intel (`x86_64`) and Apple Silicon (`arm64`).

## Install

1. Download `go-glpi-agent_<version>_<arch>.dmg` (or `.pkg`) for your architecture
   (`x86_64` for Intel, `arm64` for Apple Silicon — check with `uname -m`).
2. Open the `.dmg` and double-click the `.pkg`, **or** install from the terminal:

   ```sh
   sudo installer -pkg go-glpi-agent_<version>_<arch>.pkg -target /
   ```

The installer lays down:

| Path | Purpose |
|---|---|
| `/usr/local/go-glpi-agent/go-glpi-agent` | the binary |
| `/usr/local/go-glpi-agent/agent.cfg` | configuration |
| `/Library/LaunchDaemons/com.glpi.go-agent.plist` | scheduled run (every hour) |

The `postinstall` script loads the LaunchDaemon, so the agent starts running on
its periodic schedule immediately.

> The installer is **unsigned**. On a Mac that downloaded it via a browser,
> Gatekeeper may block it — right-click → Open, or push it via your MDM.

## Configure

Edit `/usr/local/go-glpi-agent/agent.cfg` and set the `server` line to your GLPI
inventory endpoint, then reload the daemon:

```sh
sudo launchctl bootout system /Library/LaunchDaemons/com.glpi.go-agent.plist
sudo launchctl bootstrap system /Library/LaunchDaemons/com.glpi.go-agent.plist
```

## Run once manually

```sh
sudo /usr/local/go-glpi-agent/go-glpi-agent run --server http://glpi/front/inventory.php
# or write the inventory locally as XML:
/usr/local/go-glpi-agent/go-glpi-agent run --local /tmp/inv
```

## Uninstall

```sh
sudo /usr/local/go-glpi-agent/uninstall.sh
```

(or download `uninstall.sh` from `contrib/macos/` if the file was not installed).

## Collectors

OS/kernel (Darwin, via `sw_vers`/`uname`), CPU (Intel `machdep.cpu.*` and Apple
Silicon `system_profiler` "Chip"), memory (+ slots), system identity — model,
serial, UUID, boot-ROM — via `system_profiler SPHardwareDataType` with an `ioreg`
fallback, physical disks (NVMe/SATA), filesystems, network, USB, and installed
applications (`system_profiler SPApplicationsDataType`).

The serial is resolved through `Serial Number` → `Serial Number (system)` →
`ioreg IOPlatformSerialNumber`, and falls back to the hardware UUID if no serial
is available, so a Mac is never reported to GLPI without a serial.

# Dockerized GLPI validation laboratory

The project-local lab is isolated from the parent repository's Go stack. It pins official GLPI and MariaDB images by version and multi-platform digest:

- GLPI 10.0.26 on host port 8180.
- GLPI 11.0.8 on host port 8181.
- MariaDB 11.4.12 for each independent database.

Start one version or the matrix:

```sh
./lab.sh start 10
./lab.sh start 11
./lab.sh start all
```

`start` waits up to 15 minutes, enables native inventory in the database, clears the active GLPI cache, extracts the container's exact `inventory.schema.json`, and records running image digests under `artifacts/`. Other commands are `wait`, `enable`, `schema`, `inspect`, `stop`, and `reset`; each accepts `10`, `11`, or `all`.

The Windows VM reaches the host through VirtualBox NAT at `10.0.2.2`, so its endpoints are:

```text
http://10.0.2.2:8180/front/inventory.php
http://10.0.2.2:8181/front/inventory.php
```

For Hyper-V, set `GLPI_SERVER` to a host address reachable from the selected virtual switch. Test connectivity with `Test-NetConnection HOST -Port PORT` before installing the MSI.

The default credentials are disposable lab values. Copy `.env.example` to `.env` to override ports or passwords. Do not expose these containers beyond a test host.

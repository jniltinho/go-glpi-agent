# freebsd-software Specification

## Purpose

Collect installed software on FreeBSD from the pkg package database for the
inventory's software section.

## Requirements

### Requirement: The agent SHALL collect installed packages via pkg

On FreeBSD the agent SHALL enumerate installed software using `pkg query`, reporting each
package's name, version, architecture/ABI, install size and one-line comment. When `pkg` is
absent (a base-only system with no packages), the collector SHALL degrade gracefully and add
no software rather than failing the cycle.

#### Scenario: Software list populated

- **WHEN** an inventory cycle runs on a FreeBSD host with packages installed
- **THEN** `SOFTWARES` contains one entry per package with `NAME`, `VERSION` and `FROM=pkg`

#### Scenario: pkg not bootstrapped

- **WHEN** `pkg` is not installed/bootstrapped on the host
- **THEN** the software collector adds nothing and the inventory cycle still completes

#### Scenario: Install size reported

- **WHEN** `pkg query` returns a flat size for a package
- **THEN** the entry's `FILESIZE` reflects that size in bytes

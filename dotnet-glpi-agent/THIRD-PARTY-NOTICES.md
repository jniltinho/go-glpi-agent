# Third-party notices

The project records all direct runtime, test, packaging, and build dependencies
in centrally managed project files. Release artifacts must include an SBOM and
the license notices applicable to the exact resolved dependency versions.

The Perl implementations under the parent repository's `base/` directory are
GPL-licensed behavioral references. No Perl source is included in this project
or its binary artifacts.

WiX Toolset is not yet an approved dependency. Its licensing decision is an
explicit release gate documented in `docs/product-decisions.md`.

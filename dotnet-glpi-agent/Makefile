DOTNET_ENV = DOTNET_CLI_HOME=/tmp/dotnet-cli NUGET_PACKAGES=/tmp/nuget-packages DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
CONFIGURATION ?= Release
VERSION ?= 0.1.0

.PHONY: restore format build test publish publish-validator package-windows release-artifacts prepare-vagrant ci

restore:
	$(DOTNET_ENV) dotnet restore DotnetGlpiAgent.sln

format:
	$(DOTNET_ENV) dotnet format DotnetGlpiAgent.sln --no-restore --verify-no-changes

build:
	$(DOTNET_ENV) dotnet build DotnetGlpiAgent.sln --configuration $(CONFIGURATION) --no-restore

test:
	$(DOTNET_ENV) dotnet test DotnetGlpiAgent.sln --configuration $(CONFIGURATION) --no-build --no-restore

publish:
	$(DOTNET_ENV) dotnet publish src/DotnetGlpiAgent.App/DotnetGlpiAgent.App.csproj --configuration $(CONFIGURATION) --runtime win-x64 --self-contained true --output artifacts/publish/win-x64

publish-validator:
	$(DOTNET_ENV) dotnet publish tools/GlpiSchemaValidator/GlpiSchemaValidator.csproj --configuration $(CONFIGURATION) --runtime win-x64 --self-contained true --output artifacts/tools/glpi-schema-validator

# WiX MSI build requires Windows. On Linux this target fails by design.
package-windows:
	$(DOTNET_ENV) dotnet build packaging/wix/DotnetGlpiAgent.Package.wixproj --configuration $(CONFIGURATION) -p:ProductVersion=$(VERSION)

release-artifacts:
	VERSION=$(VERSION) bash packaging/scripts/New-ReleaseArtifacts.sh

prepare-vagrant: publish publish-validator
	@echo "Stage MSI on a Windows packager with 'make package-windows', then copy"
	@echo "artifacts/msi/*.msi into test/vagrant-windows/artifacts/ before vagrant up."

ci: restore build test
	$(DOTNET_ENV) dotnet publish tools/GlpiSchemaValidator/GlpiSchemaValidator.csproj --configuration $(CONFIGURATION) --output artifacts/tools/glpi-schema-validator-host
	@test -f test/glpi/artifacts/glpi10/inventory.schema.json
	@test -f test/glpi/artifacts/glpi11/inventory.schema.json
	@echo "CI host checks complete (MSI build is windows-only)."

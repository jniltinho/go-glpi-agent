DOTNET_ENV = DOTNET_CLI_HOME=/tmp/dotnet-cli NUGET_PACKAGES=/tmp/nuget-packages DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
CONFIGURATION ?= Release
VERSION ?= 0.1.0

.PHONY: restore format build test publish publish-validator package-windows prepare-vagrant

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

package-windows:
	$(DOTNET_ENV) dotnet build packaging/wix/DotnetGlpiAgent.Package.wixproj --configuration $(CONFIGURATION) -p:ProductVersion=$(VERSION)

prepare-vagrant: publish publish-validator

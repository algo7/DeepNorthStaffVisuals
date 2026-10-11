# WeaponAuras: build, test, package.
#
# Installing is done in r2modman: `make package`, then "Import local mod" with the zip.
# Game DLLs come from MANAGED_DIR (default: the local client); CI passes the dedicated server's:
#   make test MANAGED_DIR=/path/to/valheim_server_Data/Managed

DOTNET_ROOT   ?= $(HOME)/.dotnet
DOTNET        ?= $(DOTNET_ROOT)/dotnet
VALHEIM_DIR   ?= $(HOME)/.local/share/Steam/steamapps/common/Valheim
MANAGED_DIR   ?= $(VALHEIM_DIR)/valheim_Data/Managed
# make log: your r2modman profile's BepInEx folder
BEPINEX_DIR   ?= $(HOME)/.config/r2modmanPlus-local/Valheim/profiles/Default/BepInEx
CONFIGURATION ?= Release

export DOTNET_ROOT
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_NOLOGO := 1

PROPS      = -c $(CONFIGURATION) "-p:ManagedDir=$(MANAGED_DIR)"

.PHONY: build test package clean log

## build: compile bin/$(CONFIGURATION)/WeaponAuras.dll
build:
	"$(DOTNET)" build WeaponAuras.csproj $(PROPS)

## test: run the out-of-game unit tests
test:
	"$(DOTNET)" run --project tests/WeaponAuras.Tests.csproj $(PROPS)

## package: build Algo7-WeaponAuras-<version>.zip here, for r2modman's "Import local mod"
package:
	"$(DOTNET)" tool restore
	"$(DOTNET)" build WeaponAuras.csproj $(PROPS) -t:Package

clean:
	rm -rf bin obj tests/bin tests/obj

## log: this plugin's lines from the last game session (BEPINEX_DIR: the r2modman profile's BepInEx)
log:
	@grep -n "WeaponAuras" "$(BEPINEX_DIR)/LogOutput.log" | tail -n 60

#!/usr/bin/env bash
# Provisiona a VM: instala dependências de coleta, instala o agente Perl
# (referência) e o binário Go, roda os dois e compara as seções do XML.
#
# Uso (chamado pelo Vagrant):  provision.sh {rocky|debian}
set -euo pipefail

DISTRO="${1:-unknown}"
BIN=/opt/gfi/fusioninventory-agent
OUTDIR=/tmp/gfi-test
mkdir -p "$OUTDIR"

echo "==> Distro: $DISTRO"

install_rocky() {
  dnf install -y dmidecode util-linux lvm2 pciutils usbutils which \
                 epel-release >/dev/null 2>&1 || true
  # agente Perl de referência (opcional; pode não existir no repo)
  dnf install -y fusioninventory-agent >/dev/null 2>&1 || \
    echo "   (fusioninventory-agent Perl indisponível no repo Rocky — segue só com o Go)"
}

install_debian() {
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -y >/dev/null 2>&1
  apt-get install -y dmidecode util-linux lvm2 pciutils usbutils >/dev/null 2>&1
  apt-get install -y fusioninventory-agent >/dev/null 2>&1 || \
    echo "   (fusioninventory-agent Perl indisponível — segue só com o Go)"
}

case "$DISTRO" in
  rocky)  install_rocky ;;
  debian) install_debian ;;
  *) echo "distro desconhecida: $DISTRO"; exit 1 ;;
esac

if [[ ! -x "$BIN" ]]; then
  echo "ERRO: binário não encontrado em $BIN"
  echo "      rode 'make build-all' no host antes de 'vagrant up'."
  exit 1
fi

echo "==> Versão do binário Go:"
"$BIN" version

echo "==> Rodando agente Go (run --local) como root..."
"$BIN" run --local "$OUTDIR/go" --debug 2>&1 | tail -20 || true
GO_XML=$(ls "$OUTDIR"/go/*.xml 2>/dev/null | head -1 || true)

echo "==> Rodando agente Perl (referência), se disponível..."
PERL_XML=""
if command -v fusioninventory-inventory >/dev/null 2>&1; then
  fusioninventory-inventory > "$OUTDIR/perl.xml" 2>/dev/null && PERL_XML="$OUTDIR/perl.xml"
fi

echo
echo "================ Comparação de seções ($DISTRO) ================"
printf "%-18s %-6s %-6s\n" "SEÇÃO" "GO" "PERL"
for sec in HARDWARE BIOS OPERATINGSYSTEM CPUS MEMORIES DRIVES STORAGES \
           NETWORKS SOFTWARES LOCAL_USERS LOCAL_GROUPS USERS; do
  g=0; p=0
  [[ -n "$GO_XML"   ]] && g=$(grep -c "<$sec>" "$GO_XML" 2>/dev/null || echo 0)
  [[ -n "$PERL_XML" ]] && p=$(grep -c "<$sec>" "$PERL_XML" 2>/dev/null || echo 0)
  printf "%-18s %-6s %-6s\n" "$sec" "$g" "$p"
done
echo "==============================================================="
echo "XML do Go gravado em (dentro da VM): $GO_XML"
[[ -n "$PERL_XML" ]] && echo "XML do Perl em: $PERL_XML" || echo "(sem agente Perl para comparar nesta distro)"
echo
echo "Para enviar ao GLPI a partir desta VM:"
echo "  $BIN run --server http://IP_DO_HOST:8080/front/inventory.php --debug"

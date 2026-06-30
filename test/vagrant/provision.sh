#!/usr/bin/env bash
# Provisiona a VM: instala dependências de coleta, instala o agente Perl
# (referência, quando disponível) e o binário Go, roda os dois e compara as
# seções do XML.
#
# Uso (chamado pelo Vagrant):  provision.sh {debian|rhel|suse} [nome-distro]
#   família = gerenciador de pacotes:  debian=apt  rhel=dnf  suse=zypper
set -euo pipefail

FAMILY="${1:-unknown}"
DISTRO="${2:-$FAMILY}"
# binário Go e AppImage do glpi-agent são copiados para /tmp pelo file provisioner
BIN=/tmp/fusioninventory-agent
APP=/tmp/glpi-agent.AppImage
OUTDIR=/tmp/gfi-test
mkdir -p "$OUTDIR"
chmod +x "$BIN" "$APP" 2>/dev/null || true

echo "==> Distro: $DISTRO (família: $FAMILY)"

# pacotes de coleta: dmidecode (BIOS/DMI), lsblk (util-linux), lvm2, pciutils,
# usbutils. O agente Perl é referência opcional para comparação.
install_debian() {
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -y >/dev/null 2>&1 || true
  apt-get install -y dmidecode util-linux lvm2 pciutils usbutils >/dev/null 2>&1 || true
  apt-get install -y fusioninventory-agent >/dev/null 2>&1 || \
    echo "   (agente Perl indisponível — segue só com o Go)"
}

install_rhel() {
  dnf install -y dmidecode util-linux lvm2 pciutils usbutils which >/dev/null 2>&1 || true
  dnf install -y epel-release >/dev/null 2>&1 || true
  dnf install -y fusioninventory-agent >/dev/null 2>&1 || \
    echo "   (agente Perl indisponível no repo — segue só com o Go)"
}

install_suse() {
  zypper --non-interactive --gpg-auto-import-keys refresh >/dev/null 2>&1 || true
  zypper --non-interactive install -y dmidecode util-linux lvm2 pciutils usbutils >/dev/null 2>&1 || true
  zypper --non-interactive install -y fusioninventory-agent >/dev/null 2>&1 || \
    echo "   (agente Perl indisponível no repo — segue só com o Go)"
}

install_arch() {
  pacman -Sy --noconfirm --needed dmidecode util-linux lvm2 pciutils usbutils >/dev/null 2>&1 || true
  # glpi-agent é instalado via AppImage (referência), igual às demais distros
}

case "$FAMILY" in
  debian) install_debian ;;
  rhel)   install_rhel ;;
  suse)   install_suse ;;
  arch)   install_arch ;;
  *) echo "família desconhecida: $FAMILY (use debian|rhel|suse|arch)"; exit 1 ;;
esac

if [[ ! -x "$BIN" ]]; then
  echo "ERRO: binário não encontrado em $BIN"
  echo "      rode 'make build-all' no host antes de 'vagrant up'."
  exit 1
fi

# GLPI server: por padrão o gateway NAT do VirtualBox (10.0.2.2) aponta para o
# host, onde roda o docker do GLPI na porta 8080. Sobrescreva com GLPI_URL.
GLPI_URL="${GLPI_URL:-http://10.0.2.2:8080/front/inventory.php}"

echo "==> Versão do binário Go:"
"$BIN" version

echo "==> Go agent: inventário local..."
"$BIN" run --local "$OUTDIR/go" 2>&1 | tail -5 || true
GO_XML=$(ls "$OUTDIR"/go/*.xml 2>/dev/null | head -1 || true)

echo "==> Go agent: enviando ao GLPI ($GLPI_URL)..."
"$BIN" run --server "$GLPI_URL" --debug 2>&1 | grep -iE "native|sent|status|error" | tail -3 || true

# --- Agente de referência: glpi-agent oficial (instalado a partir do AppImage
# montado em /opt/gfi). Roda local e também envia ao GLPI para comparação web.
REF_OUT=""
if [[ -x "$APP" ]]; then
  # o instalador exige um target (--local/--server); --runnow roda o inventário
  # logo após instalar, gravando o XML de referência em $OUTDIR/glpi.
  echo "==> Instalando glpi-agent (referência) via AppImage..."
  APPIMAGE_EXTRACT_AND_RUN=1 "$APP" --install --no-service --local="$OUTDIR/glpi" >/dev/null 2>&1 || true
  # o instalador grava em /usr/local/bin, que pode não estar no PATH do
  # provisioner (RHEL/SUSE) — use o caminho absoluto. --force/--no-fork garantem
  # que o inventário roda de forma síncrona (o guard de agendamento pularia).
  GA=$(command -v glpi-agent || echo /usr/local/bin/glpi-agent)
  if [[ -x "$GA" ]]; then
    echo "==> glpi-agent: inventário local + envio ao GLPI..."
    "$GA" --local="$OUTDIR/glpi" --force --no-fork >/dev/null 2>&1 || true
    "$GA" --server="$GLPI_URL" --force --no-fork >/dev/null 2>&1 || true
    REF_OUT=$(ls "$OUTDIR"/glpi/*.xml "$OUTDIR"/glpi/*.json 2>/dev/null | head -1 || true)
  else
    echo "   (glpi-agent não ficou disponível após instalar)"
  fi
else
  echo "   (AppImage do glpi-agent ausente em $APP — baixe com 'make fetch-glpi-agent')"
fi

# count_sec conta itens de uma seção: XML conta <SEC>; JSON usa o tamanho do
# array em content[sec] (via python3, com fallback para presença).
count_sec() {
  local f="$1" up="$2" low="$3"
  [[ -z "$f" || ! -f "$f" ]] && { echo 0; return; }
  case "$f" in
    *.json)
      python3 -c "import json,sys;d=json.load(open(sys.argv[1])).get('content',{});v=d.get(sys.argv[2],[]);print(len(v) if isinstance(v,list) else (1 if v else 0))" "$f" "$low" 2>/dev/null \
        || grep -c "\"$low\"" "$f" 2>/dev/null || echo 0 ;;
    *) grep -c "<$up>" "$f" 2>/dev/null || echo 0 ;;
  esac
}

echo
echo "============ Comparação de seções ($DISTRO): Go vs glpi-agent ============"
printf "%-18s %-6s %-9s\n" "SEÇÃO" "GO" "GLPI-AGT"
for pair in HARDWARE:hardware BIOS:bios OPERATINGSYSTEM:operatingsystem CPUS:cpus \
            MEMORIES:memories DRIVES:drives STORAGES:storages NETWORKS:networks \
            SOFTWARES:softwares LOCAL_USERS:local_users LOCAL_GROUPS:local_groups USERS:users; do
  up="${pair%%:*}"; low="${pair##*:}"
  printf "%-18s %-6s %-9s\n" "$up" "$(count_sec "$GO_XML" "$up" "$low")" "$(count_sec "$REF_OUT" "$up" "$low")"
done
echo "========================================================================"
echo "Go XML:         ${GO_XML:-(nenhum)}"
echo "glpi-agent out: ${REF_OUT:-(não instalado/sem saída)}"
echo "Ambos enviados ao GLPI em: $GLPI_URL"

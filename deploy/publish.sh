#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
DEST_ROOT="/opt/photoncore"
RESTART=false

usage() {
  cat <<USAGE
Usage: $(basename "$0") [--root PATH] [--restart]

Options:
  --root PATH   Destination root directory (default: /opt/photoncore)
  --restart     Restart PhotonCore services after deployment
  --help        Show this help message
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --root)
      [[ $# -ge 2 ]] || { echo "--root requires a path" >&2; exit 1; }
      DEST_ROOT="$2"
      shift 2
      ;;
    --restart)
      RESTART=true
      shift
      ;;
    --help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "${WORK_DIR}"' EXIT

ensure_user() {
  local service_name="$1"
  local user="photoncore-${service_name}"
  if ! id -u "$user" >/dev/null 2>&1; then
    sudo useradd --system --home "${DEST_ROOT}/${service_name}" --no-create-home \
      --shell /usr/sbin/nologin "$user"
  fi
}

publish_and_stage() {
  local project="$1"
  local service_name="$2"
  local output_dir="${WORK_DIR}/${service_name}"
  dotnet publish "${REPO_ROOT}/src/${project}/${project}.csproj" -c Release -o "$output_dir"
  ensure_user "$service_name"
  local dest="${DEST_ROOT}/${service_name}"
  sudo rm -rf "$dest"
  sudo mkdir -p "$dest"
  sudo cp -a "$output_dir/." "$dest/"
  sudo chown -R "photoncore-${service_name}:photoncore-${service_name}" "$dest"
}

publish_and_stage "PSO.Login" "login"
publish_and_stage "PSO.Ship" "ship"
publish_and_stage "PSO.AdminApi" "admin"

sudo systemctl daemon-reload

if [[ "$RESTART" == true ]]; then
  sudo systemctl restart photoncore-login.service photoncore-ship.service photoncore-admin.service
fi

echo "Deployment complete."

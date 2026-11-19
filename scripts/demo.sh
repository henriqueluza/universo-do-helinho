#!/usr/bin/env bash
# Demonstração local: sobe o PostgreSQL, aplica migração + seed e inicia o sistema.
# Uso: scripts/demo.sh   (HELINHO_SEED_PASSWORD opcional; se ausente, usa .demo/seed-password ou gera)
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

die() { printf 'ERRO: %s\n' "$*" >&2; exit 1; }
rand() { LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c "$1" || true; }

command -v docker >/dev/null 2>&1 || die "Docker não encontrado. Instale em https://docs.docker.com/get-docker/"
docker info >/dev/null 2>&1 || die "Docker está instalado, mas o daemon não responde. Inicie o Docker."
docker compose version >/dev/null 2>&1 || die "Plugin 'docker compose' não encontrado."
command -v dotnet >/dev/null 2>&1 || die ".NET SDK não encontrado. Instale o .NET 10 em https://dot.net"
dotnet --list-sdks | grep -q '^10\.' || die "SDK .NET 10 não encontrado (veja global.json)."

# Senha do banco local: gerada uma vez e guardada fora do git, para o volume continuar válido entre execuções.
state_dir=".demo"
db_pass_file="$state_dir/db-password"
mkdir -p "$state_dir"
chmod 700 "$state_dir"
if [[ ! -s "$db_pass_file" ]]; then
  (umask 077; rand 32 >"$db_pass_file")
fi
POSTGRES_PASSWORD="$(<"$db_pass_file")"
export POSTGRES_PASSWORD
export POSTGRES_DB="${POSTGRES_DB:-helinho}"
export POSTGRES_USER="${POSTGRES_USER:-helinho}"
export ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:5018}"
# O seed sintético é opt-in (padrão desligado, para produção); a demonstração local o liga.
export Seed__Enabled=true

# Senha do seed: HELINHO_SEED_PASSWORD (se definida) vale e é gravada; senão reaproveita o arquivo; senão é gerada.
seed_pass_file="$state_dir/seed-password"
if [[ -n "${HELINHO_SEED_PASSWORD:-}" ]]; then
  seed_password_source="variável HELINHO_SEED_PASSWORD"
  (umask 077; printf '%s' "$HELINHO_SEED_PASSWORD" >"$seed_pass_file") && chmod 600 "$seed_pass_file"
elif [[ -s "$seed_pass_file" ]]; then
  seed_password_source="$seed_pass_file"
  HELINHO_SEED_PASSWORD="$(<"$seed_pass_file")"
else
  seed_password_source="gerada agora em $seed_pass_file"
  HELINHO_SEED_PASSWORD="$(rand 20)Aa1!"
  (umask 077; printf '%s' "$HELINHO_SEED_PASSWORD" >"$seed_pass_file") && chmod 600 "$seed_pass_file"
fi
export HELINHO_SEED_PASSWORD

echo "==> Subindo o PostgreSQL..."
docker compose up -d --wait db

echo "==> Aplicando migração e seed..."
dotnet run --project src/Helinho.Web -- seed

base_url="${ASPNETCORE_URLS%%;*}"
echo
echo "==> Sistema em: $base_url"
echo "    Painel: $base_url/"
echo "    Scalar: $base_url/scalar/v1"
echo "    Senha dos usuários do seed: $HELINHO_SEED_PASSWORD  ($seed_password_source)"
echo "    Ctrl+C para parar (o banco continua: 'docker compose down' para desligar)."
echo

exec dotnet run --project src/Helinho.Web --no-launch-profile

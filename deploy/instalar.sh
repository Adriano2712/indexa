#!/usr/bin/env bash
# Instala o Indexa numa VM Ubuntu (testado como alvo: Ubuntu 22.04/24.04, x86 ou ARM).
# Uso na VM:   git clone https://github.com/Adriano2712/indexa.git && cd indexa && bash deploy/instalar.sh
# Rodar de novo é seguro: só instala o que falta e atualiza os containers.
set -euo pipefail

raiz="$(cd "$(dirname "$0")/.." && pwd)"
compose=(docker compose -f "$raiz/deploy/compose.prod.yml" --env-file "$raiz/deploy/.env")

if ! command -v docker >/dev/null 2>&1; then
  echo ">> Instalando Docker"
  curl -fsSL https://get.docker.com | sudo sh
  sudo usermod -aG docker "$USER"
fi

# Imagens Ubuntu da Oracle Cloud vêm com iptables bloqueando tudo menos SSH.
# (Também é preciso liberar 80/443 na Security List da VCN, pelo console.)
if sudo iptables -L INPUT -n 2>/dev/null | grep -q REJECT && ! sudo iptables -C INPUT -p tcp -m multiport --dports 80,443 -j ACCEPT 2>/dev/null; then
  echo ">> Liberando portas 80 e 443 no iptables"
  linha=$(sudo iptables -L INPUT --line-numbers -n | awk '/REJECT/ {print $1; exit}')
  sudo iptables -I INPUT "$linha" -p tcp -m multiport --dports 80,443 -j ACCEPT
  sudo apt-get install -y iptables-persistent >/dev/null 2>&1 || true
  sudo netfilter-persistent save || true
fi

env="$raiz/deploy/.env"
if [ ! -f "$env" ]; then
  cp "$raiz/deploy/.env.example" "$env"
  sed -i "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$(openssl rand -hex 24)|" "$env"
  sed -i "s|^JWT_CHAVE=.*|JWT_CHAVE=$(openssl rand -base64 48 | tr -d '\n')|" "$env"
  chmod 600 "$env"
  echo
  echo ">> Criei deploy/.env com senhas geradas."
  echo "   Edite DOMINIO, SMTP_* e ALERTAS_EMAIL (nano deploy/.env) e rode este script de novo."
  exit 0
fi

grep -q '^DOMINIO=indexa.seudominio.com.br' "$env" && { echo "Edite DOMINIO em deploy/.env antes de continuar."; exit 1; }

mkdir -p "$raiz/deploy/backups"
echo ">> Baixando imagens e subindo"
sudo "${compose[@]}" pull api || echo "(imagem não encontrada no registro; será compilada localmente)"
sudo "${compose[@]}" up -d --no-build || sudo "${compose[@]}" up -d --build
sudo "${compose[@]}" ps
echo
echo ">> Pronto. Acesse https://$(grep '^DOMINIO=' "$env" | cut -d= -f2) (o certificado HTTPS pode levar 1 minuto)."
echo "   Para o beta: PERMITIR_REGISTRO=true no .env, rode de novo, crie as contas e volte para false."

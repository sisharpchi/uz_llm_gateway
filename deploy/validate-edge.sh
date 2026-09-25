#!/bin/sh
set -eu
temp_dir=$(mktemp -d)
trap 'rm -rf "$temp_dir"' EXIT HUP INT TERM
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj /CN=localhost \
  -keyout "$temp_dir/public.key" -out "$temp_dir/public.crt" >/dev/null 2>&1
cp "$temp_dir/public.crt" "$temp_dir/internal-ca.crt"
docker run --rm \
  --add-host gateway-a:127.0.0.1 --add-host gateway-b:127.0.0.1 \
  --add-host management-a:127.0.0.1 --add-host management-b:127.0.0.1 \
  -e NGINX_ENVSUBST_FILTER='^UZLLM_' \
  -e NGINX_ENVSUBST_OUTPUT_DIR=/etc/nginx \
  -e UZLLM_GATEWAY_A=gateway-a:8443 -e UZLLM_GATEWAY_B=gateway-b:8443 \
  -e UZLLM_MANAGEMENT_A=management-a:8443 -e UZLLM_MANAGEMENT_B=management-b:8443 \
  -v "$temp_dir:/run/certs:ro" uzllm-edge:ops002 nginx -t

#!/bin/sh
# Writes the DNS resolver of Nginx before Nginx starts.
# Nginx needs a resolver to find the API again when the address of the API changes.
# The value comes from RELIQUARY_DNS_RESOLVER, or else from the first name server of /etc/resolv.conf.
# Docker Compose gives 127.0.0.11. Kubernetes gives the address of the cluster DNS service.
set -eu

output_dir="${NGINX_ENVSUBST_OUTPUT_DIR:-/etc/nginx/conf.d}"
resolver="${RELIQUARY_DNS_RESOLVER:-}"

if [ -z "$resolver" ]; then
    resolver=$(awk '$1 == "nameserver" { print $2; exit }' /etc/resolv.conf)
fi

if [ -z "$resolver" ]; then
    echo "reliquary: no DNS resolver. Set RELIQUARY_DNS_RESOLVER." >&2
    exit 1
fi

# Nginx needs square brackets around an IPv6 address.
case "$resolver" in
    *:*) case "$resolver" in \[*) ;; *) resolver="[$resolver]" ;; esac ;;
esac

echo "resolver $resolver valid=10s ipv6=off;" > "$output_dir/00-resolver.conf"
echo "reliquary: DNS resolver $resolver, API $RELIQUARY_API_URL"

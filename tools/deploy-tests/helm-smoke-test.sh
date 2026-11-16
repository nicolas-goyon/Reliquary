#!/bin/sh
# Smoke test of the Helm chart deploy/helm/reliquary on a kind cluster.
#
# Usage: tools/deploy-tests/helm-smoke-test.sh [OLD_TAG] [NEW_TAG]   (default: latest develop)
#
# The test does these steps:
# 1. It creates the kind cluster KIND_CLUSTER (default reliquary-smoke), or uses the cluster when it exists.
# 2. It starts PostgreSQL and Redis from helm-smoke/infrastructure.yaml, and makes the Secret of the chart.
# 3. It installs the chart with the images OLD_TAG, and waits until all the pods are ready.
# 4. It checks /health/ready of the API and the worker, and /api/v1/status through the web app.
# 5. It upgrades the chart to the images NEW_TAG, and does the checks again.
#
# The computer needs Docker, kind, kubectl and helm.
# Set KEEP_CLUSTER=true to keep the cluster after the test. By default, the test deletes the cluster that it created.
# Set IMAGE_PREFIX to use other images (default ghcr.io/nicolas-goyon/reliquary).

set -eu

old_tag=${1:-latest}
new_tag=${2:-develop}
repository=$(cd "$(dirname "$0")/../.." && pwd)
cluster=${KIND_CLUSTER:-reliquary-smoke}
namespace=reliquary-smoke
release=reliquary
image_prefix=${IMAGE_PREFIX:-ghcr.io/nicolas-goyon/reliquary}
curl_image=docker.io/curlimages/curl:8.16.0
created_cluster=false

log() {
    printf 'helm-smoke-test: %s\n' "$*"
}

for tool in docker kind kubectl helm openssl; do
    command -v "$tool" >/dev/null 2>&1 || { log "the command $tool is missing."; exit 1; }
done

kube() {
    kubectl --context "kind-$cluster" --namespace "$namespace" "$@"
}

# Shows the state of the cluster when a step fails, then deletes the cluster that the test created.
on_exit() {
    status=$?
    if [ "$status" -ne 0 ]; then
        log "the test failed. State of the namespace $namespace:"
        kube get pods,jobs,services --output wide || true
        kube get events --sort-by=.lastTimestamp | tail -n 40 || true
        for pod in $(kube get pods --output name 2>/dev/null); do
            log "logs of $pod:"
            kube logs "$pod" --all-containers --tail=60 || true
        done
    fi
    if [ "$created_cluster" = "true" ] && [ "${KEEP_CLUSTER:-false}" != "true" ]; then
        kind delete cluster --name "$cluster" || true
    fi
    exit "$status"
}
trap on_exit EXIT

if ! kind get clusters 2>/dev/null | grep --quiet --line-regexp "$cluster"; then
    log "create the kind cluster $cluster."
    created_cluster=true
    if [ -n "${KIND_NODE_IMAGE:-}" ]; then
        kind create cluster --name "$cluster" --image "$KIND_NODE_IMAGE" --wait 180s
    else
        kind create cluster --name "$cluster" --wait 180s
    fi
fi

log "start PostgreSQL and Redis in the namespace $namespace."
kubectl --context "kind-$cluster" create namespace "$namespace" --dry-run=client --output yaml | kubectl --context "kind-$cluster" apply --filename -
postgres_password=$(openssl rand -hex 16)
kube create secret generic smoke-postgres --from-literal=password="$postgres_password" --dry-run=client --output yaml | kube apply --filename -
kube apply --filename "$repository/tools/deploy-tests/helm-smoke/infrastructure.yaml"
kube rollout status deployment/postgres --timeout=180s
kube rollout status deployment/redis --timeout=180s

log "make the Secret of the chart."
kube create secret generic reliquary-secrets \
    --from-literal=Auth__SigningKey="$(openssl rand -base64 32)" \
    --from-literal=ConnectionStrings__Users="Host=postgres;Port=5432;Database=reliquary;Username=reliquary;Password=$postgres_password" \
    --from-literal=ConnectionStrings__Redis="redis:6379" \
    --dry-run=client --output yaml | kube apply --filename -

# Installs or upgrades the chart with the images of a tag, and waits until all the pods are ready.
# The worker does not import the card data, so the test downloads nothing from Scryfall.
deploy() {
    deploy_tag=$1
    helm --kube-context "kind-$cluster" upgrade "$release" "$repository/deploy/helm/reliquary" \
        --install --namespace "$namespace" --wait --timeout 10m \
        --set image.registry="${image_prefix%/*}" \
        --set image.prefix="${image_prefix##*/}" \
        --set image.tag="$deploy_tag" \
        --set image.pullPolicy=Always \
        --set config.publicUrl=http://localhost:8080 \
        --set config.cards.importEnabled=false \
        --set secrets.existingSecret=reliquary-secrets
}

# Sends a GET request from a pod in the cluster, and prints the body. The request fails on an HTTP error.
get() {
    kube run "smoke-curl-$(openssl rand -hex 4)" --image="$curl_image" --restart=Never --rm --quiet --stdin \
        --command -- curl --fail --silent --show-error --max-time 10 "$1"
}

check() {
    check_tag=$1
    succeeded=$(kube get job "$release-migrate" --output jsonpath='{.status.succeeded}')
    [ "$succeeded" = "1" ] || { log "the migration Job did not succeed."; exit 1; }
    get "http://$release-api:8080/health/ready" >/dev/null
    get "http://$release-worker:8080/health/ready" >/dev/null
    api_status=$(get "http://$release-web:8080/api/v1/status")
    log "status of the API through the web app: $api_status"
    printf '%s' "$api_status" | grep --quiet '"version"' || { log "the status has no version."; exit 1; }
    log "the images $check_tag are ready."
}

log "install the chart with the images $old_tag."
deploy "$old_tag"
check "$old_tag"

log "upgrade the chart to the images $new_tag."
deploy "$new_tag"
check "$new_tag"

log "the chart installs, and it upgrades from $old_tag to $new_tag."

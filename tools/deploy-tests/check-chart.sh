#!/bin/sh
# Checks the Helm chart deploy/helm/reliquary: "helm lint", "helm template" and kubeconform.
# The tools run in containers, so the computer needs only Docker.
#
# Usage: tools/deploy-tests/check-chart.sh
#
# The script renders the chart with each file of deploy/helm/reliquary/ci, and checks the manifests against
# the schemas of KUBERNETES_VERSION (default 1.31.0). kubeconform downloads the schemas.
# Behind a proxy, set HTTPS_PROXY. Set SSL_CERT_FILE to give the certificate bundle of the proxy.

set -eu

repository=$(cd "$(dirname "$0")/../.." && pwd)
chart=deploy/helm/reliquary
helm_image=${HELM_IMAGE:-docker.io/alpine/helm:3.19.0}
kubeconform_image=${KUBECONFORM_IMAGE:-ghcr.io/yannh/kubeconform:v0.7.0}
kubernetes_version=${KUBERNETES_VERSION:-1.31.0}
output=$(mktemp -d)
trap 'rm -rf "$output"' EXIT

# The containers use the network of the host, so a proxy on 127.0.0.1 works.
network_options="--network host --env HTTPS_PROXY --env HTTP_PROXY --env NO_PROXY --env https_proxy --env http_proxy --env no_proxy"
certificate_options=""
if [ -n "${SSL_CERT_FILE:-}" ]; then
    certificate_options="--volume $SSL_CERT_FILE:/etc/ssl/certs/ca-certificates.crt:ro"
fi

helm() {
    # shellcheck disable=SC2086 # The options are lists of words.
    docker run --rm $network_options --user "$(id -u):$(id -g)" --env HOME=/tmp \
        --volume "$repository:/repository:ro" --volume "$output:/output" --workdir /repository \
        "$helm_image" "$@"
}

for values in "$repository/$chart"/ci/*.yaml; do
    name=$(basename "$values" .yaml)
    echo "check-chart: lint and render with $name.yaml."
    helm lint --strict "$chart" --values "$chart/ci/$name.yaml"
    helm template reliquary "$chart" --namespace reliquary --values "$chart/ci/$name.yaml" > "$output/$name.yaml"
done

echo "check-chart: check the manifests against the schemas of Kubernetes $kubernetes_version."
# The second schema location gives the schemas of the custom resources, for example HTTPRoute of the Gateway API.
# shellcheck disable=SC2086 # The options are lists of words.
docker run --rm $network_options $certificate_options --volume "$output:/output:ro" "$kubeconform_image" \
    -strict -summary -kubernetes-version "$kubernetes_version" \
    -schema-location default \
    -schema-location 'https://raw.githubusercontent.com/datreeio/CRDs-catalog/main/{{.Group}}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json' \
    /output

echo "check-chart: the chart is correct."

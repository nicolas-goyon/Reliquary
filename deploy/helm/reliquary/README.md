# Helm chart of Reliquary

This chart installs [Reliquary](https://github.com/nicolas-goyon/reliquary) on Kubernetes.
It contains the API, the worker and the web app.
PostgreSQL and Redis are external.
The chart has an optional Redis server for a small cluster.

[ADR 0053](../../../docs/decisions/0053-helm-chart-scope.md) gives the scope of the chart.
[Requirements for Kubernetes](../../../docs/deployment/kubernetes.md) tells how to use the chart.
`values.yaml` gives all the values, with a comment for each value.

## Check the chart

Run `just helm-check`.
The command runs `helm lint`, `helm template` and kubeconform in containers, with the values of the folder `ci`.

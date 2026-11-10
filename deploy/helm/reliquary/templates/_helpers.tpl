{{/* The name of the chart. */}}
{{- define "reliquary.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/* The prefix of the names of the resources. */}}
{{- define "reliquary.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else if contains (include "reliquary.name" .) .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name (include "reliquary.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{/* The labels of all the resources. */}}
{{- define "reliquary.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ include "reliquary.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: reliquary
{{- end -}}

{{/* The selector labels of one component. Call it with (dict "context" $ "component" "api"). */}}
{{- define "reliquary.selectorLabels" -}}
app.kubernetes.io/name: {{ include "reliquary.name" .context }}
app.kubernetes.io/instance: {{ .context.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end -}}

{{/* The labels of one component. Call it with (dict "context" $ "component" "api"). */}}
{{- define "reliquary.componentLabels" -}}
{{ include "reliquary.labels" .context }}
app.kubernetes.io/component: {{ .component }}
{{- end -}}

{{/*
The image of one component. Call it with (dict "context" $ "component" "api" "values" .Values.api).
A digest has priority over a tag.
*/}}
{{- define "reliquary.image" -}}
{{- $values := .values.image | default dict -}}
{{- $root := .context.Values.image -}}
{{- $repository := $values.repository | default (printf "%s/%s-%s" $root.registry $root.prefix .component) -}}
{{- if $values.digest -}}
{{- printf "%s@%s" $repository $values.digest -}}
{{- else -}}
{{- printf "%s:%s" $repository ($values.tag | default $root.tag | default .context.Chart.AppVersion) -}}
{{- end -}}
{{- end -}}

{{/* The name of the Secret of the settings. */}}
{{- define "reliquary.secretName" -}}
{{- .Values.secrets.existingSecret | default (printf "%s-secrets" (include "reliquary.fullname" .)) -}}
{{- end -}}

{{/* The full address of the API Service, for the web app. Nginx does not use the search domains. */}}
{{- define "reliquary.apiUrl" -}}
{{- printf "http://%s-api.%s.svc.%s:8080" (include "reliquary.fullname" .) .Release.Namespace .Values.clusterDomain -}}
{{- end -}}

{{/* The environment variables of the .NET hosts that are not secret: the API, the worker and the CLI. */}}
{{- define "reliquary.backendEnv" -}}
{{- $config := .Values.config -}}
- name: Auth__PublicUrl
  value: {{ required "Set config.publicUrl, for example https://reliquary.example.org." $config.publicUrl | quote }}
{{- range $index, $email := $config.administratorEmails }}
- name: Auth__AdministratorEmails__{{ $index }}
  value: {{ $email | quote }}
{{- end }}
- name: Auth__AllowAccountsWithoutEmail
  value: {{ $config.accountsWithoutEmail | quote }}
- name: Auth__RequireCheckedEmail
  value: {{ $config.requireCheckedEmail | quote }}
{{- with $config.google.clientId }}
- name: Auth__Providers__Google__ClientId
  value: {{ . | quote }}
{{- end }}
{{- with $config.email.from }}
- name: Email__From
  value: {{ . | quote }}
{{- end }}
{{- with $config.email.smtp.host }}
- name: Email__Smtp__Host
  value: {{ . | quote }}
- name: Email__Smtp__Port
  value: {{ $config.email.smtp.port | quote }}
- name: Email__Smtp__Security
  value: {{ $config.email.smtp.security | quote }}
{{- end }}
{{- with $config.email.smtp.userName }}
- name: Email__Smtp__UserName
  value: {{ . | quote }}
{{- end }}
- name: Cards__Import__Enabled
  value: {{ $config.cards.importEnabled | quote }}
- name: Cards__Import__BulkType
  value: {{ $config.cards.bulkType | quote }}
{{- with $config.moxfieldUserAgent }}
- name: Decks__LinkImport__MoxfieldUserAgent
  value: {{ . | quote }}
{{- end }}
- name: Recommendations__Enabled
  value: {{ $config.recommendations | quote }}
{{- if .Values.redis.enabled }}
- name: ConnectionStrings__Redis
  value: {{ printf "%s-redis:6379" (include "reliquary.fullname" .) | quote }}
{{- end }}
{{- with .Values.extraEnv }}
{{ toYaml . }}
{{- end }}
{{- end -}}

{{/* The volumes that a read-only root file system needs: /tmp. */}}
{{- define "reliquary.tmpVolume" -}}
- name: tmp
  emptyDir: {}
{{- end -}}

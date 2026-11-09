# syntax=docker/dockerfile:1
# Container image of the web app: the built files behind Nginx.
# Nginx also sends the /api requests to the API, so the web app and the API are on the same site.

# The build stage runs on the platform of the builder. The output is the same for each target platform.
FROM --platform=$BUILDPLATFORM node:26-alpine AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
# The CI gives the version of the image. A local build uses the version of package.json.
ARG RELIQUARY_VERSION=
ENV RELIQUARY_VERSION=$RELIQUARY_VERSION
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.30-alpine
# The Nginx image makes /etc/nginx/conf.d/default.conf from the template at start.
# RELIQUARY_API_URL gives the address of the API. RELIQUARY_DNS_RESOLVER gives the DNS server.
# With no RELIQUARY_DNS_RESOLVER, the script uses the first name server of /etc/resolv.conf.
# With a read-only root file system, give a writable /tmp and /etc/nginx/conf.d (tmpfs).
ENV RELIQUARY_API_URL=http://api:8080 \
    RELIQUARY_DNS_RESOLVER=
USER root
RUN rm -f /etc/nginx/conf.d/default.conf
USER 101
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --chmod=755 nginx-resolver.sh /docker-entrypoint.d/15-reliquary-resolver.sh
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=5s --retries=24 CMD wget --quiet --output-document=/dev/null http://127.0.0.1:8080/api/v1/status || exit 1

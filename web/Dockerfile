# syntax=docker/dockerfile:1
# Container image of the web app: the built files behind Nginx.
# Nginx also sends the /api requests to the API, so the web app and the API are on the same site.

FROM node:24-alpine AS build
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.28-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=5s --retries=24 CMD wget --quiet --output-document=/dev/null http://127.0.0.1:8080/api/v1/status || exit 1

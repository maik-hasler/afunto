FROM node:26.13.0-slim AS build
WORKDIR /app
COPY package.json pnpm-lock.yaml ./
# Node 25 no longer bundles corepack (nodejs/corepack#783), so it needs an explicit
# install first. --force because this base image already ships /usr/local/bin/yarn,
# and corepack's own package installs its own yarn/yarnpkg shims over the same path -
# without it, npm refuses with EEXIST rather than overwrite it. corepack prepare then
# reads the pnpm version straight off this package.json's "packageManager" field - one
# source of truth Renovate already bumps, instead of a second pin duplicated here that
# can drift out of sync.
RUN npm install -g corepack --force && corepack enable && corepack prepare --activate
RUN pnpm install --frozen-lockfile
COPY . .

# No defaults (#2207): the runtime values in docker-entrypoint.d/99-runtime-
# config.sh are what every deployment actually uses (see frontend/AGENTS.md's
# "Released image"); these build-time ARGs only seed the bundle's offline
# fallback (see src/lib/runtimeConfig.ts) for the rare cold start where the
# PWA cannot reach /config.js at all. A localhost default there silently
# pointed that fallback at the visitor's own machine instead of failing
# loudly, so an unset ARG is left to build empty rather than a value that
# looks plausible while being wrong for every deployment but one.
ARG VITE_KEYCLOAK_AUTHORITY_URL
ARG VITE_KEYCLOAK_CLIENT_ID
ARG VITE_API_URL
ARG VITE_APP_VERSION=dev

RUN VITE_KEYCLOAK_AUTHORITY_URL=$VITE_KEYCLOAK_AUTHORITY_URL \
	VITE_KEYCLOAK_CLIENT_ID=$VITE_KEYCLOAK_CLIENT_ID \
	VITE_API_URL=$VITE_API_URL \
	VITE_APP_VERSION=$VITE_APP_VERSION \
	pnpm run build

FROM nginx:1.31-alpine AS runtime
# nginx:1.31-alpine is a floating tag - it already tracks the newest build of
# that line, but a fresh Alpine security patch can land in the package index
# before upstream rebuilds and re-publishes the image itself. This closes
# that gap by re-resolving every package against the index at build time
# instead of trusting whatever the image happened to bundle last.
RUN apk --no-cache upgrade
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf.template /etc/nginx/nginx.conf.template
COPY docker-entrypoint.d/99-runtime-config.sh /docker-entrypoint.d/99-runtime-config.sh
RUN chmod +x /docker-entrypoint.d/99-runtime-config.sh
EXPOSE 80

# wget is BusyBox's applet, already present on nginx:alpine - no extra package needed.
# --spider skips the response body and just checks that / (the SPA shell) serves 200.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
	CMD wget --no-verbose --tries=1 --spider http://127.0.0.1:80/ || exit 1

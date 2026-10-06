#!/bin/sh
set -eu

# Fail fast, before rendering anything, rather than silently falling back to
# a value that points at the visitor's own machine (#2207) - a missing or
# misspelled env var used to render as an empty substitution in config.js,
# which every API call and the Keycloak login would then silently resolve
# against http://localhost:... in the visitor's own browser.
: "${VITE_API_URL:?VITE_API_URL is required}"
: "${VITE_KEYCLOAK_AUTHORITY_URL:?VITE_KEYCLOAK_AUTHORITY_URL is required}"
: "${VITE_KEYCLOAK_CLIENT_ID:?VITE_KEYCLOAK_CLIENT_ID is required}"

config="/usr/share/nginx/html/config.js"

# OPERATOR_NAME/ADDRESS/EMAIL/SITE_URL carry this deployment's legal identity
# (Impressum, GDPR controller, contact) - templated the same way as the origins
# above rather than baked in at build time, so a self-hosting operator is never
# silently stuck with somebody else's name and address (afunto#2196).
# Unset here means empty after envsubst, not a leftover ${...} placeholder -
# the frontend renders a visible "operator not configured" notice in that case
# instead of falling back to anyone's real details.
if [ -f "$config" ]; then
	tmp="$(mktemp)"
	envsubst '${VITE_KEYCLOAK_AUTHORITY_URL} ${VITE_KEYCLOAK_CLIENT_ID} ${VITE_API_URL} ${VITE_APP_VERSION} ${OPERATOR_NAME} ${OPERATOR_ADDRESS} ${OPERATOR_EMAIL} ${OPERATOR_SITE_URL}' < "$config" > "$tmp"
	mv "$tmp" "$config"
	chmod 644 "$config"
fi

# The Content-Security-Policy's connect-src/frame-src need the backend API
# and Keycloak origins (scheme+host, no path), derived from the same env vars
# as config.js above rather than hardcoded, so whoever runs this image is not
# silently locked to somebody else's origins. img-src additionally needs the
# object storage origin (STORAGE_PUBLIC_URL, matching
# the backend's Storage__PublicEndpoint) since uploaded org logos/opportunity
# banners/avatars are served from there, not from the API origin. Unlike the
# three required above, STORAGE_PUBLIC_URL keeps a fallback: a missing value
# only narrows the CSP's img-src (broken images, loudly visible), not an
# origin that silently swallows every API call and login.
: "${STORAGE_PUBLIC_URL:=http://localhost:9000}"

# BACKEND_UPSTREAM/DNS_RESOLVER back the /sitemap.xml and social-crawler proxy
# targets below. Defaulted to the values a Docker Compose/user-defined-network
# setup with a "backend" service already gets for free, so this stays a no-op
# for that shape - but templated rather than hardcoded, since nothing else
# guarantees a service named exactly "backend" on the frontend's network
# (afunto#2196; the compose file that used to guarantee that name was
# deleted in #2165).
: "${BACKEND_UPSTREAM:=http://backend:8080}"
: "${DNS_RESOLVER:=127.0.0.11}"

url_origin() {
	proto="${1%%://*}"
	rest="${1#*://}"
	host="${rest%%/*}"
	printf '%s://%s' "$proto" "$host"
}

CSP_API_ORIGIN="$(url_origin "$VITE_API_URL")"
CSP_KEYCLOAK_ORIGIN="$(url_origin "$VITE_KEYCLOAK_AUTHORITY_URL")"
CSP_STORAGE_ORIGIN="$(url_origin "$STORAGE_PUBLIC_URL")"
export CSP_API_ORIGIN CSP_KEYCLOAK_ORIGIN CSP_STORAGE_ORIGIN BACKEND_UPSTREAM DNS_RESOLVER

envsubst '${CSP_API_ORIGIN} ${CSP_KEYCLOAK_ORIGIN} ${CSP_STORAGE_ORIGIN} ${BACKEND_UPSTREAM} ${DNS_RESOLVER}' \
	< /etc/nginx/nginx.conf.template \
	> /etc/nginx/conf.d/default.conf

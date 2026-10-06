FROM quay.io/keycloak/keycloak:26.7.4 AS builder

ENV KC_DB=postgres
ENV KC_HEALTH_ENABLED=true
ENV KC_METRICS_ENABLED=false

RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.7.4

COPY --from=builder /opt/keycloak/ /opt/keycloak/
COPY themes /opt/keycloak/themes
COPY realms /opt/keycloak/data/import

# --import-realm's default behavior skips a realm that already exists in the
# database, so restarts are idempotent and the production deployment keeps
# that default: only the first start imports this image's baked realm. Setting
# KC_SPI_REALM_IMPORTER_FILE_IMPORT_BEHAVIOR=OVERWRITE_EXISTING would instead
# rebuild the realm from this copy on every restart and drop every user who
# signed up since. Realm changes after the first import therefore have to be
# applied through the Admin API.
ENTRYPOINT ["/opt/keycloak/bin/kc.sh", "start", "--optimized", "--import-realm"]

# This image has neither curl nor wget (security hardening on the UBI9-micro base),
# so the check speaks raw HTTP over bash's /dev/tcp instead - the same recipe
# Keycloak's own docs recommend for containers built this way. Port 9000 is the
# management interface KC_HEALTH_ENABLED=true above exposes /health/ready on,
# separate from the app's own HTTP(S) port.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
	CMD bash -c '{ printf "HEAD /health/ready HTTP/1.0\r\n\r\n" >&0; grep -q "HTTP/1.0 200"; } 0<>/dev/tcp/127.0.0.1/9000'

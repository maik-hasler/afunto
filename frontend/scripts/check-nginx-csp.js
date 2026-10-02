#!/usr/bin/env node

import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { join, dirname } from "path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const frontendDir = join(__dirname, "..");

const template = readFileSync(
	join(frontendDir, "nginx.conf.template"),
	"utf8",
);
const entrypoint = readFileSync(
	join(frontendDir, "docker-entrypoint.d/99-runtime-config.sh"),
	"utf8",
);

let ok = true;
function fail(message) {
	console.error(message);
	ok = false;
}

const mapMatch = template.match(
	/map\s+\$host\s+\$csp_header\s*\{\s*default\s+"([^"]+)";\s*\}/,
);
if (!mapMatch) {
	fail(
		'Could not find a single `map $host $csp_header { default "..."; }` block in nginx.conf.template. ' +
			"The CSP policy must be defined once and referenced by all location blocks, not duplicated per-location.",
	);
} else {
	const policy = mapMatch[1];

	const imgSrcMatch = policy.match(/img-src ([^;]+);/);
	if (!imgSrcMatch || !imgSrcMatch[1].includes("${CSP_STORAGE_ORIGIN}")) {
		fail(
			"img-src directive is missing ${CSP_STORAGE_ORIGIN} - uploaded org logos/opportunity banners/avatars " +
				"are served from the object storage origin and will be blocked by the browser without it.",
		);
	}
	if (!imgSrcMatch || !imgSrcMatch[1].split(" ").includes("blob:")) {
		fail(
			"img-src directive is missing blob: - avatar/org-logo/opportunity-banner previews are rendered from " +
				"URL.createObjectURL() before upload and will be blocked by the browser without it.",
		);
	}

	const frameSrcMatch = policy.match(/frame-src ([^;]+);/);
	if (!frameSrcMatch || !frameSrcMatch[1].split(" ").includes("'self'")) {
		fail(
			"frame-src directive is missing 'self' - the hidden iframe automaticSilentRenew/signinSilent() " +
				"loads for silent_redirect_uri is same-origin and will be blocked by the browser without it (#2042).",
		);
	}
	if (!frameSrcMatch || !frameSrcMatch[1].includes("${CSP_KEYCLOAK_ORIGIN}")) {
		fail(
			"frame-src directive is missing ${CSP_KEYCLOAK_ORIGIN} - Keycloak's own iframes (e.g. its check-session " +
				"iframe) will be blocked by the browser without it.",
		);
	}
}

const allowedCspHeaderLines = new Set([
	"add_header Content-Security-Policy $csp_header always;",
	"add_header Content-Security-Policy $csp_header_silent_renew always;",
]);
const cspHeaderLines = template
	.split("\n")
	.map((line) => line.trim())
	.filter((line) => /^add_header\s+Content-Security-Policy\s+/.test(line));

if (cspHeaderLines.length === 0) {
	fail("No `add_header Content-Security-Policy` lines found in nginx.conf.template.");
}

for (const line of cspHeaderLines) {
	if (!allowedCspHeaderLines.has(line)) {
		fail(
			`Found a Content-Security-Policy header not referencing a shared $csp_header* variable: "${line}". ` +
				"Every location must emit the header via $csp_header or $csp_header_silent_renew, not a hardcoded policy string.",
		);
	}
}

const templateVars = [...new Set(template.match(/\$\{CSP_[A-Z_]+\}/g) ?? [])];

const envsubstMatch = entrypoint.match(
	/envsubst\s+'([^']*)'\s*\\\s*\n\s*<\s*\/etc\/nginx\/nginx\.conf\.template/,
);
if (!envsubstMatch) {
	fail(
		"Could not find the envsubst invocation rendering nginx.conf.template in 99-runtime-config.sh.",
	);
} else {
	const envsubstVars = new Set(envsubstMatch[1].split(/\s+/).filter(Boolean));
	for (const templateVar of templateVars) {
		if (!envsubstVars.has(templateVar)) {
			fail(
				`nginx.conf.template references ${templateVar} but 99-runtime-config.sh's envsubst call ` +
					"does not include it in its variable list, so it will be left unexpanded at container start.",
			);
		}
	}
}

if (!/:\s*"\$\{STORAGE_PUBLIC_URL:=[^}]+\}"/.test(entrypoint)) {
	fail(
		"99-runtime-config.sh is missing a default fallback for STORAGE_PUBLIC_URL (expected a `: \"${STORAGE_PUBLIC_URL:=...}\"` line).",
	);
}

if (!/CSP_STORAGE_ORIGIN="\$\(url_origin "\$STORAGE_PUBLIC_URL"\)"/.test(entrypoint)) {
	fail(
		"99-runtime-config.sh does not derive CSP_STORAGE_ORIGIN from STORAGE_PUBLIC_URL via url_origin().",
	);
}

if (!/export\s+.*\bCSP_STORAGE_ORIGIN\b/.test(entrypoint)) {
	fail("99-runtime-config.sh computes CSP_STORAGE_ORIGIN but never exports it.");
}

const silentRenewMapMatch = template.match(
	/map\s+\$host\s+\$csp_header_silent_renew\s*\{\s*default\s+"([^"]+)";\s*\}/,
);
if (!silentRenewMapMatch) {
	fail(
		'Could not find a `map $host $csp_header_silent_renew { default "..."; }` block in nginx.conf.template - ' +
			"silent-renew.html (#2042) needs its own policy with frame-ancestors 'self', distinct from every " +
			"other page's frame-ancestors 'none'.",
	);
} else if (!silentRenewMapMatch[1].includes("frame-ancestors 'self'")) {
	fail(
		"$csp_header_silent_renew's policy does not include frame-ancestors 'self' - without it, the browser " +
			"still refuses to render silent-renew.html inside the hidden iframe regardless of frame-src (#2042).",
	);
}

const silentRenewLocationMatch = template.match(
	/location\s*=\s*\/silent-renew\.html\s*\{([\s\S]*?)\n\t\}/,
);
if (!silentRenewLocationMatch) {
	fail("Could not find a `location = /silent-renew.html { ... }` block in nginx.conf.template.");
} else {
	const locationBlock = silentRenewLocationMatch[1];
	if (!/add_header X-Frame-Options "SAMEORIGIN" always;/.test(locationBlock)) {
		fail(
			"The /silent-renew.html location does not set X-Frame-Options: SAMEORIGIN - the site-wide DENY " +
				"(used everywhere else) independently blocks the hidden iframe this page exists for, regardless " +
				"of frame-ancestors/frame-src (#2042).",
		);
	}
	if (!/add_header Content-Security-Policy \$csp_header_silent_renew always;/.test(locationBlock)) {
		fail(
			"The /silent-renew.html location does not emit Content-Security-Policy via $csp_header_silent_renew.",
		);
	}
}

if (ok) {
	console.log(
		"nginx CSP header is consolidated, includes the storage origin, and all referenced vars are substituted.",
	);
} else {
	process.exit(1);
}

#!/usr/bin/env node

import { readFileSync } from "fs";
import { fileURLToPath } from "url";
import { join, dirname } from "path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const frontendDir = join(__dirname, "..");

const viteConfig = readFileSync(join(frontendDir, "vite.config.ts"), "utf8");

let ok = true;
function fail(message) {
	console.error(message);
	ok = false;
}

function sliceBalanced(source, startIndex, open, close) {
	const from = source.indexOf(open, startIndex);
	if (from === -1) return null;
	let depth = 0;
	for (let i = from; i < source.length; i++) {
		if (source[i] === open) depth++;
		else if (source[i] === close) {
			depth--;
			if (depth === 0) return source.slice(from, i + 1);
		}
	}
	return null;
}

const denylistIndex = viteConfig.indexOf("navigateFallbackDenylist:");
const denylistBlock =
	denylistIndex === -1
		? null
		: sliceBalanced(viteConfig, denylistIndex, "[", "]");
if (!denylistBlock) {
	fail("Could not find workbox.navigateFallbackDenylist in vite.config.ts.");
}

// Regex literals in the array look like /^\/silent-renew\.html.../ - scan from the
// first "/" of the one mentioning "silent-renew" to the matching unescaped closing
// "/", respecting backslash escapes, rather than a hand-rolled regex-matching-regex.
function extractRegexLiteralContaining(source, needle) {
	const needleIndex = source.indexOf(needle);
	if (needleIndex === -1) return null;
	const start = source.lastIndexOf("/", needleIndex);
	if (start === -1) return null;
	for (let i = start + 1; i < source.length; i++) {
		if (source[i] === "\\") {
			i++;
			continue;
		}
		if (source[i] === "/") return source.slice(start, i + 1);
	}
	return null;
}

const literal = denylistBlock
	? extractRegexLiteralContaining(denylistBlock, "silent-renew")
	: null;
if (!literal) {
	fail(
		"navigateFallbackDenylist in vite.config.ts has no pattern excluding /silent-renew.html - " +
			"without it, the service worker's SPA-shell fallback intercepts the hidden silent-SSO-renewal " +
			"iframe's navigation and serves index.html instead (#2042), silently breaking automatic " +
			"session renewal (react-oidc-context's automaticSilentRenew/signinSilent) - the only visible " +
			"symptom is a CSP frame-ancestors console warning that looks unrelated.",
	);
} else {
	// Constructing the real RegExp from vite.config.ts's own literal text (rather than
	// pattern-matching the literal's escaping by eye) is the point: this is a trusted,
	// self-authored file, and a text-only assertion here would keep passing even if the
	// escaping changed in a way that broke the pattern, since it would never actually run it.
	const regExp = eval(literal);

	// Workbox's NavigationRoute tests denylist/allowlist patterns against
	// `url.pathname + url.search` (workbox-routing's NavigationRoute.ts _match()), and
	// Keycloak's redirect back to silent_redirect_uri always carries a query string -
	// ?code=...&state=...&session_state=... on success, ?error=login_required&state=...&iss=...
	// with no active SSO session. A pattern anchored with `$` directly after ".html" (no
	// allowance for a query string) only ever matches a bare, query-less request this flow
	// never actually sends - which is exactly the bug this check exists to catch (found live
	// on the staging host: DevTools showed the redirect blocked by
	// frame-ancestors 'none', because the service worker served cached index.html instead of
	// the real silent-renew.html).
	const successRedirect =
		"/silent-renew.html?code=abc123&state=xyz&session_state=1";
	const noSessionRedirect =
		"/silent-renew.html?error=login_required&state=xyz&iss=https%3A%2F%2Flogin.example.test%2Frealms%2Fafunto";

	if (!regExp.test(successRedirect)) {
		fail(
			`navigateFallbackDenylist's silent-renew pattern (${regExp}) does not match a real ` +
				`silent-renewal success redirect ("${successRedirect}"). Keycloak's redirect always ` +
				"carries a query string, so a pattern with no allowance for one silently never excludes it.",
		);
	}
	if (!regExp.test(noSessionRedirect)) {
		fail(
			`navigateFallbackDenylist's silent-renew pattern (${regExp}) does not match the ` +
				`no-active-session redirect ("${noSessionRedirect}").`,
		);
	}
}

if (ok) {
	console.log(
		"workbox.navigateFallbackDenylist's /silent-renew.html pattern matches Keycloak's real, " +
			"query-string-bearing redirect back to it.",
	);
} else {
	process.exit(1);
}

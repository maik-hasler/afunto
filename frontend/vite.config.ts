import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig, type Plugin } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import svgr from "vite-plugin-svgr";
import { VitePWA } from "vite-plugin-pwa";
import { compression } from "vite-plugin-compression2";
import { designTokens } from "./src/lib/designTokens";

const __dirname = dirname(fileURLToPath(import.meta.url));

// The installed app's title bar and splash screen, from the same @theme block
// the stylesheet is built from (see scripts/generate-design-tokens.js). It was
// the brand green spelled out a third and fourth time before that.
const themeColor = designTokens["--color-brand-700"];

const manifestIcons = [
	{
		src: "/icons/icon-192.png",
		sizes: "192x192",
		type: "image/png",
	},
	{
		src: "/icons/icon-512.png",
		sizes: "512x512",
		type: "image/png",
	},
	{
		src: "/icons/icon-512-maskable.png",
		sizes: "512x512",
		type: "image/png",
		purpose: "maskable",
	},
];

const deManifest = {
	id: "/",
	name: "Afunto",
	short_name: "Afunto",
	description:
		"Afunto verbindet engagierte Freiwillige mit regionalen Hilfsangeboten. Finde lokale Einsätze, hilf spontan und mach einen Unterschied in deiner Gemeinde.",
	lang: "de",
	start_url: "/",
	display: "standalone",
	background_color: "#ffffff",
	theme_color: themeColor,
	icons: manifestIcons,

	screenshots: [
		{
			src: "/screenshots/wide-home.png",
			sizes: "1920x1080",
			type: "image/png",
			form_factor: "wide",
			label: "Startseite mit Suche nach Einsätzen in deiner Nähe",
		},
		{
			src: "/screenshots/wide-opportunities.png",
			sizes: "1920x1080",
			type: "image/png",
			form_factor: "wide",
			label: "Übersicht der gefundenen Einsätze mit Filtern",
		},
		{
			src: "/screenshots/narrow-home.png",
			sizes: "1080x1920",
			type: "image/png",
			form_factor: "narrow",
			label: "Startseite mit Suche nach Einsätzen in deiner Nähe",
		},
		{
			src: "/screenshots/narrow-opportunities.png",
			sizes: "1080x1920",
			type: "image/png",
			form_factor: "narrow",
			label: "Liste der gefundenen Einsätze",
		},
		{
			src: "/screenshots/narrow-detail.png",
			sizes: "1080x1920",
			type: "image/png",
			form_factor: "narrow",
			label: "Detailansicht eines Einsatzes mit Ort und Zeitraum",
		},
	],

	shortcuts: [
		{
			name: "Einsätze finden",
			short_name: "Einsätze",
			description: "Freiwilligeneinsätze in deiner Nähe durchsuchen",
			url: "/opportunities",
			icons: [
				{
					src: "/icons/shortcut-search.png",
					sizes: "96x96",
					type: "image/png",
				},
			],
		},
		{
			name: "Meine Einsätze",
			short_name: "Meine Einsätze",
			description: "Einsätze ansehen, für die du dich eingetragen hast",
			url: "/my-signups",
			icons: [
				{
					src: "/icons/shortcut-signups.png",
					sizes: "96x96",
					type: "image/png",
				},
			],
		},
	],
};

const enManifest = {
	id: "/",
	name: "Afunto",
	short_name: "Afunto",
	description:
		"Afunto connects committed volunteers with regional volunteer opportunities. Find local opportunities, help spontaneously, and make a difference in your community.",
	lang: "en",
	start_url: "/",
	display: "standalone",
	background_color: "#ffffff",
	theme_color: themeColor,
	icons: manifestIcons,
	screenshots: [
		{
			src: "/screenshots/wide-home.png",
			sizes: "1920x1080",
			type: "image/png",
			form_factor: "wide",
			label: "Home page with search for opportunities near you",
		},
		{
			src: "/screenshots/wide-opportunities.png",
			sizes: "1920x1080",
			type: "image/png",
			form_factor: "wide",
			label: "Overview of matching opportunities with filters",
		},
		{
			src: "/screenshots/narrow-home.png",
			sizes: "1080x1920",
			type: "image/png",
			form_factor: "narrow",
			label: "Home page with search for opportunities near you",
		},
		{
			src: "/screenshots/narrow-opportunities.png",
			sizes: "1080x1920",
			type: "image/png",
			form_factor: "narrow",
			label: "List of matching opportunities",
		},
		{
			src: "/screenshots/narrow-detail.png",
			sizes: "1080x1920",
			type: "image/png",
			form_factor: "narrow",
			label: "Detail view of an opportunity with location and time period",
		},
	],
	shortcuts: [
		{
			name: "Find opportunities",
			short_name: "Opportunities",
			description: "Search volunteer opportunities near you",
			url: "/opportunities",
			icons: [
				{
					src: "/icons/shortcut-search.png",
					sizes: "96x96",
					type: "image/png",
				},
			],
		},
		{
			name: "My sign-ups",
			short_name: "Sign-ups",
			description: "View your volunteer opportunity sign-ups",
			url: "/my-signups",
			icons: [
				{
					src: "/icons/shortcut-signups.png",
					sizes: "96x96",
					type: "image/png",
				},
			],
		},
	],
};

function emitLocaleManifest(
	fileName: string,
	manifest: Record<string, unknown>,
): Plugin {
	const source = JSON.stringify(manifest, null, 2);
	return {
		name: `emit-${fileName}`,
		generateBundle() {
			this.emitFile({ type: "asset", fileName, source });
		},
	};
}

function stripPwaChromeFromSilentRenew(): Plugin {
	return {
		name: "strip-pwa-chrome-from-silent-renew",
		// VitePWA's own html injection runs as an `enforce: "post"` plugin, not
		// just a `transformIndexHtml` `order: "post"` hook - Vite buckets hooks
		// by their enclosing plugin's `enforce` first, so without this too, our
		// (merely order:"post") hook would run in the earlier "normal" bucket,
		// before VitePWA has injected anything to strip.
		enforce: "post",
		transformIndexHtml: {
			order: "post",
			handler(html, ctx) {
				if (!ctx.filename.endsWith("silent-renew.html")) return html;
				return html
					.replace(/\s*<link rel="manifest"[^>]*>/, "")
					.replace(
						/\s*<script id="vite-plugin-pwa:register-sw"[^>]*><\/script>/,
						"",
					)
					.replace(/\s*<link rel="stylesheet"[^>]*>/, "");
			},
		},
	};
}

export default defineConfig({
	plugins: [
		react(),
		tailwindcss(),
		svgr(),

		compression({
			algorithms: ["gzip"],
			include: /\.(js|mjs|css|json|svg)$/,
			threshold: 1024,
		}),
		VitePWA({
			// "prompt", not "autoUpdate" (#2207): autoUpdate swaps the service
			// worker and its caches the instant a new build is detected, with no
			// reload and no notice - an open tab keeps running the old JS against
			// whatever the new API version expects. "prompt" waits for
			// PwaUpdatePrompt (src/components/PwaUpdatePrompt.tsx) to call
			// updateServiceWorker() from an explicit user action instead.
			registerType: "prompt",
			// PwaUpdatePrompt registers the service worker itself via
			// virtual:pwa-register/react's useRegisterSW - the default injected
			// registration script would otherwise register it a second time.
			injectRegister: false,
			workbox: {
				globPatterns: [
					"index.html",
					"favicon.svg",
					"manifest.*.webmanifest",
					"icons/*.png",
					"assets/{index,vendor}-*.js",
					"assets/*.css",
				],

				// config.js is env-templated at container start
				// (docker-entrypoint.d/99-runtime-config.sh), so it must never be
				// precached: a precache entry is keyed by a build-time revision and
				// served cache-first, which would pin every deployment to whatever
				// origins the image happened to be built against. The runtimeCaching
				// rule below is what covers it instead - keeping it out of *both*
				// was #2317.
				globIgnores: ["config.js"],

				runtimeCaching: [
					// /config.js - the runtime config index.html loads ahead of the
					// app bundle, and the one file the app cannot boot without:
					// lib/runtimeConfig.ts reads window.__APP_CONFIG__ from it for the
					// API/Keycloak origins, and ConfigGate refuses to render anything
					// when they are missing (#2207). Left uncached, that turned every
					// offline reload and every cold start of the installed PWA into a
					// full-page "configuration missing" dead end while index.html, the
					// bundles, the route chunks and the API responses all sat warm in
					// the cache - the one request that failed was this one (#2317).
					// NetworkFirst rather than a cache-first strategy so a redeploy's
					// new origins still win whenever the network answers; the 3s
					// timeout matches the API rules below, and bounds how long a
					// technically-connected-but-useless link can hold the app at a
					// blank screen before the last config that did arrive is used.
					// No expiration: exactly one URL is ever stored here, so there is
					// nothing to evict - and an entry aging out is precisely the state
					// that breaks the cold start this rule exists to fix. Cacheable
					// statuses are [200], not the [0, 200] the API rules below need:
					// those talk to a cross-origin host and so have to accept opaque
					// responses, where the same-origin match here guarantees a
					// readable one - and an opaque status-0 response cached under
					// this name would be an unverifiable stand-in for the one file
					// the whole app boots from.
					{
						urlPattern: ({
							url,
							sameOrigin,
						}: {
							url: URL;
							sameOrigin: boolean;
						}) => sameOrigin && url.pathname === "/config.js",
						handler: "NetworkFirst",
						options: {
							cacheName: "runtime-config",
							networkTimeoutSeconds: 3,
							cacheableResponse: { statuses: [200] },
						},
					},
					{
						urlPattern: /\/assets\/.+\.js$/,
						handler: "StaleWhileRevalidate",
						options: {
							cacheName: "route-chunks",
							expiration: {
								maxEntries: 60,
								maxAgeSeconds: 30 * 24 * 60 * 60,
							},
						},
					},
					// Opportunity list responses (VolunteerOpportunitiesList,
					// LatestOpportunitiesSection) - #2233. Matched by pathname alone,
					// never by origin: the API is served from a separate host that is
					// only known at container start (config.js, see #2207), not at
					// service-worker build time. NetworkFirst prefers a fresh answer
					// whenever the network responds within networkTimeoutSeconds, and
					// only falls back to the last cached page once it doesn't - so a
					// volunteer standing at a meeting point with poor signal still
					// gets the list they already loaded instead of an empty state.
					{
						urlPattern: ({ url }: { url: URL }) =>
							/^\/v1\/volunteer-opportunities$/.test(url.pathname),
						handler: "NetworkFirst",
						options: {
							cacheName: "opportunity-list",
							networkTimeoutSeconds: 3,
							cacheableResponse: { statuses: [0, 200] },
							expiration: {
								maxEntries: 40,
								maxAgeSeconds: 24 * 60 * 60,
							},
						},
					},
					// Opportunity detail responses (VolunteerOpportunityDetailPage) -
					// #2233. Scoped to the bare "/{id}" path so sibling sub-resources
					// (engagements, check-in-pin, date-availability) are never cached -
					// those are either per-user write-adjacent or security-sensitive,
					// unlike the detail payload itself which is AllowAnonymous.
					// GetVolunteerOpportunityDetails still personalizes that payload
					// with the caller's own CurrentUserEngagement, so the cache key is
					// widened with the request's Authorization header (or "anonymous")
					// - without it, two accounts signed in on the same shared/public
					// device would silently read back each other's sign-up/check-in
					// status once the network fetch stopped winning the race.
					{
						urlPattern: ({ url }: { url: URL }) =>
							/^\/v1\/volunteer-opportunities\/[0-9a-fA-F-]+$/.test(
								url.pathname,
							),
						handler: "NetworkFirst",
						options: {
							cacheName: "opportunity-detail",
							networkTimeoutSeconds: 3,
							cacheableResponse: { statuses: [0, 200] },
							expiration: {
								maxEntries: 50,
								maxAgeSeconds: 24 * 60 * 60,
							},
							plugins: [
								{
									cacheKeyWillBeUsed: async ({
										request,
									}: {
										request: Request;
									}) => {
										const cacheUrl = new URL(request.url);
										cacheUrl.searchParams.set(
											"__eb-auth",
											request.headers.get("Authorization") ?? "anonymous",
										);
										return cacheUrl.href;
									},
								},
							],
						},
					},
				],
				navigateFallback: "/index.html",
				// /silent-renew.html (#2042) is a real static file, not a client
				// route - without this, the service worker's SPA-shell fallback
				// would intercept the hidden iframe's navigation to it and serve
				// index.html instead, booting the full app right back into the
				// iframe it exists to avoid. Workbox's NavigationRoute matches
				// denylist patterns against pathname + search (its own docs and
				// workbox-routing's NavigationRoute.ts _match()), and Keycloak's
				// redirect back here always carries a query string
				// (?code=...&state=... on success, ?error=login_required&state=...
				// with no active session) - the un-anchored-for-query original
				// pattern below only ever matched a bare, query-less request that
				// this flow never actually sends, so the fallback intercepted every
				// silent-renewal round trip and served index.html (with its
				// frame-ancestors 'none') into the hidden iframe instead.
				navigateFallbackDenylist: [/^\/v1\//, /^\/silent-renew\.html(\?.*)?$/],
			},

			manifest: deManifest,
			manifestFilename: "manifest.de.webmanifest",
		}),
		emitLocaleManifest("manifest.en.webmanifest", enManifest),
		stripPwaChromeFromSilentRenew(),
	],

	build: {
		cssCodeSplit: false,
		rollupOptions: {
			input: {
				index: resolve(__dirname, "index.html"),
				silentRenew: resolve(__dirname, "silent-renew.html"),
			},
			output: {
				manualChunks(id) {
					if (
						/[\\/]node_modules[\\/](react|react-dom|scheduler)[\\/]/.test(id)
					) {
						return "vendor-react";
					}
					if (/[\\/]node_modules[\\/]react-router[\\/]/.test(id)) {
						return "vendor-router";
					}
					return undefined;
				},
			},
		},
	},
	server: { port: 4321 },
});

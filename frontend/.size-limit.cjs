/**
 * Byte budgets, measured against `pnpm build` and enforced by `pnpm size`.
 *
 * Every limit below is the size measured when the budget was introduced plus
 * roughly five per cent of headroom, rounded up to a whole kilobyte. That makes
 * this a ratchet, not an aspiration: a change that grows a chunk by more than
 * its slack has to say so in the diff, and a change that shrinks one should
 * lower the number in the same commit so the slack does not quietly accumulate.
 *
 * All sizes are gzipped, because that is what crosses the wire - nginx serves
 * the precompressed `.gz` siblings vite-plugin-compression2 emits
 * (`check:nginx-gzip-static` guards the wiring).
 *
 * The one entry that grew rather than shrank when these were introduced is the
 * first load, by about 10 kB: that is TanStack Query, and ADR-9 argues the
 * trade. Everything else moved the other way - the organizer dashboard from
 * 76 kB to 12, the administration area from one 6.3 kB chunk to a 0.6 kB shell
 * plus the one tab the visitor opened.
 *
 * "First load" is not a guess: it is exactly the set VitePWA precaches
 * (`workbox.globPatterns` in vite.config.ts - index/vendor JS plus the single
 * stylesheet `cssCodeSplit: false` produces), which is what a visitor
 * downloads before any route chunk is fetched.
 */
module.exports = [
	{
		name: "First load (entry + vendor + CSS)",
		path: [
			"dist/assets/index-*.js",
			"dist/assets/vendor-react-*.js",
			"dist/assets/vendor-router-*.js",
			"dist/assets/style-*.css",
		],
		// Raised from 154 kB when main's React 19.2.8 -> 19.3.0 bump (#2439) was
		// merged in. Measured on identical sources either side of that merge:
		// 146.9 kB -> 155.5 kB, and the whole 8.6 kB sits in `vendor-react`
		// (58.9 -> 67.5 kB) while `index` and `vendor-router` moved by bytes.
		// Recorded rather than silently bumped, because "the dependency grew"
		// is the one explanation that makes a ratchet meaningless if nobody
		// ever checks it.
		limit: "163 kB",
		gzip: true,
	},
	{
		name: "Stylesheet (cssCodeSplit: false - every route pays for all of it)",
		path: "dist/assets/style-*.css",
		limit: "24 kB",
		gzip: true,
	},
	{
		name: "Generated API client",
		// Rolldown names a shared chunk after one of its modules, so this follows the
		// client wherever it lands: `apiError-*` until the email removal shrank it
		// enough to share a chunk with `formClasses` (11.3 kB gzipped, form recipes
		// included). Re-point it if the next build moves the client again.
		path: "dist/assets/formClasses-*.js",
		limit: "12 kB",
		gzip: true,
	},
	{
		name: "Form schemas (zod)",
		path: "dist/assets/schemas-*.js",
		limit: "30 kB",
		gzip: true,
	},
	{
		name: "Locale: de",
		path: "dist/assets/de-*.js",
		limit: "33 kB",
		gzip: true,
	},
	{
		name: "Locale: en",
		path: "dist/assets/en-*.js",
		limit: "30 kB",
		gzip: true,
	},
	{
		name: "Route: organizer dashboard",
		path: "dist/assets/OrgDashboardPage-*.js",
		limit: "13 kB",
		gzip: true,
	},
	{
		// Lazy, and the reason the dashboard above is 12 kB rather than 76:
		// react-big-calendar plus its date-fns locales. Only organizers whose
		// saved layout holds the Calendar tile pay for it.
		name: "Widget: calendar (lazy)",
		path: "dist/assets/CalendarWidget-*.js",
		limit: "68 kB",
		gzip: true,
	},
	{
		// The sum of the shell and all four tabs, which is the number that would
		// move if they ever merged back into one module: no visitor loads more
		// than the shell plus the one tab they opened.
		name: "Administration (shell + four tabs)",
		path: ["dist/assets/Administration*-*.js", "dist/assets/Admin*-*.js"],
		limit: "12 kB",
		gzip: true,
	},
	{
		name: "Route: opportunity detail",
		path: "dist/assets/VolunteerOpportunityDetailPage-*.js",
		limit: "9 kB",
		gzip: true,
	},
	{
		name: "Map (leaflet)",
		path: "dist/assets/SingleMarkerMap-*.js",
		limit: "48 kB",
		gzip: true,
	},
	{
		name: "QR scanner (jsqr)",
		path: "dist/assets/QRScannerModal-*.js",
		limit: "52 kB",
		gzip: true,
	},
];

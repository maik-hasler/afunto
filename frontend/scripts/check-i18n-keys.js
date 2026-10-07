#!/usr/bin/env node
import { readFileSync, readdirSync, statSync } from "fs";
import { fileURLToPath } from "url";
import { join, dirname, extname } from "path";

const __dirname = dirname(fileURLToPath(import.meta.url));

function flattenKeys(obj, prefix = "") {
	const keys = new Set();
	for (const [k, v] of Object.entries(obj)) {
		const full = prefix ? `${prefix}.${k}` : k;
		if (v !== null && typeof v === "object" && !Array.isArray(v)) {
			for (const nested of flattenKeys(v, full)) keys.add(nested);
		} else {
			keys.add(full);
		}
	}
	return keys;
}

function flattenValues(obj, prefix = "") {
	const out = {};
	for (const [k, v] of Object.entries(obj)) {
		const full = prefix ? `${prefix}.${k}` : k;
		if (v !== null && typeof v === "object" && !Array.isArray(v)) {
			Object.assign(out, flattenValues(v, full));
		} else {
			out[full] = v;
		}
	}
	return out;
}

function placeholderSet(value) {
	if (typeof value !== "string") return new Set();
	const matches = value.match(/\{\{\s*[\w-]+\s*\}\}|<[a-zA-Z][\w-]*>/g) ?? [];
	return new Set(matches.map((m) => m.replace(/\s+/g, "")));
}

function setsEqual(a, b) {
	if (a.size !== b.size) return false;
	for (const item of a) if (!b.has(item)) return false;
	return true;
}

const ALLOWED_IDENTICAL_KEYS = new Set(
	[
		"brand.name",

		"contact.email",

		"footer.support",
		"footer.license",
		"nav.administration",
		"landing.heroStat3Label",
		"landing.missionAuthor",
		"orgDashboard.settingsWidgetTitle",

		"opportunities.remote",
		"opportunities.filterLabelRemote",
		"opportunities.dateRangeDisplay",
		"opportunities.radiusKmValue",
		"opportunities.category.Sport",
		"createOpportunity.step3Title",
		"createOpportunity.step4Title",
		"createOpportunity.fieldTags",
		"common.charCount",
		"timeSlots.removing",
		"timeSlots.seriesBadge",
		"organization.nameLabel",
		"orgSettings.fieldLogo",
		"orgSettings.fieldName",
		"orgSettings.fieldWebsite",
		"feedback.organizerTab",
		"checkIn.pinLabel",
		"checkIn.submitting",
		"checkIn.markingCheckedIn",
		"checkIn.undoingCheckIn",
		"myEngagements.addToCalendarOutlook",
		"engagementManagement.processing",
		"engagementManagement.filterLabelStatus",
		"orgEngagements.processing",
		"orgEngagements.filterLabelStatus",
		"privacyPolicy.section8Title",
		"language.de",
		"language.en",
		"report.detailsLabel",
		"report.reasons.Spam",
		"invitations.accepting",
		"invitations.declining",
		"imageCrop.zoomLabel",
		"profile.preferredLanguageDe",
		"profile.preferredLanguageEn",
		"administration.title",
		"administration.users.adminBadge",
		"administration.reports.reason.Spam",
		"orgOverview.tabDashboard",
		"orgOverview.calendarAgenda",
		"orgOverview.eventChipUnlimited",

		"help.eyebrow",
		"contact.eyebrow",
	].map((k) => `translation.${k}`),
);

const EMAIL_TEMPLATE_ALLOWED_IDENTICAL_KEYS = new Set([
	// A .NET format string, not copy.
	"layout.timePattern",
	// The same word in both languages.
	"layout.online",
]);

const PLURAL_EXEMPT_KEYS = new Set(
	[
		"opportunities.nCategoriesSelected",
		"opportunities.radiusKmValue",
		"timeSlots.bookedCount",
		"timeSlots.seriesBadge",
		"signUp.spotsLeft",
		"orgOverview.calendarShowMore",
	].map((k) => `translation.${k}`),
);

const PLURAL_SUFFIXES = ["_zero", "_one", "_two", "_few", "_many", "_other"];

function pluralBaseKey(key) {
	for (const suffix of PLURAL_SUFFIXES) {
		if (key.endsWith(suffix)) return key.slice(0, -suffix.length);
	}
	return null;
}

// "Anmelden" is reserved for authentication; participation uses "eintragen"
// (CONTRIBUTING.md's German copy conventions, #2237). A hand sweep cannot hold
// this: #2261 swept these strings out and #2257 put two back the same day.
// Keys here are legitimately about signing in, where the English twin does not
// happen to carry the vocabulary this check looks for.
const ANMELDEN_AUTH_ALLOWLIST = new Set([
	// The 7-day *login* streak badge (LoginStreakMiddleware); EN name is "On a Roll".
	"translation.achievements.badges.on-a-roll-7.name",
]);

// German register is "du" throughout, never "Sie" (CONTRIBUTING.md). Keys here
// use a capitalised "Sie"/"Ihr*" as third-person plural about other people,
// not as formal address of the reader.
const SIE_EXEMPT_KEYS = new Set([
	// "Sie werden automatisch als eingecheckt markiert" - the participants, not you.
	"translation.createOpportunity.checkInMethodNoneHint",
	// "Sie bekommen eine E-Mail" - the invited people, not you.
	"translation.help.organizersA5",
]);

const AUTH_VOCABULARY =
	/\bsign(s|ing|ed)? ?-?in\b|\bsign(s|ing|ed)? ?-?out\b|\blog ?in\b|\blogged ?in\b|authenticat|\bsession\b|\bpassword\b|\baccount\b|\bcredential/i;

function checkAnmeldenReservedForAuth(enValues, deValues, label) {
	const violations = [];
	for (const key of Object.keys(deValues)) {
		if (typeof deValues[key] !== "string") continue;
		if (!/anmeld/i.test(deValues[key])) continue;
		if (ANMELDEN_AUTH_ALLOWLIST.has(key)) continue;
		if (AUTH_VOCABULARY.test(enValues[key] ?? "")) continue;
		violations.push(key);
	}
	if (violations.length === 0) return [];
	return [
		`${label}: German uses "anmelden" where the English is not about authentication - participation is "eintragen"/"Eintragung" (CONTRIBUTING.md, #2237):\n` +
			violations
				.map((k) => `  - ${k}\n      de: ${JSON.stringify(deValues[k])}\n      en: ${JSON.stringify(enValues[k] ?? "")}`)
				.join("\n") +
			"\nIf the string really is about signing in, add it to ANMELDEN_AUTH_ALLOWLIST in this script.",
	];
}

function checkGermanRegister(deValues, label) {
	const violations = [];
	for (const key of Object.keys(deValues)) {
		if (typeof deValues[key] !== "string") continue;
		if (!/\b(Sie|Ihre|Ihrem|Ihren|Ihnen|Ihrer|Ihres)\b/.test(deValues[key])) continue;
		if (SIE_EXEMPT_KEYS.has(key)) continue;
		violations.push(key);
	}
	if (violations.length === 0) return [];
	return [
		`${label}: German uses the formal "Sie"/"Ihr" register - this product says "du" throughout (CONTRIBUTING.md):\n` +
			violations.map((k) => `  - ${k} = ${JSON.stringify(deValues[k])}`).join("\n") +
			"\nIf the word is third-person plural about other people rather than formal address, add the key to SIE_EXEMPT_KEYS in this script.",
	];
}

function checkKeyParity(enKeys, deKeys) {
	const missingInDe = [...enKeys].filter((k) => !deKeys.has(k));
	const missingInEn = [...deKeys].filter((k) => !enKeys.has(k));
	const errors = [];
	if (missingInDe.length > 0) {
		errors.push(
			"Keys present in en.json but missing in de.json:\n" +
				missingInDe.map((k) => `  - ${k}`).join("\n"),
		);
	}
	if (missingInEn.length > 0) {
		errors.push(
			"Keys present in de.json but missing in en.json:\n" +
				missingInEn.map((k) => `  - ${k}`).join("\n"),
		);
	}
	return errors;
}

function checkIdenticalValues(enValues, deValues, allowlist, label) {
	const violations = [];
	for (const key of Object.keys(enValues)) {
		if (typeof enValues[key] !== "string") continue;
		if (enValues[key] !== deValues[key]) continue;
		if (allowlist.has(key)) continue;
		violations.push(key);
	}
	if (violations.length === 0) return [];
	return [
		`${label}: values are byte-identical between en and de (looks like an untranslated copy-paste) and not on the allowlist:\n` +
			violations.map((k) => `  - ${k} = ${JSON.stringify(enValues[k])}`).join("\n") +
			"\nIf this is a legitimate loanword/brand name, add it to ALLOWED_IDENTICAL_KEYS in this script.",
	];
}

function checkPlaceholderDrift(enValues, deValues, allKeys, label) {
	const violations = [];
	for (const key of allKeys) {
		if (typeof enValues[key] !== "string" || typeof deValues[key] !== "string") continue;
		const enSet = placeholderSet(enValues[key]);
		const deSet = placeholderSet(deValues[key]);
		if (!setsEqual(enSet, deSet)) {
			violations.push(
				`  - ${key}: en has {${[...enSet].join(", ")}}, de has {${[...deSet].join(", ")}}`,
			);
		}
	}
	if (violations.length === 0) return [];
	return [`${label}: placeholder/{{...}} tokens differ between en and de:\n` + violations.join("\n")];
}

function checkPluralCompleteness(enValues, deValues, label) {
	const violations = [];
	for (const [key, value] of Object.entries(enValues)) {
		if (typeof value !== "string") continue;
		if (pluralBaseKey(key) !== null) continue;
		if (!value.includes("{{count}}")) continue;
		if (PLURAL_EXEMPT_KEYS.has(key)) continue;
		const hasOneOrOther =
			`${key}_one` in enValues ||
			`${key}_other` in enValues ||
			`${key}_one` in deValues ||
			`${key}_other` in deValues;
		if (!hasOneOrOther) violations.push(key);
	}
	if (violations.length === 0) return [];
	return [
		`${label}: keys interpolate {{count}} but have no _one/_other plural variant in either language ` +
			"(a bare key alone renders the same text for count=1 as for count=5):\n" +
			violations.map((k) => `  - ${k} = ${JSON.stringify(enValues[k])}`).join("\n"),
	];
}

function checkPluralSuffixParity(enKeys, deKeys, label) {
	const enFamilies = new Map();
	const deFamilies = new Map();
	for (const k of enKeys) {
		const base = pluralBaseKey(k);
		if (base === null) continue;
		if (!enFamilies.has(base)) enFamilies.set(base, new Set());
		enFamilies.get(base).add(k.slice(base.length));
	}
	for (const k of deKeys) {
		const base = pluralBaseKey(k);
		if (base === null) continue;
		if (!deFamilies.has(base)) deFamilies.set(base, new Set());
		deFamilies.get(base).add(k.slice(base.length));
	}
	const allBases = new Set([...enFamilies.keys(), ...deFamilies.keys()]);
	const violations = [];
	for (const base of allBases) {
		const enSuffixes = enFamilies.get(base) ?? new Set();
		const deSuffixes = deFamilies.get(base) ?? new Set();
		if (!setsEqual(enSuffixes, deSuffixes)) {
			violations.push(
				`  - ${base}: en has {${[...enSuffixes].join(", ")}}, de has {${[...deSuffixes].join(", ")}}`,
			);
		}
	}
	if (violations.length === 0) return [];
	return [`${label}: plural-form suffixes differ between en and de for the same key:\n` + violations.join("\n")];
}

function checkUnusedKeys(enKeys) {
	const srcDir = join(__dirname, "../src");

	function walkSourceFiles(dir, out = []) {
		for (const entry of readdirSync(dir)) {
			if (entry === "node_modules") continue;
			// The generated client is ~7,300 lines of TypeScript that mentions no
			// translation key but is full of string literals. Feeding it into the
			// corpus that backs the unused-key check is one coincidence away from
			// marking a genuinely unused key as used. This used to be a filename
			// compare against NSwag's single `api-client.ts`; @hey-api emits a
			// directory, so the skip has to be one too.
			if (entry === "generated" && dir.endsWith("client")) continue;
			const full = join(dir, entry);
			const st = statSync(full);
			if (st.isDirectory()) {
				walkSourceFiles(full, out);
			} else if ([".ts", ".tsx"].includes(extname(entry))) {
				out.push(full);
			}
		}
		return out;
	}

	const enKeysNoRoot = new Set([...enKeys].map((k) => k.replace(/^translation\./, "")));

	const sourceText = walkSourceFiles(srcDir)
		.map((f) => readFileSync(f, "utf8"))
		.join("\n");

	const dynamicRoots = new Set();

	for (const m of sourceText.matchAll(/`([A-Za-z0-9_.]*)\$\{/g)) {
		const prefix = m[1];
		const lastDot = prefix.lastIndexOf(".");
		if (lastDot > 0) dynamicRoots.add(prefix.slice(0, lastDot));
	}

	for (const m of sourceText.matchAll(/["'`]([A-Za-z0-9_.]+)["'`]\s*\+/g)) {
		const prefix = m[1].replace(/\.$/, "");
		dynamicRoots.add(prefix);
	}

	function isUsed(key) {
		const base = pluralBaseKey(key) ?? key;
		if (
			sourceText.includes(`"${base}"`) ||
			sourceText.includes(`'${base}'`) ||
			sourceText.includes(`\`${base}\``)
		) {
			return true;
		}
		for (const root of dynamicRoots) {
			if (base === root || base.startsWith(`${root}.`)) return true;
		}
		return false;
	}

	const unused = [...enKeysNoRoot].filter((k) => !isUsed(k)).sort();
	if (unused.length === 0) return [];
	return [
		`locales: ${unused.length} translation key(s) have no reference anywhere in frontend/src:\n` +
			unused.map((k) => `  - ${k}`).join("\n"),
	];
}

function runParityChecks(enPath, dePath, label) {
	const en = JSON.parse(readFileSync(enPath, "utf8"));
	const de = JSON.parse(readFileSync(dePath, "utf8"));
	const enKeys = flattenKeys(en);
	const deKeys = flattenKeys(de);
	const enValues = flattenValues(en);
	const deValues = flattenValues(de);

	const identicalAllowlist =
		label === "locales" ? ALLOWED_IDENTICAL_KEYS : EMAIL_TEMPLATE_ALLOWED_IDENTICAL_KEYS;

	return [
		...checkKeyParity(enKeys, deKeys),
		...checkIdenticalValues(enValues, deValues, identicalAllowlist, label),
		...checkPlaceholderDrift(enValues, deValues, enKeys, label),
		...checkPluralCompleteness(enValues, deValues, label),
		...checkPluralSuffixParity(enKeys, deKeys, label),
		...checkAnmeldenReservedForAuth(enValues, deValues, label),
		...checkGermanRegister(deValues, label),
		...(label === "locales" ? checkUnusedKeys(enKeys) : []),
	];
}

const localesDir = join(__dirname, "../src/locales");
const emailTemplatesDir = join(__dirname, "../../backend/src/Infrastructure/Email/Templates");

const errors = [
	...runParityChecks(join(localesDir, "en.json"), join(localesDir, "de.json"), "locales"),
	...runParityChecks(
		join(emailTemplatesDir, "en.json"),
		join(emailTemplatesDir, "de.json"),
		"email templates",
	),
];

if (errors.length > 0) {
	for (const error of errors) console.error(error + "\n");
	process.exit(1);
}

const totalKeys = flattenKeys(JSON.parse(readFileSync(join(localesDir, "en.json"), "utf8"))).size;
console.log(`All ${totalKeys} locale keys match between de.json and en.json, plus email templates - no drift detected.`);

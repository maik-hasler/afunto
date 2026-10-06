import { beforeEach, describe, expect, it } from "vitest";

import {
	APP_NAME,
	registerPageDescription,
	registerPageTitle,
	resetDocumentMeta,
	setDocumentMetaDefaults,
} from "./documentMeta";

const GERMAN_DEFAULTS = {
	socialTitle: "Afunto - Finde deinen Einsatz.",
	description: "Afunto verbindet Freiwillige mit Hilfsangeboten.",
};

const ENGLISH_DEFAULTS = {
	socialTitle: "Afunto - Find your opportunity.",
	description: "Afunto connects volunteers with regional needs.",
};

function content(selector: string): string | null {
	return document.querySelector(selector)?.getAttribute("content") ?? null;
}

const ogTitle = () => content('meta[property="og:title"]');
const twitterTitle = () => content('meta[name="twitter:title"]');
const description = () => content('meta[name="description"]');
const ogDescription = () => content('meta[property="og:description"]');
const twitterDescription = () => content('meta[name="twitter:description"]');

describe("documentMeta", () => {
	beforeEach(() => {
		document.head.innerHTML = `
			<meta name="description" content="${GERMAN_DEFAULTS.description}" />
			<meta property="og:title" content="${GERMAN_DEFAULTS.socialTitle}" />
			<meta property="og:description" content="${GERMAN_DEFAULTS.description}" />
			<meta name="twitter:title" content="${GERMAN_DEFAULTS.socialTitle}" />
			<meta name="twitter:description" content="${GERMAN_DEFAULTS.description}" />
		`;
		resetDocumentMeta(GERMAN_DEFAULTS);
	});

	it("mirrors the page title into the social title tags", () => {
		registerPageTitle("First Aid Course");

		expect(document.title).toBe(`First Aid Course | ${APP_NAME}`);
		expect(ogTitle()).toBe(`First Aid Course | ${APP_NAME}`);
		expect(twitterTitle()).toBe(`First Aid Course | ${APP_NAME}`);
	});

	it("restores the default social title once the page unregisters", () => {
		const unregister = registerPageTitle("First Aid Course");
		unregister();

		expect(document.title).toBe(APP_NAME);
		expect(ogTitle()).toBe(GERMAN_DEFAULTS.socialTitle);
		expect(twitterTitle()).toBe(GERMAN_DEFAULTS.socialTitle);
	});

	it("writes a page description to all three description tags", () => {
		registerPageDescription("Help out at the animal shelter.");

		expect(description()).toBe("Help out at the animal shelter.");
		expect(ogDescription()).toBe("Help out at the animal shelter.");
		expect(twitterDescription()).toBe("Help out at the animal shelter.");
	});

	it("falls back to the default description, not the previous page's", () => {
		const unregister = registerPageDescription("Help out at the shelter.");
		unregister();

		expect(description()).toBe(GERMAN_DEFAULTS.description);
	});

	// The bug in #2328: switching to English left every route that sets no
	// description of its own serving the German index.html prose.
	it("swaps the defaults when the interface language changes", () => {
		setDocumentMetaDefaults(ENGLISH_DEFAULTS);

		expect(description()).toBe(ENGLISH_DEFAULTS.description);
		expect(ogTitle()).toBe(ENGLISH_DEFAULTS.socialTitle);
	});

	it("keeps a page's own values when the defaults change underneath", () => {
		registerPageTitle("First Aid Course");
		registerPageDescription("A four-hour refresher.");

		setDocumentMetaDefaults(ENGLISH_DEFAULTS);

		expect(ogTitle()).toBe(`First Aid Course | ${APP_NAME}`);
		expect(description()).toBe("A four-hour refresher.");
	});

	// A page yields with `null` while a RouteState owns the title, so the two
	// can be registered at once; the later registration wins and removing it
	// hands the title back rather than blanking it.
	it("hands the title back to an earlier owner when the later one leaves", () => {
		registerPageTitle("Organization");
		const unregisterInner = registerPageTitle("Not found");

		expect(document.title).toBe(`Not found | ${APP_NAME}`);

		unregisterInner();
		expect(document.title).toBe(`Organization | ${APP_NAME}`);
	});

	it("treats an empty page title as the bare app name", () => {
		registerPageTitle("");

		expect(document.title).toBe(APP_NAME);
		expect(ogTitle()).toBe(GERMAN_DEFAULTS.socialTitle);
	});
});

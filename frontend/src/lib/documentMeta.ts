export const APP_NAME = "Afunto";

const SOCIAL_TITLE_SELECTORS = [
	'meta[property="og:title"]',
	'meta[name="twitter:title"]',
];

const DESCRIPTION_SELECTORS = [
	'meta[name="description"]',
	'meta[property="og:description"]',
	'meta[name="twitter:description"]',
];

export interface DocumentMetaDefaults {
	socialTitle: string;
	description: string;
}

interface Entry {
	id: symbol;
	value: string;
}

function readContent(selector: string): string | null {
	return document.querySelector(selector)?.getAttribute("content") ?? null;
}

// index.html ships German values so a crawler that never runs the bundle still
// gets prose rather than an empty head; useDocumentMetaDefaults replaces them
// with the interface language's own as soon as i18n is up (#2328).
let defaults: DocumentMetaDefaults = {
	socialTitle: readContent(SOCIAL_TITLE_SELECTORS[0]) ?? APP_NAME,
	description: readContent(DESCRIPTION_SELECTORS[0]) ?? "",
};

// Stacks rather than single slots: a route can have two live owners at once -
// a page yielding to the RouteState it renders for a load failure, say - and
// on a route change React runs every cleanup before every effect, so the
// entering page always lands after the leaving one is gone.
const titles: Entry[] = [];
const descriptions: Entry[] = [];

function topOf(entries: Entry[]): string | undefined {
	return entries.at(-1)?.value;
}

function setContent(selectors: string[], content: string): void {
	selectors.forEach((selector) =>
		document.querySelector(selector)?.setAttribute("content", content),
	);
}

// Everything reapplies from the current defaults plus the current owners, so
// the language changing and a page setting its own title can arrive in either
// order without one clobbering the other.
function apply(): void {
	const pageTitle = topOf(titles);
	const documentTitle = pageTitle ? `${pageTitle} | ${APP_NAME}` : APP_NAME;
	document.title = documentTitle;
	setContent(
		SOCIAL_TITLE_SELECTORS,
		pageTitle ? documentTitle : defaults.socialTitle,
	);
	setContent(
		DESCRIPTION_SELECTORS,
		topOf(descriptions) ?? defaults.description,
	);
}

function register(entries: Entry[], value: string): () => void {
	const entry: Entry = { id: Symbol("documentMeta"), value };
	entries.push(entry);
	apply();
	return () => {
		const index = entries.indexOf(entry);
		if (index !== -1) entries.splice(index, 1);
		apply();
	};
}

export function setDocumentMetaDefaults(next: DocumentMetaDefaults): void {
	defaults = next;
	apply();
}

export function registerPageTitle(title: string): () => void {
	return register(titles, title);
}

export function registerPageDescription(description: string): () => void {
	return register(descriptions, description);
}

// Test-only: the stacks and defaults are module state, so a suite that mounts
// several pages needs a way back to a clean document.
export function resetDocumentMeta(next?: DocumentMetaDefaults): void {
	titles.length = 0;
	descriptions.length = 0;
	defaults = next ?? {
		socialTitle: readContent(SOCIAL_TITLE_SELECTORS[0]) ?? APP_NAME,
		description: readContent(DESCRIPTION_SELECTORS[0]) ?? "",
	};
	apply();
}

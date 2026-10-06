import { describe, expect, it } from "vitest";

import indexHtml from "../../index.html?raw";
import documentMetaSource from "../lib/documentMeta.ts?raw";

describe("static document title", () => {
	const appName = () => {
		const match = /const APP_NAME = "([^"]+)"/.exec(documentMetaSource);
		expect(
			match,
			"documentMeta.ts must declare a string APP_NAME const",
		).not.toBeNull();
		return match?.[1];
	};

	const staticTitle = () => {
		const match = /<title>(.*?)<\/title>/s.exec(indexHtml);
		expect(match, "index.html must declare a <title> element").not.toBeNull();
		return match?.[1];
	};

	it("is the bare app name, not a translated sentence", () => {
		expect(staticTitle()).toBe("Afunto");
	});

	it("matches what usePageTitle resets document.title to", () => {
		expect(staticTitle()).toBe(appName());
	});
});

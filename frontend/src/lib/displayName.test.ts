import { describe, it, expect, beforeEach, vi } from "vitest";
import {
	clearDisplayNameOverride,
	getDisplayNameOverride,
	setDisplayNameOverride,
	subscribeDisplayName,
} from "./displayName";

const SUB = "aaaaaaaa-0000-0000-0000-000000000001";

beforeEach(() => {
	sessionStorage.clear();
	clearDisplayNameOverride();
});

describe("display name override", () => {
	it("returns the saved name for the subject it was saved for", () => {
		setDisplayNameOverride(SUB, "Veronika Schmidt-Neuberger");

		expect(getDisplayNameOverride(SUB)).toBe("Veronika Schmidt-Neuberger");
	});

	it("does not hand another account's session the previous name", () => {
		setDisplayNameOverride(SUB, "Veronika Schmidt-Neuberger");

		expect(getDisplayNameOverride("someone-else")).toBeNull();
		expect(getDisplayNameOverride(undefined)).toBeNull();
	});

	it("survives a reload by way of sessionStorage", async () => {
		setDisplayNameOverride(SUB, "Veronika Schmidt-Neuberger");

		// A reload starts the module over with an empty in-memory cache, so the
		// value has to come back off the store rather than out of the closure.
		vi.resetModules();
		const fresh = await import("./displayName");

		expect(fresh.getDisplayNameOverride(SUB)).toBe(
			"Veronika Schmidt-Neuberger",
		);
	});

	it("ignores a stored value that is not a name for a subject", async () => {
		sessionStorage.setItem("afunto.display-name", "{oh no");

		vi.resetModules();
		const fresh = await import("./displayName");

		expect(fresh.getDisplayNameOverride(SUB)).toBeNull();
	});

	it("clears on sign-out so the next account starts from its own token", () => {
		setDisplayNameOverride(SUB, "Veronika Schmidt-Neuberger");

		clearDisplayNameOverride();

		expect(getDisplayNameOverride(SUB)).toBeNull();
		expect(sessionStorage.getItem("afunto.display-name")).toBeNull();
	});

	it("treats a blank name as no override rather than storing an empty pill", () => {
		setDisplayNameOverride(SUB, "Veronika");

		setDisplayNameOverride(SUB, "   ");

		expect(getDisplayNameOverride(SUB)).toBeNull();
	});

	it("notifies subscribers on both save and clear", () => {
		let calls = 0;
		const unsubscribe = subscribeDisplayName(() => calls++);

		setDisplayNameOverride(SUB, "Veronika");
		clearDisplayNameOverride();
		unsubscribe();
		setDisplayNameOverride(SUB, "Ignored");

		expect(calls).toBe(2);
	});
});

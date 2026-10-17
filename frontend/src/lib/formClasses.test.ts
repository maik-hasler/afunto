import { describe, it, expect } from "vitest";
import {
	checkboxClass,
	radioClass,
	inputSurfaceClass,
	inputClass,
	textareaClass,
	labelClass,
	selectClass,
	fieldBorderClass,
	getInputClass,
	getTextareaClass,
	getLabelClass,
} from "./formClasses";

describe("formClasses", () => {
	it("gives inputSurfaceClass the shared visual recipe (radius/border/focus)", () => {
		expect(inputSurfaceClass).toContain("rounded-xl");
		expect(inputSurfaceClass).toContain("border-gray-200");
		expect(inputSurfaceClass).toContain("focus:border-brand-400");
		expect(inputSurfaceClass).toContain("shadow-sm");
	});

	it("keeps inputSurfaceClass free of block-level layout classes", () => {
		expect(inputSurfaceClass.split(" ")).not.toContain("block");
		expect(inputSurfaceClass.split(" ")).not.toContain("mt-1");
	});

	it("builds inputClass from inputSurfaceClass plus block-level layout", () => {
		for (const cls of inputSurfaceClass.split(" ")) {
			expect(inputClass).toContain(cls);
		}
		expect(inputClass).toContain("mt-1");
		expect(inputClass).toContain("block");
		expect(inputClass).toContain("text-gray-900");
	});

	it("extends inputClass with vertical resize for textareaClass", () => {
		for (const cls of inputClass.split(" ")) {
			expect(textareaClass).toContain(cls);
		}
		expect(textareaClass).toContain("resize-y");
	});

	it("keeps labelClass a small muted label style", () => {
		expect(labelClass).toContain("text-xs");
		expect(labelClass).toContain("text-gray-600");
	});

	it("never paints the select chevron as a data: background image (#2225)", () => {
		// The deployed CSP's img-src has no `data:` - a `bg-[url('data:...')]` chevron
		// is silently blocked while appearance-none has already removed the native
		// arrow, leaving the control looking disabled. components/Select.tsx draws
		// the chevron as an inline <svg> instead; this class must stay free of any
		// background-image so that fix can't silently regress.
		expect(selectClass).not.toContain("data:");
		expect(selectClass).not.toContain("bg-[url(");
		expect(selectClass).toContain("appearance-none");
	});

	it("gives every invalid field the same red border/focus treatment (#2239)", () => {
		expect(fieldBorderClass(true)).toContain("border-red-300");
		expect(fieldBorderClass(true)).toContain("focus:border-red-400");
		expect(fieldBorderClass(true)).not.toContain("border-gray-200");
	});

	it("keeps the pristine border/focus treatment when there is no error", () => {
		expect(fieldBorderClass(false)).toContain("border-gray-200");
		expect(fieldBorderClass(false)).toContain("focus:border-brand-400");
		expect(fieldBorderClass(false)).not.toContain("border-red-300");
	});

	it("carries the invalid border into getInputClass/getTextareaClass", () => {
		expect(getInputClass(true)).toContain("border-red-300");
		expect(getTextareaClass(true)).toContain("border-red-300");
		expect(getTextareaClass(true)).toContain("resize-y");
	});

	it("defaults getInputClass/getTextareaClass to the pristine treatment", () => {
		expect(getInputClass()).toBe(inputClass);
		expect(getTextareaClass()).toBe(textareaClass);
	});

	it("turns the label red on error, same as the field it labels", () => {
		expect(getLabelClass(true)).toContain("text-red-600");
		expect(getLabelClass(false)).toBe(labelClass);
	});
});

// Sources rather than rendered output: the defect this guards against is that
// `text-brand-600` on a native checkbox renders exactly like no class at all,
// so there is nothing in the DOM to assert on - the browser paints its own
// default blue and the markup looks fine either way (#2329 F8). The one place
// the mistake is visible is the class string itself.
const sources = import.meta.glob("../**/*.tsx", {
	query: "?raw",
	import: "default",
	eager: true,
}) as Record<string, string>;

function inputElementsOfType(source: string, type: string): string[] {
	return source
		.split("<input")
		.slice(1)
		.map((chunk) => chunk.slice(0, chunk.indexOf("/>")))
		.filter((element) => element.includes(`type="${type}"`));
}

describe("the shared checkbox recipe", () => {
	it("drives the checked fill through accent-color", () => {
		expect(checkboxClass).toContain("accent-brand-600");
		// `text-*` and `border-*` are inert on a control the UA paints itself.
		expect(checkboxClass).not.toMatch(/\b(text|border)-/);
	});

	it("is what every checkbox in the app is painted with", () => {
		let checked = 0;
		for (const [path, source] of Object.entries(sources)) {
			if (path.includes(".test.")) continue;
			for (const element of inputElementsOfType(source, "checkbox")) {
				checked += 1;
				expect(
					element,
					`${path} paints a checkbox without the shared recipe, so it falls back to the browser's default blue`,
				).toContain("checkboxClass");
			}
		}
		// A floor, so a scan that silently matches nothing cannot pass. Six since
		// the opportunity wizard's remote checkbox became an on-site/remote
		// choice (#2387).
		expect(checked).toBeGreaterThanOrEqual(6);
	});
});

// The same guardrail as above, for radios (#2329's fix never got extended to
// `type="radio"`, and two radio groups slipped through unstyled as a result).
// A radio that is itself `sr-only` never paints a native dot at all - its
// wrapping `<label>` carries the visible state instead - so it's exempt.
describe("the shared radio recipe", () => {
	it("drives the checked fill through accent-color", () => {
		expect(radioClass).toContain("accent-brand-600");
		expect(radioClass).not.toMatch(/\b(text|border)-/);
	});

	it("is what every non-sr-only radio in the app is painted with", () => {
		let checked = 0;
		for (const [path, source] of Object.entries(sources)) {
			if (path.includes(".test.")) continue;
			for (const element of inputElementsOfType(source, "radio")) {
				if (element.includes("sr-only")) continue;
				checked += 1;
				expect(
					element,
					`${path} paints a radio without the shared recipe, so it falls back to the browser's default blue`,
				).toContain("radioClass");
			}
		}
		expect(checked).toBeGreaterThanOrEqual(2);
	});
});

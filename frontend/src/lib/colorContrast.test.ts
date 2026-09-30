import { describe, it, expect } from "vitest";
import {
	readableTextColor,
	contrastRatio,
	bestTextContrastRatio,
	meetsTextContrastFloor,
} from "./colorContrast";

describe("readableTextColor", () => {
	it("picks dark text on a near-white background", () => {
		expect(readableTextColor("#ffff00")).toBe("#111827");
	});

	it("picks dark text on a light brand shade", () => {
		expect(readableTextColor("#5bbf8c")).toBe("#111827");
	});

	it("picks white text on the default brand-700 background", () => {
		expect(readableTextColor("#226947")).toBe("#ffffff");
	});

	it("picks white text on a fully saturated dark color", () => {
		expect(readableTextColor("#0000ff")).toBe("#ffffff");
	});

	it("expands 3-digit shorthand hex", () => {
		expect(readableTextColor("#000")).toBe("#ffffff");
		expect(readableTextColor("#fff")).toBe("#111827");
	});
});

describe("contrastRatio", () => {
	it("returns 21:1 for black on white", () => {
		expect(contrastRatio("#000000", "#ffffff")).toBeCloseTo(21, 0);
	});

	it("returns 1:1 for identical colors", () => {
		expect(contrastRatio("#3eaf78", "#3eaf78")).toBeCloseTo(1, 5);
	});

	it("is symmetric regardless of argument order", () => {
		expect(contrastRatio("#e7000b", "#fef2f2")).toBeCloseTo(
			contrastRatio("#fef2f2", "#e7000b"),
			5,
		);
	});

	it("flags Chip's pre-fix danger tone (text-red-600 on bg-red-50) as failing WCAG AA", () => {
		expect(contrastRatio("#e7000b", "#fef2f2")).toBeLessThan(4.5);
	});

	it("confirms Chip's danger tone clears WCAG AA after the text-red-700 fix (afunto#1671)", () => {
		expect(contrastRatio("#c10007", "#fef2f2")).toBeGreaterThanOrEqual(4.5);
	});
});

describe("bestTextContrastRatio / meetsTextContrastFloor", () => {
	it("rejects brand-600 - the two text candidates cross over near this luminance and both fall short of 4.5:1 (afunto#1726)", () => {
		expect(bestTextContrastRatio("#2d8a5e")).toBeCloseTo(4.28, 1);
		expect(meetsTextContrastFloor("#2d8a5e")).toBe(false);
	});

	it("rejects pure red - its best text contrast (white-on-red) is only 4.44:1", () => {
		expect(bestTextContrastRatio("#ff0000")).toBeLessThan(4.5);
		expect(meetsTextContrastFloor("#ff0000")).toBe(false);
	});

	it("accepts the default brand-700 background", () => {
		expect(meetsTextContrastFloor("#226947")).toBe(true);
	});

	it("matches whichever color readableTextColor actually picks", () => {
		for (const hex of ["#5bbf8c", "#226947", "#0000ff", "#3366cc"]) {
			const picked = readableTextColor(hex);
			expect(bestTextContrastRatio(hex)).toBeCloseTo(
				contrastRatio(picked, hex),
				5,
			);
		}
	});
});

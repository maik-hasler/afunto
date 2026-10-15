import { describe, it, expect } from "vitest";
import {
	byStartDateTime,
	capacityToInput,
	findOverlappingSlotIds,
	hasUnaddedSlotInput,
	isCompleteDateTime,
	MAX_PARTICIPANTS_LIMIT,
	overlapsAnySlot,
	resolveCapacity,
} from "./timeSlots";

describe("isCompleteDateTime (#2373 follow-up)", () => {
	it("accepts a full date-time value", () => {
		expect(isCompleteDateTime("2026-09-18T21:23")).toBe(true);
	});

	it("rejects an empty value", () => {
		expect(isCompleteDateTime("")).toBe(false);
	});

	it("rejects a date picked without a time yet", () => {
		expect(isCompleteDateTime("2026-09-18")).toBe(false);
	});
});

describe("hasUnaddedSlotInput (#2388)", () => {
	it("is false for an untouched add form", () => {
		expect(hasUnaddedSlotInput({ startDateTime: "", endDateTime: "" })).toBe(
			false,
		);
	});

	it("is true once only a start date is picked", () => {
		expect(
			hasUnaddedSlotInput({ startDateTime: "2026-09-18", endDateTime: "" }),
		).toBe(true);
	});

	it("is true once only an end is set", () => {
		expect(
			hasUnaddedSlotInput({
				startDateTime: "",
				endDateTime: "2026-09-18T12:00",
			}),
		).toBe(true);
	});
});

describe("resolveCapacity (#2325)", () => {
	it("reads a whole number inside the cap", () => {
		expect(resolveCapacity("4")).toBe(4);
		expect(resolveCapacity(" 12 ")).toBe(12);
		expect(resolveCapacity("1")).toBe(1);
		expect(resolveCapacity(String(MAX_PARTICIPANTS_LIMIT))).toBe(
			MAX_PARTICIPANTS_LIMIT,
		);
	});

	it("reads a ticked unlimited box as unlimited", () => {
		expect(resolveCapacity(null)).toBeNull();
	});

	it("rejects zero rather than rewriting it to one", () => {
		expect(resolveCapacity("0")).toBeUndefined();
	});

	it("rejects an empty or half-typed box instead of substituting a value", () => {
		expect(resolveCapacity("")).toBeUndefined();
		expect(resolveCapacity("   ")).toBeUndefined();
		expect(resolveCapacity("-")).toBeUndefined();
		expect(resolveCapacity("1.5")).toBeUndefined();
		expect(resolveCapacity("1e5")).toBeUndefined();
	});

	it("rejects a figure past the cap", () => {
		expect(resolveCapacity("999999999")).toBeUndefined();
		expect(resolveCapacity(String(MAX_PARTICIPANTS_LIMIT + 1))).toBeUndefined();
	});
});

describe("capacityToInput", () => {
	it("round-trips through resolveCapacity", () => {
		expect(resolveCapacity(capacityToInput(7))).toBe(7);
		expect(resolveCapacity(capacityToInput(null))).toBeNull();
	});
});

describe("byStartDateTime", () => {
	it("orders slots by when they start, not by when they were added", () => {
		const slots = [
			{ startDateTime: "2026-12-20T09:00:00.000Z", endDateTime: "x" },
			{ startDateTime: "2026-10-05T08:00:00.000Z", endDateTime: "x" },
			{ startDateTime: "2026-11-11T09:00:00.000Z", endDateTime: "x" },
		];

		expect(
			[...slots].sort(byStartDateTime).map((s) => s.startDateTime.slice(0, 10)),
		).toEqual(["2026-10-05", "2026-11-11", "2026-12-20"]);
	});
});

const slot = (id: string, startHour: number, endHour: number) => ({
	id,
	startDateTime: `2026-09-10T${String(startHour).padStart(2, "0")}:00:00.000Z`,
	endDateTime: `2026-09-10T${String(endHour).padStart(2, "0")}:00:00.000Z`,
});

describe("findOverlappingSlotIds (#2325)", () => {
	it("names both sides of an overlap", () => {
		const ids = findOverlappingSlotIds([
			slot("a", 10, 12),
			slot("b", 11, 13),
			slot("c", 14, 15),
		]);

		expect([...ids].sort()).toEqual(["a", "b"]);
	});

	it("treats slots that only touch at the boundary as separate", () => {
		expect(
			findOverlappingSlotIds([slot("a", 10, 12), slot("b", 12, 14)]).size,
		).toBe(0);
	});

	it("finds nothing in a schedule with no collisions", () => {
		expect(
			findOverlappingSlotIds([slot("a", 8, 9), slot("b", 10, 11)]).size,
		).toBe(0);
	});
});

describe("overlapsAnySlot (#2325)", () => {
	const existing = [slot("a", 10, 12)];
	const at = (hour: number) => Date.parse(`2026-09-10T0${hour}:00:00.000Z`);

	it("reports a candidate window that runs into an existing slot", () => {
		expect(
			overlapsAnySlot(
				Date.parse("2026-09-10T11:00:00.000Z"),
				Date.parse("2026-09-10T13:00:00.000Z"),
				existing,
			),
		).toBe(true);
	});

	it("stays quiet for a window that clears every existing slot", () => {
		expect(overlapsAnySlot(at(7), at(9), existing)).toBe(false);
	});

	it("stays quiet while the boxes hold nothing usable yet", () => {
		expect(overlapsAnySlot(Number.NaN, at(9), existing)).toBe(false);
		expect(overlapsAnySlot(at(9), at(9), existing)).toBe(false);
	});
});

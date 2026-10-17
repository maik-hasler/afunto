import { describe, expect, it } from "vitest";
import {
	meetingLink,
	meetingState,
	parseMeetingTimes,
	suggestEndTime,
} from "./meetings";

const NOW = new Date("2026-10-15T09:00:00Z");
const LATER = new Date("2026-10-20T10:00:00Z");

describe("meetingState", () => {
	it("is the viewer's turn when the other side proposed and nobody answered", () => {
		expect(
			meetingState(
				{ endsAt: LATER, proposedBy: "Organization", response: "Pending" },
				"Volunteer",
				NOW,
			),
		).toBe("awaitingYou");
	});

	it("waits for the other side when the viewer proposed", () => {
		expect(
			meetingState(
				{ endsAt: LATER, proposedBy: "Volunteer", response: "Pending" },
				"Volunteer",
				NOW,
			),
		).toBe("awaitingOther");
	});

	it.each([
		["Accepted", "accepted"],
		["Declined", "declined"],
	] as const)("reports %s regardless of side", (response, expected) => {
		expect(
			meetingState(
				{ endsAt: LATER, proposedBy: "Organization", response },
				"Organization",
				NOW,
			),
		).toBe(expected);
	});

	it("is over once the end has passed, whatever the answer", () => {
		expect(
			meetingState(
				{ endsAt: NOW, proposedBy: "Organization", response: "Pending" },
				"Volunteer",
				NOW,
			),
		).toBe("over");
	});
});

describe("meetingLink", () => {
	it("links an https URL", () => {
		expect(meetingLink("  https://meet.example.org/abc  ")).toBe(
			"https://meet.example.org/abc",
		);
	});

	it.each([
		"http://meet.example.org/abc",
		"javascript:alert(1)",
		"Vereinsheim, Hauptstraße 1",
		"https://example.org und dann klingeln",
		"",
	])("does not link %j", (location) => {
		expect(meetingLink(location)).toBeNull();
	});
});

describe("parseMeetingTimes", () => {
	it("reads the day and times as Berlin wall-clock time", () => {
		const result = parseMeetingTimes("2026-10-20", "18:00", "19:30", NOW);

		expect(result).toEqual({
			ok: true,
			startsAt: new Date("2026-10-20T16:00:00Z"),
			endsAt: new Date("2026-10-20T17:30:00Z"),
		});
	});

	it.each([
		["", "18:00", "19:00", "incomplete"],
		["2026-10-20", "", "19:00", "incomplete"],
		["2026-10-20", "18:00", "", "incomplete"],
		["2026-10-14", "18:00", "19:00", "startInPast"],
		["2026-10-20", "18:00", "18:00", "endBeforeStart"],
		// Same day only: 23:00 to 01:00 is not read as running past midnight.
		["2026-10-20", "23:00", "01:00", "endBeforeStart"],
		["2026-10-20", "08:00", "20:01", "tooLong"],
	])("rejects %j %j-%j as %s", (date, start, end, error) => {
		expect(parseMeetingTimes(date, start, end, NOW)).toEqual({
			ok: false,
			error,
		});
	});

	it("accepts exactly the maximum duration", () => {
		expect(parseMeetingTimes("2026-10-20", "08:00", "20:00", NOW).ok).toBe(
			true,
		);
	});
});

describe("suggestEndTime", () => {
	it.each([
		["09:30", "10:30"],
		["22:15", "23:15"],
	])("suggests an hour after %s", (start, end) => {
		expect(suggestEndTime(start)).toBe(end);
	});

	it.each(["23:00", "23:45", "", "9"])("suggests nothing for %j", (start) => {
		expect(suggestEndTime(start)).toBeNull();
	});
});

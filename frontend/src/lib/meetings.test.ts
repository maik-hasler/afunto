import { describe, expect, it } from "vitest";
import { meetingLink, meetingState, parseMeetingTimes } from "./meetings";

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
	it("reads the picker values as Berlin wall-clock time", () => {
		const result = parseMeetingTimes(
			"2026-10-20T18:00",
			"2026-10-20T19:30",
			NOW,
		);

		expect(result).toEqual({
			ok: true,
			startsAt: new Date("2026-10-20T16:00:00Z"),
			endsAt: new Date("2026-10-20T17:30:00Z"),
		});
	});

	it.each([
		["2026-10-20", "2026-10-20T19:00", "incomplete"],
		["", "", "incomplete"],
		["2026-10-14T18:00", "2026-10-14T19:00", "startInPast"],
		["2026-10-20T18:00", "2026-10-20T18:00", "endBeforeStart"],
		["2026-10-20T18:00", "2026-10-20T17:00", "endBeforeStart"],
		["2026-10-20T08:00", "2026-10-20T20:01", "tooLong"],
	])("rejects %j to %j as %s", (start, end, error) => {
		expect(parseMeetingTimes(start, end, NOW)).toEqual({ ok: false, error });
	});

	it("accepts exactly the maximum duration", () => {
		expect(
			parseMeetingTimes("2026-10-20T08:00", "2026-10-20T20:00", NOW).ok,
		).toBe(true);
	});
});

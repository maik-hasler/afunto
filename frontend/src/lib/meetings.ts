import { CANONICAL_TIME_ZONE, zonedDatetimeLocalToUtc } from "./timezone";

/** Which side of a meeting the viewer is on - the backend's `MeetingParty`. */
export type MeetingParty = "Organization" | "Volunteer";

/** The part of an `EngagementMeetingSummary` the state depends on. */
export interface MeetingStateInput {
	endsAt: Date;
	proposedBy: string;
	response: string;
}

/**
 * Where a meeting stands from one side's point of view. A pending proposal
 * waits for whoever did not make it, so "whose turn is it" follows from who
 * proposed it - there is no separate turn field to fall out of step.
 */
export type MeetingState =
	"over" | "awaitingYou" | "awaitingOther" | "accepted" | "declined";

export function meetingState(
	meeting: MeetingStateInput,
	viewer: MeetingParty,
	now: Date = new Date(),
): MeetingState {
	if (new Date(meeting.endsAt).getTime() <= now.getTime()) return "over";
	if (meeting.response === "Accepted") return "accepted";
	if (meeting.response === "Declined") return "declined";
	return meeting.proposedBy === viewer ? "awaitingOther" : "awaitingYou";
}

/**
 * The meeting's place as a link, when it is one. Only https is linked: anyone
 * can create an organization, and a plain-http or `javascript:` "link" from a
 * stranger is not something to put one click away. Anything else - an address,
 * "Online, Link folgt" - renders as text.
 */
export function meetingLink(location: string): string | null {
	const trimmed = location.trim();
	if (trimmed === "" || /\s/.test(trimmed)) return null;
	try {
		const url = new URL(trimmed);
		return url.protocol === "https:" ? url.href : null;
	} catch {
		return null;
	}
}

// Mirrors Meeting.MaxDuration in the backend (Domain/Engagements/Meeting.cs).
// Advisory only: the domain rejects a longer meeting either way, this just
// says so before the round trip.
export const MEETING_MAX_DURATION_HOURS = 12;

export type MeetingTimesError =
	"incomplete" | "startInPast" | "endBeforeStart" | "tooLong";

export type MeetingTimes =
	| { ok: true; startsAt: Date; endsAt: Date }
	| { ok: false; error: MeetingTimesError };

const COMPLETE_DATETIME_LOCAL = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/;

/**
 * Turns the two `yyyy-MM-ddTHH:mm` picker values into UTC instants, read as
 * wall-clock time in the platform's canonical zone like every other date the
 * product authors.
 */
export function parseMeetingTimes(
	start: string,
	end: string,
	now: Date = new Date(),
): MeetingTimes {
	if (
		!COMPLETE_DATETIME_LOCAL.test(start) ||
		!COMPLETE_DATETIME_LOCAL.test(end)
	)
		return { ok: false, error: "incomplete" };

	const startsAt = zonedDatetimeLocalToUtc(start, CANONICAL_TIME_ZONE);
	const endsAt = zonedDatetimeLocalToUtc(end, CANONICAL_TIME_ZONE);

	if (startsAt.getTime() <= now.getTime())
		return { ok: false, error: "startInPast" };
	if (endsAt.getTime() <= startsAt.getTime())
		return { ok: false, error: "endBeforeStart" };
	if (
		endsAt.getTime() - startsAt.getTime() >
		MEETING_MAX_DURATION_HOURS * 60 * 60 * 1000
	)
		return { ok: false, error: "tooLong" };

	return { ok: true, startsAt, endsAt };
}

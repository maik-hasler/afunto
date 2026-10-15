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

/** How a meeting takes place - the backend's `MeetingFormat`. */
export type MeetingFormat = "InPerson" | "Online";

/** What a proposal consists of - the form's output and its prefill. */
export interface MeetingDraft {
	startsAt: Date;
	endsAt: Date;
	format: MeetingFormat;
	/** The address or meeting point, or the https link for an online meeting. */
	location: string;
	note: string | null;
}

/** A meeting as the API returns it, narrowed to a draft to prefill the form. */
export function toMeetingDraft(meeting: {
	startsAt: Date;
	endsAt: Date;
	format: string;
	location: string;
	note: string | null;
}): MeetingDraft {
	return {
		startsAt: meeting.startsAt,
		endsAt: meeting.endsAt,
		format: meeting.format === "Online" ? "Online" : "InPerson",
		location: meeting.location,
		note: meeting.note,
	};
}

/**
 * An online meeting's link, when it is one the app will put a click away. Only
 * https is linked: anyone can create an organization, and a plain-http or
 * `javascript:` "link" from a stranger is not something to make clickable. The
 * backend refuses anything else for an online meeting, so this is the second
 * line, not the first.
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

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;
const TIME_OF_DAY = /^\d{2}:\d{2}$/;

/**
 * Turns the form's day and its two times of day into UTC instants, read as
 * wall-clock time in the platform's canonical zone like every other date the
 * product authors. A meeting starts and ends on the same day, so an end at or
 * before the start is an error rather than a meeting running past midnight.
 */
export function parseMeetingTimes(
	date: string,
	startTime: string,
	endTime: string,
	now: Date = new Date(),
): MeetingTimes {
	if (
		!ISO_DATE.test(date) ||
		!TIME_OF_DAY.test(startTime) ||
		!TIME_OF_DAY.test(endTime)
	)
		return { ok: false, error: "incomplete" };

	const startsAt = zonedDatetimeLocalToUtc(
		`${date}T${startTime}`,
		CANONICAL_TIME_ZONE,
	);
	const endsAt = zonedDatetimeLocalToUtc(
		`${date}T${endTime}`,
		CANONICAL_TIME_ZONE,
	);

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

/**
 * The end a freshly picked start suggests: an hour later, which is what most
 * meetings last. Null when that would cross midnight - the organizer then
 * picks the end themselves.
 */
export function suggestEndTime(startTime: string): string | null {
	if (!TIME_OF_DAY.test(startTime)) return null;
	const [hours, minutes] = startTime.split(":").map(Number);
	if (hours >= 23) return null;
	return `${String(hours + 1).padStart(2, "0")}:${String(minutes).padStart(2, "0")}`;
}

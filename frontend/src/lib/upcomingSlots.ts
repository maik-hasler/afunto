import type { OrganizationCalendarEventDto } from "../client";
import { pickLocalizedText } from "./format";

export const MAX_UPCOMING_SLOTS = 6;

/**
 * One occurrence an organizer still has to staff.
 *
 * The dashboard used to list *opportunities* by whichever slot came next, which
 * is the wrong unit: a Saturday market stall that runs every week is one
 * opportunity and twelve separate mornings that each need people. Rolling the
 * twelve up into one row hid eleven of them, and the "8/20 sign-ups" it showed
 * beside the title was the whole series' total, so a mostly-full series read as
 * healthy while this Saturday sat empty.
 */
export interface UpcomingSlot {
	id: string;
	opportunityId: string;

	title: string;

	/** The locale the title was actually served in, for a `lang` attribute. */
	titleLang: string;

	start: string;
	end: string;

	startMs: number;

	bookedCount: number;

	/** `null` when the slot takes as many people as turn up. */
	maxParticipants: number | null;

	membersOnly: boolean;
}

/**
 * The next occurrences across every opportunity, soonest first.
 *
 * Keyed off each slot's END rather than its start, so a shift that is running
 * right now stays at the top of the list instead of disappearing from it at the
 * moment the organizer is most likely to be looking.
 */
export function selectUpcomingSlots(
	events: OrganizationCalendarEventDto[],
	nowMs: number,
	unnamedTitle: string,
	lang: string,
	limit: number = MAX_UPCOMING_SLOTS,
): UpcomingSlot[] {
	return events
		.flatMap((event): UpcomingSlot[] => {
			// The calendar endpoint returns every occurrence an organizer has
			// scheduled, drafts included - which is right for the calendar, where
			// they plan. It is wrong here: nobody can sign up to a draft, so an
			// empty one is not a shift that is short of people, and sorted purely
			// by time it looks like the most desperate row on the board. The
			// widget this replaced asked the opportunities endpoint for
			// "Published" and so never had to think about it.
			if (event.status !== "Published") return [];

			const title = pickLocalizedText(event.titleDe, event.titleEn, lang);
			return event.timeSlots.flatMap((slot): UpcomingSlot[] => {
				const start = slot.startDateTime as unknown as string;
				const end = slot.endDateTime as unknown as string;
				const startMs = new Date(start).getTime();
				const endMs = new Date(end).getTime();

				if (Number.isNaN(startMs) || Number.isNaN(endMs)) return [];
				if (endMs < nowMs) return [];

				return [
					{
						id: slot.timeSlotId,
						opportunityId: event.opportunityId,
						title: title.text || unnamedTitle,
						titleLang: title.lang,
						start,
						end,
						startMs,
						bookedCount: slot.bookedCount,
						maxParticipants: slot.maxParticipants ?? null,
						membersOnly: event.membersOnly,
					},
				];
			});
		})
		.sort((a, b) => a.startMs - b.startMs)
		.slice(0, limit);
}

import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { EngagementMeetingSummary } from "../client";
import { formatDateTimeRange } from "../lib/format";
import {
	meetingLink,
	meetingState,
	type MeetingParty,
	type MeetingState,
} from "../lib/meetings";
import { quoteMarks } from "../lib/quotes";
import { inlineLinkClass } from "../lib/linkClasses";
import Chip, { type ChipTone } from "./Chip";
import { CalendarIcon, GlobeIcon, MapPinIcon, UsersIcon } from "./icons";

const STATE_TONES: Record<MeetingState, ChipTone> = {
	awaitingYou: "warning",
	awaitingOther: "neutral",
	accepted: "success",
	declined: "danger",
	over: "neutral",
};

interface Props {
	meeting: EngagementMeetingSummary;
	viewer: MeetingParty;
	/** The actions that fit the meeting's state; the caller decides which. */
	children?: ReactNode;
}

/**
 * One meeting proposal on an engagement, as both sides see it: when, where,
 * the note, and whose turn it is. A group meeting only says how many people
 * were invited - never who, so a volunteer learns nothing about the others
 * from it.
 */
export default function MeetingDetails({ meeting, viewer, children }: Props) {
	const { t, i18n } = useTranslation();
	const quotes = quoteMarks(i18n.language);
	const state = meetingState(meeting, viewer);
	const isOnline = meeting.format === "Online";
	// Only an online meeting is ever linked; an address stays text even if it
	// happens to look like a URL.
	const link = isOnline ? meetingLink(meeting.location) : null;
	const stateLabel =
		state === "awaitingOther"
			? viewer === "Volunteer"
				? t("meetings.state.awaitingOrganization")
				: t("meetings.state.awaitingVolunteer")
			: t(`meetings.state.${state}`);

	return (
		<div
			data-testid="meeting-details"
			className="mt-2 rounded-xl border border-brand-100 bg-brand-50/60 p-3 text-xs text-gray-700"
		>
			<div className="flex flex-wrap items-center justify-between gap-2">
				<p className="flex items-center gap-1.5 font-medium text-gray-900">
					<CalendarIcon className="h-3.5 w-3.5 shrink-0" />
					<span>
						{t("meetings.scheduledFor", {
							range: formatDateTimeRange(
								meeting.startsAt as unknown as string,
								meeting.endsAt as unknown as string,
								i18n.language,
							),
						})}
					</span>
				</p>
				<Chip tone={STATE_TONES[state]} size="sm">
					{stateLabel}
				</Chip>
			</div>
			<p className="mt-1 flex items-start gap-1.5 break-words">
				{isOnline ? (
					<GlobeIcon className="mt-0.5 h-3.5 w-3.5 shrink-0" />
				) : (
					<MapPinIcon className="mt-0.5 h-3.5 w-3.5 shrink-0" />
				)}
				<span className="sr-only">
					{t(`meetings.form.format.${isOnline ? "Online" : "InPerson"}`)}:{" "}
				</span>
				{link ? (
					<a
						href={link}
						target="_blank"
						rel="noopener noreferrer"
						className={inlineLinkClass}
					>
						{meeting.location}
					</a>
				) : (
					<span>{meeting.location}</span>
				)}
			</p>
			{meeting.participantCount > 1 && (
				<p className="mt-1 flex items-center gap-1.5">
					<UsersIcon className="h-3.5 w-3.5 shrink-0" />
					<span>
						{t("meetings.groupMeeting", { count: meeting.participantCount })}
					</span>
				</p>
			)}
			{meeting.note && (
				<p className="mt-1 italic">
					{quotes.open}
					{meeting.note}
					{quotes.close}
				</p>
			)}
			{children && <div className="mt-2 flex flex-wrap gap-2">{children}</div>}
		</div>
	);
}

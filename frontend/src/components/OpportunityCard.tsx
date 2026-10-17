import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { quoteMarks } from "../lib/quotes";
import type { TFunction } from "i18next";
import {
	findCrossLocaleKeywordMatch,
	formatDate,
	formatDateTime,
	formatOccurrence,
	formatParticipationType,
	isDeadlineImminent,
	pickLocalizedText,
} from "../lib/format";
import Chip, { type ChipTone } from "./Chip";
import OrgAvatar from "./OrgAvatar";
import {
	FEW_SPOTS_THRESHOLD,
	getOpportunityCapacity,
	type OpportunityCapacity,
} from "../lib/opportunityCapacity";
import {
	ArrowsRightLeftIcon,
	CalendarIcon,
	ClockIcon,
	GlobeIcon,
	MapPinIcon,
} from "./icons";
import { CategoryGlyph } from "./VolunteerOpportunitiesList/CategoryGlyph";
import type { ActiveEngagementStatus } from "../lib/engagementStatus";

// `null | undefined` throughout rather than one or the other: the two DTOs
// that satisfy this interface disagree about which they use for an absent
// value (the generated client mirrors the OpenAPI document's nullability,
// which is not the same thing as optionality), and every field here is read
// through a helper that treats both as "not there".
export interface OpportunityCardItem {
	id: string;
	titleDe: string;
	titleEn: string | null | undefined;
	descriptionDe: string | null | undefined;
	descriptionEn: string | null | undefined;
	street: string | null | undefined;
	houseNumber: string | null | undefined;
	zipCode: string | null | undefined;
	city: string | null | undefined;
	isRemote: boolean;
	occurrence: string;
	participationType: string;
	category: string | null | undefined;
	totalMaxParticipants: number | null | undefined;
	currentParticipantCount: number;
	validUntil: Date | null | undefined;
	nextTimeSlotStart: Date | null | undefined;

	organizationId?: string | null;
	organizationName?: string | null;
	organizationLogoUrl?: string | null;
	tags?: string[] | null;
	bannerImageUrl?: string | null;
	membersOnly?: boolean;
}

/**
 * Reports capacity only - how many spots exist and how many are taken.
 * Returns undefined for "interest" opportunities, which have no capacity to
 * count; their participation type is already stated by the always-shown
 * sign-up mechanism chip below, so this position stays free of a category
 * label standing in for a count (#2228).
 */
export function capacityChip(
	capacity: OpportunityCapacity,
	t: TFunction,
): { tone: ChipTone; label: string } | undefined {
	switch (capacity.kind) {
		case "unlimited":
			return { tone: "brand", label: t("opportunities.unlimitedSpots") };
		case "notApplicable":
			return capacity.reason === "interest"
				? undefined
				: { tone: "neutral", label: t("opportunities.noSpotsYet") };
		case "capped":
			if (capacity.isFull) {
				return { tone: "danger", label: t("opportunities.full") };
			}
			return {
				tone: capacity.spotsLeft <= FEW_SPOTS_THRESHOLD ? "warning" : "neutral",
				label: t("opportunities.spotsLeft", { count: capacity.spotsLeft }),
			};
	}
}

/**
 * The viewer's own commitment to this opportunity (#2079). Worded per
 * participation type, because an expression of interest is not an
 * "Eintragung" (see frontend/AGENTS.md's Copy Conventions).
 */
export function viewerEngagementChip(
	status: ActiveEngagementStatus,
	participationType: string,
	t: TFunction,
): { tone: ChipTone; label: string } {
	const isInterest = participationType === "IndividualContact";
	if (status === "Confirmed") {
		return {
			tone: "success",
			label: isInterest
				? t("opportunities.viewerEngagement.interestConfirmed")
				: t("opportunities.viewerEngagement.signedUp"),
		};
	}
	return {
		tone: "warning",
		label: isInterest
			? t("opportunities.viewerEngagement.interestPending")
			: t("opportunities.viewerEngagement.signUpPending"),
	};
}

function dateLine(
	item: OpportunityCardItem,
	t: TFunction,
	language: string,
): {
	kind: "start" | "deadline" | "flexible";
	Icon: typeof CalendarIcon;
	tone: string;
	label: string;
} {
	if (item.nextTimeSlotStart) {
		return {
			kind: "start",
			Icon: CalendarIcon,
			tone: "text-gray-700",
			label: t("opportunities.startsOn", {
				date: formatDateTime(
					item.nextTimeSlotStart as unknown as string,
					language,
				),
			}),
		};
	}

	if (item.validUntil) {
		const validUntil = item.validUntil as unknown as string;
		return {
			kind: "deadline",
			Icon: ClockIcon,
			tone: isDeadlineImminent(validUntil) ? "text-amber-700" : "text-gray-700",
			label: t("opportunities.applyBy", {
				date: formatDate(validUntil, language),
			}),
		};
	}

	return {
		kind: "flexible",
		Icon: ArrowsRightLeftIcon,
		tone: "text-gray-500",
		label: t("opportunities.flexibleDate"),
	};
}

export default function OpportunityCard({
	item,
	headingLevel = 2,
	keyword,
	withMedia = false,
	viewerEngagementStatus,
}: {
	item: OpportunityCardItem;

	headingLevel?: 2 | 3;
	/** The active search keyword, if this card is shown as a search result (#2242). */
	keyword?: string;
	/**
	 * Whether this surface's cards carry a media band. A per-list decision, not
	 * a per-item one: rendering the band only for the items that happen to have
	 * an image gave one grid row two different card anatomies and stretched the
	 * banner-less siblings to the tallest card's height - 365px against the
	 * 209px they needed, ~156px of it empty (#2329 F10). Pass it on the
	 * surfaces whose DTO carries `bannerImageUrl` at all; the leaner
	 * `PublicOpportunitySummaryDto` has no banner field, so those lists would
	 * only ever show placeholders.
	 */
	withMedia?: boolean;
	/**
	 * The signed-in viewer's own engagement state on this opportunity, from
	 * `useMyEngagementStates` - a per-viewer fact neither listing DTO carries
	 * (#2079). Absent for a visitor with no active engagement here.
	 */
	viewerEngagementStatus?: ActiveEngagementStatus;
}) {
	const { t, i18n } = useTranslation();
	const quotes = quoteMarks(i18n.language);
	const Heading = headingLevel === 3 ? "h3" : "h2";
	const capacity = capacityChip(getOpportunityCapacity(item), t);
	const date = dateLine(item, t, i18n.language);
	const DateIcon = date.Icon;
	const viewerEngagement = viewerEngagementStatus
		? viewerEngagementChip(viewerEngagementStatus, item.participationType, t)
		: undefined;
	const title = pickLocalizedText(item.titleDe, item.titleEn, i18n.language);
	const description = pickLocalizedText(
		item.descriptionDe,
		item.descriptionEn,
		i18n.language,
	);

	const isGermanFallback =
		title.lang !== i18n.language ||
		(description !== undefined && description.lang !== i18n.language);
	const hasOrganization = !!item.organizationId && !!item.organizationName;

	const crossLocaleMatch = keyword
		? findCrossLocaleKeywordMatch(
				item.titleDe,
				item.titleEn,
				item.descriptionDe,
				item.descriptionEn,
				item.organizationName ?? "",
				keyword,
				title,
				description,
			)
		: undefined;

	/** Always shown - the one place a card states its participation type (#2228). */
	const signUpMechanismLabel = formatParticipationType(
		item.participationType,
		t,
	);

	return (
		<li className="group relative flex h-full flex-col rounded-card border border-gray-100 bg-white shadow-resting transition-shadow hover:shadow-raised">
			<Link
				to={`/volunteer-opportunities/${item.id}`}
				className="absolute inset-0 z-10 rounded-card"
				aria-label={title.text}
				lang={title.lang}
			/>
			<div className="flex h-full flex-col">
				{withMedia && (
					<div className="relative h-32 w-full shrink-0 overflow-hidden rounded-t-card bg-gradient-to-br from-brand-50 to-brand-100">
						{item.bannerImageUrl ? (
							<img
								src={item.bannerImageUrl}
								alt=""
								width={1200}
								height={480}
								loading="lazy"
								className="absolute inset-0 h-full w-full object-cover transition-transform duration-300 group-hover:scale-105 motion-reduce:transition-none"
							/>
						) : (
							<CategoryGlyph
								category={item.category}
								className="absolute top-1/2 left-1/2 h-12 w-12 -translate-x-1/2 -translate-y-1/2 text-brand-200"
							/>
						)}
					</div>
				)}

				<div className="flex min-w-0 flex-1 flex-col p-4 sm:p-5">
					<div className="mb-2 flex flex-wrap items-center gap-1.5">
						<Chip tone="brand" size="sm" className="shrink-0">
							<CategoryGlyph category={item.category} className="h-3 w-3" />
							{item.category
								? t(`opportunities.category.${item.category}`)
								: t("opportunities.category.Other")}
						</Chip>
						<Chip tone="neutral" size="sm" className="shrink-0">
							{formatOccurrence(item.occurrence, t)}
						</Chip>
						{item.membersOnly && (
							<Chip tone="warning" size="sm" className="shrink-0">
								{t("opportunities.membersOnlyBadge")}
							</Chip>
						)}
						{viewerEngagement && (
							<Chip
								data-testid="opportunity-viewer-engagement"
								tone={viewerEngagement.tone}
								size="sm"
								className="shrink-0"
							>
								{viewerEngagement.label}
							</Chip>
						)}

						<Chip
							data-testid="opportunity-signup-mechanism"
							tone="neutral"
							size="sm"
							className="ml-auto shrink-0"
						>
							{signUpMechanismLabel}
						</Chip>
					</div>

					<Heading
						lang={title.lang}
						className="text-base leading-snug font-semibold text-gray-900 underline-offset-2 transition-colors group-hover:text-brand-700 group-hover:underline sm:text-lg"
					>
						{title.text}
					</Heading>
					{isGermanFallback && (
						<p className="mt-0.5 text-xs text-gray-500">
							{t("opportunities.germanOnlyNotice")}
						</p>
					)}
					{crossLocaleMatch && (
						<p
							data-testid="opportunity-cross-locale-match"
							className="mt-0.5 text-xs text-gray-500"
						>
							{t("opportunities.crossLocaleMatchNotice", {
								language: t(`language.${crossLocaleMatch.lang}`),
							})}{" "}
							<span lang={crossLocaleMatch.lang}>
								{quotes.open}
								{crossLocaleMatch.text}
								{quotes.close}
							</span>
						</p>
					)}

					<div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1">
						<p
							data-testid="opportunity-date-line"
							data-date-kind={date.kind}
							className={`flex items-center gap-1.5 text-sm font-medium ${date.tone}`}
						>
							<DateIcon className="h-4 w-4 shrink-0" />
							<span>{date.label}</span>
						</p>
						{capacity && (
							<Chip
								data-testid="opportunity-capacity"
								tone={capacity.tone}
								size="sm"
								className="shrink-0"
							>
								{capacity.label}
							</Chip>
						)}
					</div>
					{description && (
						<p
							lang={description.lang}
							className="mt-1 line-clamp-2 text-sm leading-relaxed text-gray-500"
						>
							{description.text}
						</p>
					)}
					{item.tags && item.tags.length > 0 && (
						<div className="relative z-20 mt-2 flex flex-wrap gap-1.5">
							{item.tags.map((tag) => (
								<Chip
									key={tag}
									tone="neutral"
									size="sm"
									to={`/opportunities?tag=${encodeURIComponent(tag)}`}
									aria-label={t("opportunities.filterByTag", { tag })}
								>
									{tag}
								</Chip>
							))}
						</div>
					)}
					{hasOrganization ? (
						/* One row, always. `flex-wrap` plus an `ml-auto` location meant
						whether the row wrapped depended purely on the org name's length,
						so two cards side by side in the same grid rendered two different
						footer anatomies (#2329 F7). The org name gives up its width
						instead, which it can: CSS truncation leaves the full name in the
						DOM, so the link's accessible name is unchanged. */
						<div className="mt-auto flex items-center gap-x-3 border-t border-gray-100 pt-3">
							<Link
								to={`/organizations/${item.organizationId}`}
								data-testid="opportunity-org-link"
								className="group/org relative z-20 inline-flex min-w-0 items-center gap-2"
							>
								<OrgAvatar
									name={item.organizationName ?? ""}
									logoUrl={item.organizationLogoUrl}
									size="lg"
									lazy
								/>
								<span className="truncate text-sm font-medium text-gray-600 transition-colors group-hover/org:text-brand-700 group-hover/org:underline">
									{item.organizationName}
								</span>
							</Link>
							{(item.isRemote || item.city) && (
								<span className="ml-auto flex shrink-0 items-center gap-1 text-xs text-gray-500">
									{item.isRemote ? (
										<>
											<GlobeIcon className="h-3.5 w-3.5 shrink-0" />
											<span>{t("opportunities.remote")}</span>
										</>
									) : (
										<>
											<MapPinIcon className="h-3.5 w-3.5 shrink-0" />
											<span>{item.city}</span>
										</>
									)}
								</span>
							)}
						</div>
					) : (
						<div className="mt-auto flex items-start gap-2 pt-2 text-sm text-gray-600">
							{item.isRemote ? (
								<>
									<GlobeIcon className="mt-0.5 h-4 w-4 shrink-0 text-gray-400" />
									<span>{t("opportunities.remote")}</span>
								</>
							) : (
								item.street && (
									<>
										<MapPinIcon className="mt-0.5 h-4 w-4 shrink-0 text-gray-400" />
										<span>
											{item.street} {item.houseNumber}, {item.zipCode}{" "}
											{item.city}
										</span>
									</>
								)
							)}
						</div>
					)}
				</div>
			</div>
		</li>
	);
}

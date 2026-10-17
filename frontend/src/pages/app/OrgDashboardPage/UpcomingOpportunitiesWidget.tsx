import { memo, useMemo, useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { addDays, endOfDay, startOfDay } from "date-fns";
import type { OrganizationCalendarEventDto } from "../../../client";
import { useApiClient } from "../../../hooks/useApiClient";
import Chip from "../../../components/Chip";
import Skeleton from "../../../components/Skeleton";
import ErrorBanner from "../../../components/ErrorBanner";
import EmptyState from "../../../components/EmptyState";
import CreateVolunteerOpportunityModal from "../../../components/CreateVolunteerOpportunityModal";
import WidgetCard from "./WidgetCard";
import SeatsBar from "./SeatsBar";
import { useCachedFetch } from "../../../hooks/useCachedFetch";
import {
	formatDateTimeRange,
	formatSlotSignUpCount,
} from "../../../lib/format";
import { getOpportunityCapacity } from "../../../lib/opportunityCapacity";
import { selectUpcomingSlots } from "../../../lib/upcomingSlots";
import type { WidgetSize } from "./widgetCatalog";

// As far ahead as the calendar's own agenda view looks. Past a month, "what is
// coming up" stops being a thing anyone acts on today and becomes planning,
// which is what the calendar below is for.
const HORIZON_DAYS = 30;

interface Props {
	organizationId: string;
	refreshKey: number;
	size: WidgetSize;
	isOrganizer: boolean;
	onOpportunityCreated: (createdDraftId?: string) => void;
}

/**
 * The next occurrences that still need people, soonest first.
 *
 * It used to list *opportunities* by whichever slot came next, with the whole
 * series' sign-up total beside each one. For anything that repeats - the market
 * stall every Saturday, the weekly soup kitchen - that showed one row where
 * there were twelve mornings to staff, and reported a series that was 8/20 full
 * as healthy while this Saturday sat empty. The unit of work is the occurrence,
 * so that is what the tile lists.
 */
function UpcomingOpportunitiesWidget({
	organizationId,
	refreshKey,
	size,
	isOrganizer,
	onOpportunityCreated,
}: Props) {
	const { t, i18n } = useTranslation();
	const api = useApiClient();
	const [showCreateModal, setShowCreateModal] = useState(false);

	// Anchored to the start of today rather than to "now", so the request key is
	// the same for every render within a day instead of a new key - and a new
	// request - on every tick.
	const { from, to } = useMemo(() => {
		const today = startOfDay(new Date());
		return { from: today, to: endOfDay(addDays(today, HORIZON_DAYS)) };
	}, []);

	const [events, , error] = useCachedFetch<OrganizationCalendarEventDto[]>(
		`upcomingSlots:${organizationId}:${refreshKey}:${from.toISOString()}`,
		() =>
			api.getOrganizationCalendarEvents({
				path: { organizationId },
				query: { from, to },
			}),
	);

	const slots = useMemo(
		() =>
			events
				? selectUpcomingSlots(
						events,
						Date.now(),
						t("orgDashboard.unnamedDraft"),
						i18n.language,
					)
				: null,
		[events, t, i18n.language],
	);

	const narrow = size.width === "compact";

	return (
		<WidgetCard
			titleId="widget-upcoming-title"
			title={t("orgDashboard.upcomingWidgetTitle")}
			footer={
				<Link
					to={`/app/${organizationId}/dashboard/opportunities`}
					className="text-sm font-medium text-brand-700 hover:underline"
				>
					{t("orgDashboard.upcomingViewAll")}
				</Link>
			}
		>
			{slots === null && !error && (
				<div role="status" className="space-y-3">
					<span className="sr-only">{t("orgDashboard.upcomingLoading")}</span>
					{Array.from({ length: 3 }).map((_, i) => (
						<div key={i} aria-hidden="true" className="py-1">
							<Skeleton className="h-4 w-2/3" />
							<Skeleton className="mt-2 h-3 w-1/2" />
						</div>
					))}
				</div>
			)}

			{error && <ErrorBanner message={t("orgDashboard.upcomingError")} />}

			{slots !== null && !error && slots.length === 0 && (
				<EmptyState
					compact
					title={t("orgDashboard.upcomingEmpty")}
					message={t("orgDashboard.upcomingEmptyHint")}
					action={
						isOrganizer
							? {
									label: t("orgDashboard.emptyStateCreateAction"),
									onClick: () => setShowCreateModal(true),
								}
							: undefined
					}
				/>
			)}

			{slots !== null && !error && slots.length > 0 && (
				<ul className="divide-y divide-gray-100">
					{slots.map((slot) => {
						const capacity = getOpportunityCapacity({
							totalMaxParticipants: slot.maxParticipants,
							currentParticipantCount: slot.bookedCount,
						});
						const signUps = formatSlotSignUpCount(capacity, t);

						return (
							<li
								key={slot.id}
								className="relative py-2.5 first:pt-0 last:pb-0"
								data-testid={`upcoming-slot-${slot.id}`}
							>
								<Link
									to={`/app/${organizationId}/dashboard/opportunities/${slot.opportunityId}/engagements`}
									className="absolute inset-0"
									aria-label={`${slot.title}, ${formatDateTimeRange(slot.start, slot.end, i18n.language)}, ${signUps}`}
								/>
								<p
									lang={slot.titleLang}
									className="truncate text-sm font-medium text-gray-900"
								>
									{slot.title}
								</p>
								{slot.membersOnly && (
									<Chip tone="warning" size="sm" className="mt-1">
										{t("opportunities.membersOnlyBadge")}
									</Chip>
								)}
								<div
									className={`mt-0.5 flex gap-x-2 text-xs text-gray-500 ${
										narrow
											? "flex-col items-start gap-y-1"
											: "flex-wrap items-center"
									}`}
								>
									<span className="tabular-nums">
										{formatDateTimeRange(slot.start, slot.end, i18n.language)}
									</span>
									<span className="flex items-center gap-1.5">
										<SeatsBar capacity={capacity} />
										<span className="tabular-nums">{signUps}</span>
									</span>
								</div>
							</li>
						);
					})}
				</ul>
			)}

			{showCreateModal && (
				<CreateVolunteerOpportunityModal
					organizationId={organizationId}
					onClose={() => setShowCreateModal(false)}
					onSuccess={onOpportunityCreated}
				/>
			)}
		</WidgetCard>
	);
}

export default memo(UpcomingOpportunitiesWidget);

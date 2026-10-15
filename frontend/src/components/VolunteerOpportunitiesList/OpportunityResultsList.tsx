import { useTranslation } from "react-i18next";
import type { VolunteerOpportunitySummary } from "../../client";
import EmptyState from "../EmptyState";
import OpportunityCardSkeleton from "../OpportunityCardSkeleton";
import LoadMoreError from "../LoadMoreError";
import LoadMoreButton from "../LoadMoreButton";
import RouteState from "../RouteState";
import OpportunityCard, { type OpportunityCardItem } from "../OpportunityCard";
import { useMyEngagementStates } from "../../hooks/useMyEngagementStates";

/** Widens the summary's nullable fields to the card's item shape, which
 * reads `undefined`. */
function toCardItem(item: VolunteerOpportunitySummary): OpportunityCardItem {
	return {
		...item,
		titleEn: item.titleEn ?? undefined,
		descriptionDe: item.descriptionDe ?? undefined,
		descriptionEn: item.descriptionEn ?? undefined,
		street: item.street ?? undefined,
		houseNumber: item.houseNumber ?? undefined,
		zipCode: item.zipCode ?? undefined,
		city: item.city ?? undefined,
		category: item.category ?? undefined,
		validUntil: item.validUntil ?? undefined,
		nextTimeSlotStart: item.nextTimeSlotStart ?? undefined,
		totalMaxParticipants: item.totalMaxParticipants ?? undefined,
		bannerImageUrl: item.bannerImageUrl ?? undefined,
		organizationLogoUrl: item.organizationLogoUrl ?? undefined,
	};
}

export default function OpportunityResultsList({
	loading,
	error,
	errorIsOffline,
	items,
	totalItems,
	hasFilters,
	onClearFilters,
	hasMore,
	loadingMore,
	onLoadMore,
	loadMoreError,
	loadMoreErrorIsOffline,
	onRetryLoadMore,
	keyword,
	pageSize,
}: {
	loading: boolean;
	error: string | null;
	errorIsOffline: boolean;
	items: VolunteerOpportunitySummary[];
	totalItems: number | undefined;
	hasFilters: boolean;
	onClearFilters: () => void;
	hasMore: boolean;
	loadingMore: boolean;
	onLoadMore: () => void;
	loadMoreError: string | null;
	loadMoreErrorIsOffline: boolean;
	onRetryLoadMore: () => void;
	keyword?: string;
	/** How many cards a page holds, so the placeholders occupy the space the
	 * results will (#2329 F6) - three of them stood in for a nine-card page. */
	pageSize: number;
}) {
	const { t } = useTranslation();
	const engagementStates = useMyEngagementStates();

	const isInitialLoad = loading && items.length === 0;
	const countMessage =
		!error && !isInitialLoad && typeof totalItems === "number"
			? t("opportunities.resultCount", { count: totalItems })
			: "";

	const liveMessage =
		error && errorIsOffline
			? `${t("routeState.offline.title")}. ${t("opportunities.offline")}`
			: countMessage;

	return (
		<>
			<p
				role="status"
				data-testid="opportunities-live-region"
				className="sr-only"
			>
				{liveMessage}
			</p>

			<h2 className="sr-only">{t("opportunities.resultsHeading")}</h2>
			{loading && items.length === 0 && (
				<div
					role="status"
					className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3"
				>
					<span className="sr-only">{t("opportunities.loading")}</span>
					{Array.from({ length: pageSize }).map((_, i) => (
						<OpportunityCardSkeleton key={i} withMedia />
					))}
				</div>
			)}

			{error &&
				(errorIsOffline ? (
					<RouteState
						inline
						variant="offline"
						title={t("routeState.offline.title")}
						message={t("opportunities.offline")}
						onRetry={onRetryLoadMore}
						data-testid="opportunities-offline"
					/>
				) : (
					<LoadMoreError
						message={t("opportunities.error", { message: error })}
						retrying={loading}
						onRetry={onRetryLoadMore}
						data-testid="opportunities-error"
					/>
				))}

			{!error && (
				<>
					{!loading && items.length === 0 ? (
						<EmptyState
							title={t("opportunities.noResults")}
							message={
								hasFilters ? t("opportunities.noResultsWithFilters") : undefined
							}
							action={
								hasFilters
									? {
											label: t("opportunities.clearFilters"),
											onClick: onClearFilters,
										}
									: undefined
							}
						/>
					) : (
						<ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
							{items.map((item: VolunteerOpportunitySummary) => (
								<OpportunityCard
									key={item.id}
									item={toCardItem(item)}
									headingLevel={3}
									keyword={keyword}
									withMedia
									viewerEngagementStatus={engagementStates.get(item.id)}
								/>
							))}
						</ul>
					)}

					{items.length > 0 &&
						hasMore &&
						(loadMoreError ? (
							loadMoreErrorIsOffline ? (
								<RouteState
									inline
									variant="offline"
									title={t("routeState.offline.title")}
									message={t("opportunities.offlineLoadMore")}
									onRetry={onRetryLoadMore}
									data-testid="opportunities-offline-load-more"
								/>
							) : (
								<LoadMoreError
									message={t("opportunities.error", { message: loadMoreError })}
									retrying={loadingMore}
									onRetry={onRetryLoadMore}
								/>
							)
						) : (
							<>
								{typeof totalItems === "number" && (
									<p
										role="status"
										data-testid="opportunities-load-more-progress"
										className="sr-only"
									>
										{t("opportunities.loadedOfTotal", {
											loaded: items.length,
											total: totalItems,
										})}
									</p>
								)}
								<LoadMoreButton
									loading={loadingMore}
									label={t("opportunities.loadMore")}
									loadingLabel={t("opportunities.loading")}
									onClick={onLoadMore}
								/>
							</>
						))}
				</>
			)}
		</>
	);
}

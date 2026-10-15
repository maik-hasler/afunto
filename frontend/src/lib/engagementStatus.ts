export const ENGAGEMENT_STATUS_COLORS: Record<string, string> = {
	Pending: "bg-yellow-50 text-yellow-700 border-yellow-100",
	Confirmed: "bg-green-50 text-green-700 border-green-100",
	Cancelled: "bg-red-50 text-red-700 border-red-100",
	Withdrawn: "bg-gray-100 text-gray-700 border-gray-200",
};

export function isTerminalEngagementStatus(status: string): boolean {
	return status === "Cancelled" || status === "Withdrawn";
}

/** The statuses that still commit a volunteer to an opportunity. */
export type ActiveEngagementStatus = "Pending" | "Confirmed";

/**
 * Collapses a volunteer's engagements to one status per opportunity, so a
 * browse card can say whether the viewer is already committed to it (#2079).
 * Terminal engagements are dropped. A recurring series can hold several
 * engagements on one opportunity; a confirmed one outranks a pending one,
 * because the card states the strongest commitment the viewer holds.
 */
export function activeEngagementStatusByOpportunity(
	engagements: readonly { opportunityId: string; status: string }[],
): Map<string, ActiveEngagementStatus> {
	const byOpportunity = new Map<string, ActiveEngagementStatus>();
	for (const { opportunityId, status } of engagements) {
		if (status !== "Pending" && status !== "Confirmed") continue;
		if (byOpportunity.get(opportunityId) === "Confirmed") continue;
		byOpportunity.set(opportunityId, status);
	}
	return byOpportunity;
}

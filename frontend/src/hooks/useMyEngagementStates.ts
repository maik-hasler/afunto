import { useCallback, useMemo } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";
import {
	activeEngagementStatusByOpportunity,
	type ActiveEngagementStatus,
} from "../lib/engagementStatus";
import { queryKeys } from "../lib/queryKeys";
import { useApiClient } from "./useApiClient";

// The endpoint's own page-size ceiling. A volunteer with more upcoming
// engagements than this sees no chip on the opportunities past the first
// page - a deliberate trade against paging through `/me/engagements` on every
// browse surface for a case nobody is near.
const PAGE_SIZE = 100;

const NONE: ReadonlyMap<string, ActiveEngagementStatus> = new Map();

/**
 * Invalidates the caller's engagement states. Every mutation that creates an
 * engagement or ends one has to call this, or a browse card keeps showing the
 * viewer's previous state for a full `staleTime`.
 */
export function useInvalidateMyEngagementStates(): () => Promise<void> {
	const queryClient = useQueryClient();
	return useCallback(
		() =>
			queryClient.invalidateQueries({
				queryKey: queryKeys.engagements.all,
			}),
		[queryClient],
	);
}

/**
 * The signed-in viewer's own engagement state per opportunity, for the
 * browse cards (#2079). Read from `/me/engagements` rather than added to the
 * listing DTO: the listing is anonymous and shares one output cache across
 * every caller, which a per-viewer field would have to fragment.
 *
 * Signed out, or until the request settles, the map is empty - a card then
 * simply shows no chip.
 */
export function useMyEngagementStates(): ReadonlyMap<
	string,
	ActiveEngagementStatus
> {
	const auth = useAuth();
	const api = useApiClient();
	const isLoggedIn = auth.isAuthenticated;

	const { data } = useQuery({
		queryKey: queryKeys.engagements.mineUpcoming(),
		queryFn: async () => {
			const page = await api.getMyEngagements({
				query: { PageNumber: 1, PageSize: PAGE_SIZE, Upcoming: true },
			});
			return page.items.map(({ opportunityId, status }) => ({
				opportunityId,
				status,
			}));
		},
		enabled: isLoggedIn,
	});

	return useMemo(
		() =>
			isLoggedIn && data ? activeEngagementStatusByOpportunity(data) : NONE,
		[isLoggedIn, data],
	);
}

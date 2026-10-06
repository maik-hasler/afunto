import { useEffect } from "react";
import { useAuth } from "react-oidc-context";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { useApiClient } from "./useApiClient";
import { dispatchToast } from "../lib/toastBus";
import { queryKeys } from "../lib/queryKeys";
import { NOTIFICATION_POLL_INTERVAL_MS } from "../lib/pollIntervals";

const SEEN_KEY_PREFIX = "afunto:seen-achievements";

const SEEDED_MARKER = "__seeded__";

function seenKeyFor(userId: string | undefined): string {
	return `${SEEN_KEY_PREFIX}:${userId ?? "anonymous"}`;
}

function getSeenIds(key: string): Set<string> {
	try {
		const raw = localStorage.getItem(key);
		return raw ? new Set(JSON.parse(raw) as string[]) : new Set();
	} catch {
		return new Set();
	}
}

function markSeen(key: string, ids: string[]): void {
	try {
		const seen = getSeenIds(key);
		ids.forEach((id) => seen.add(id));
		localStorage.setItem(key, JSON.stringify([...seen]));
	} catch {
		// ignore storage errors
	}
}

export function clearSeenAchievements(userId: string | undefined): void {
	try {
		localStorage.removeItem(seenKeyFor(userId));
	} catch {
		// ignore storage errors
	}
}

export function useAchievementNotifier() {
	const auth = useAuth();
	const api = useApiClient();
	const { t } = useTranslation();

	const userId = auth.user?.profile?.sub;

	// Polling used to be 40 lines of setInterval, a visibilitychange listener and
	// an AbortController here, and the same 40 lines again in useAccountMenu -
	// both mounted on every authenticated page. `refetchInterval` already stops
	// while the tab is hidden, which is what the listener was for.
	const { data: achievements, error } = useQuery({
		queryKey: queryKeys.profile.achievements(),
		queryFn: ({ signal }) => api.getMyAchievements({ signal }),
		enabled: auth.isAuthenticated,
		refetchInterval: NOTIFICATION_POLL_INTERVAL_MS,
		staleTime: NOTIFICATION_POLL_INTERVAL_MS,
	});

	// Structural sharing keeps `achievements` referentially stable while the
	// answer does not change, so this runs on a poll that actually brought
	// something new - not once a minute. `markSeen` makes it idempotent either
	// way, which is what stops a re-render announcing the same badge twice.
	useEffect(() => {
		if (!achievements) return;
		const key = seenKeyFor(userId);
		const seen = getSeenIds(key);

		// The first poll for a browser records what the user already had without
		// announcing it - otherwise signing in on a new device would fire a toast
		// per badge earned over months.
		if (!seen.has(SEEDED_MARKER)) {
			markSeen(key, [...achievements.map((a) => a.id), SEEDED_MARKER]);
			return;
		}

		const newOnes = achievements.filter((a) => !seen.has(a.id));
		if (newOnes.length === 0) return;

		markSeen(
			key,
			newOnes.map((a) => a.id),
		);
		newOnes.forEach((a) =>
			dispatchToast(
				"success",
				t("achievements.newBadge", {
					name: a.key
						? t(`achievements.badges.${a.key}.name`, { defaultValue: a.name })
						: a.name,
				}),
			),
		);
	}, [achievements, userId, t]);

	useEffect(() => {
		if (error) console.error("[useAchievementNotifier] poll failed:", error);
	}, [error]);
}

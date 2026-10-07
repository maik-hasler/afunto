import { useEffect, useState } from "react";
import type { NotificationPreferencesResponse } from "../../client";
import { useApiClient } from "../../hooks/useApiClient";

export type PreferenceKey =
	| "notifyOnNewSignUp"
	| "notifyOnWithdrawal"
	| "notifyOnEngagementConfirmed"
	| "notifyOnEngagementReminder";

export function useNotificationPreferencesForm() {
	const api = useApiClient();
	const [preferences, setPreferences] =
		useState<NotificationPreferencesResponse | null>(null);
	const [original, setOriginal] =
		useState<NotificationPreferencesResponse | null>(null);
	const [loading, setLoading] = useState(true);
	const [loadError, setLoadError] = useState(false);

	useEffect(() => {
		let cancelled = false;
		api
			.getNotificationPreferences({})
			.then((data) => {
				if (cancelled) return;
				setPreferences(data);
				setOriginal(data);
				setLoadError(false);
			})
			.catch(() => {
				if (!cancelled) setLoadError(true);
			})
			.finally(() => {
				if (!cancelled) setLoading(false);
			});
		return () => {
			cancelled = true;
		};
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, []);

	function toggle(key: PreferenceKey) {
		setPreferences((prev) => (prev ? { ...prev, [key]: !prev[key] } : prev));
	}

	function reset() {
		setPreferences(original);
	}

	function commit(saved: NotificationPreferencesResponse) {
		setPreferences(saved);
		setOriginal(saved);
	}

	return { preferences, loading, loadError, toggle, reset, commit };
}

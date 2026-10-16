import { useEffect, useRef } from "react";
import { useAuth } from "react-oidc-context";
import { subscribeSessionExpired } from "../lib/sessionExpiryBus";
import { hasExhaustedAuthRecovery } from "../lib/authRecovery";
import {
	useSetSessionExpiredFlag,
	useSetAuthRecoveryFailedFlag,
} from "../contexts/AuthStatusContext";

/**
 * Marks the session as over once neither the token nor a silent renewal is
 * accepted any more - by then the Keycloak session itself has ended, usually
 * after a long idle stretch.
 *
 * Deliberately quiet: no toast and no automatic redirect to Keycloak (#2392).
 * Both fired a few seconds after someone came back to an idle tab, took the
 * page away mid-read and spoke of "sessions" to people who never asked for
 * one. The header's sign-in button is the one place that says it now, and
 * the page stays where it is until they press it.
 */
export function useSessionExpiryHandler() {
	const auth = useAuth();
	const setSessionExpired = useSetSessionExpiredFlag();
	const setAuthRecoveryFailed = useSetAuthRecoveryFailedFlag();
	const handledRef = useRef(false);
	const authRef = useRef(auth);
	authRef.current = auth;

	useEffect(() => {
		handledRef.current = false;
	}, [auth.isAuthenticated]);

	useEffect(() => {
		function handleExpiry() {
			if (!authRef.current.isAuthenticated) return;

			if (handledRef.current) return;
			handledRef.current = true;

			// Bounded recovery (#2208): signing in again only counts as having
			// worked once an authenticated API call actually succeeds (cleared
			// in api-instance.ts) - so an expiry after the user already signed
			// in again, without one in between, means signing in isn't fixing
			// anything (e.g. a ValidIssuers mismatch) and the user would
			// otherwise loop forever.
			if (hasExhaustedAuthRecovery()) {
				setAuthRecoveryFailed(true);
				return;
			}

			setSessionExpired(true);
		}

		const unsubscribeBus = subscribeSessionExpired(handleExpiry);
		const unsubscribeSilentRenewError =
			auth.events.addSilentRenewError(handleExpiry);

		return () => {
			unsubscribeBus();
			unsubscribeSilentRenewError();
		};
	}, [auth, setSessionExpired, setAuthRecoveryFailed]);
}

const STORAGE_KEY = "afunto:auth-recovery";

// One sign-in round trip is allowed per recovery episode. The header's
// "Sign in again" button records it; an expiry after that, without a
// successful authenticated call landing in between, means signing in again
// didn't fix anything (e.g. a ValidIssuers mismatch), so offering it forever
// would just loop the user through Keycloak indefinitely (#2208). Counting
// sign-ins rather than expiries matters since nothing redirects on its own
// any more (#2392): a reload that meets the same rejected token is a second
// expiry, but not a second attempt. The count lives in sessionStorage because
// signing in is a full-page navigation that would otherwise wipe any
// in-memory guard.
export const AUTH_RECOVERY_REDIRECT_LIMIT = 1;

function readAttempts(): number {
	try {
		const raw = window.sessionStorage.getItem(STORAGE_KEY);
		const parsed = raw === null ? 0 : Number.parseInt(raw, 10);
		return Number.isInteger(parsed) && parsed >= 0 ? parsed : 0;
	} catch {
		return 0;
	}
}

export function hasExhaustedAuthRecovery(): boolean {
	return readAttempts() >= AUTH_RECOVERY_REDIRECT_LIMIT;
}

export function recordAuthRecoveryAttempt(): number {
	const attempts = readAttempts() + 1;
	try {
		window.sessionStorage.setItem(STORAGE_KEY, String(attempts));
	} catch {
		// Storage unavailable (e.g. private browsing with storage blocked) -
		// the count can't persist, so the bound can't be enforced either;
		// that's no worse than the unbounded loop this module exists to fix.
	}
	return attempts;
}

export function clearAuthRecoveryAttempts(): void {
	try {
		window.sessionStorage.removeItem(STORAGE_KEY);
	} catch {
		// ignore
	}
}

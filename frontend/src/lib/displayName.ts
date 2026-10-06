/**
 * The header pill names the signed-in user from the OIDC id_token, and
 * Keycloak only re-issues that on the next sign-in. A name changed on
 * /profile therefore left the pill showing the old initials indefinitely -
 * two different identities in one viewport, surviving a reload (#2330).
 *
 * The profile page publishes the saved name here and the header prefers it
 * over the stale claim. sessionStorage carries it across reloads until a
 * fresh token catches up; it is scoped to the subject it was saved for, so a
 * different account signing in on the same tab never inherits it.
 */

const STORAGE_KEY = "afunto.display-name";

interface StoredName {
	sub: string;
	name: string;
}

const listeners = new Set<() => void>();

// useSyncExternalStore compares snapshots by identity, so the parsed value is
// cached rather than re-read (and re-allocated) on every render.
let cache: StoredName | null = null;
let cacheLoaded = false;

function read(): StoredName | null {
	if (cacheLoaded) return cache;
	cacheLoaded = true;
	try {
		const raw = sessionStorage.getItem(STORAGE_KEY);
		const parsed: unknown = raw ? JSON.parse(raw) : null;
		cache =
			parsed &&
			typeof parsed === "object" &&
			typeof (parsed as StoredName).sub === "string" &&
			typeof (parsed as StoredName).name === "string"
				? (parsed as StoredName)
				: null;
	} catch {
		// Private mode, a disabled store or malformed JSON: the token claim is
		// still there to fall back on.
		cache = null;
	}
	return cache;
}

function emit(): void {
	for (const listener of listeners) listener();
}

/** The saved name for this subject, or `null` to fall back to the token. */
export function getDisplayNameOverride(sub: string | undefined): string | null {
	if (!sub) return null;
	const stored = read();
	return stored?.sub === sub ? stored.name : null;
}

export function setDisplayNameOverride(sub: string, name: string): void {
	const trimmed = name.trim();
	if (!trimmed) {
		clearDisplayNameOverride();
		return;
	}

	cache = { sub, name: trimmed };
	cacheLoaded = true;
	try {
		sessionStorage.setItem(STORAGE_KEY, JSON.stringify(cache));
	} catch {
		// The in-memory value still fixes the current tab.
	}
	emit();
}

export function clearDisplayNameOverride(): void {
	cache = null;
	cacheLoaded = true;
	try {
		sessionStorage.removeItem(STORAGE_KEY);
	} catch {
		// Nothing to do - the in-memory value is already gone.
	}
	emit();
}

export function subscribeDisplayName(listener: () => void): () => void {
	listeners.add(listener);
	return () => listeners.delete(listener);
}

/**
 * The bearer token the API client sends, held outside React.
 *
 * `react-oidc-context` owns the token and hands it out through `useAuth()`,
 * which only a component can call. The API client is a module: it has to be
 * able to read the current token from a plain function at request time.
 *
 * `hooks/useApiClient.ts` is the single writer, and it writes during render -
 * before the effect that fires the request runs, and before any handler the
 * rendered tree installs. An effect would be too late on the first render
 * after a sign-in: child effects run before their parents', so a page's own
 * fetch would go out with the previous token.
 *
 * Deliberately a plain module variable rather than a store with subscribers:
 * nothing needs to react to a token change, only to read the latest value at
 * the moment a request is built.
 */
let accessToken: string | undefined;

export function setAccessToken(token: string | undefined): void {
	accessToken = token;
}

export function getAccessToken(): string | undefined {
	return accessToken;
}

let refresher: (() => Promise<string | undefined>) | undefined;
let refreshInFlight: Promise<string | undefined> | undefined;

/**
 * How to get a new token without leaving the page - `hooks/useApiClient.ts`
 * installs `signinSilent()` here, for the same reason it is the token's writer.
 */
export function setAccessTokenRefresher(
	refresh: (() => Promise<string | undefined>) | undefined,
): void {
	refresher = refresh;
}

/**
 * Fetches a fresh token silently, or resolves `undefined` when that fails.
 *
 * The API answers 401 to a token issued before the user's permissions changed
 * (a role granted or taken away, #2395/#2407), and a fresh token carries the
 * current roles. Parallel requests that all hit that 401 share one attempt
 * rather than each starting its own round trip to Keycloak.
 */
export function refreshAccessToken(): Promise<string | undefined> {
	if (!refresher) return Promise.resolve(undefined);

	refreshInFlight ??= refresher()
		.then((token) => {
			if (token) setAccessToken(token);
			return token;
		})
		.catch(() => undefined)
		.finally(() => {
			refreshInFlight = undefined;
		});

	return refreshInFlight;
}

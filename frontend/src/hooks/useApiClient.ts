import { useAuth } from "react-oidc-context";
import { setAccessToken, setAccessTokenRefresher } from "../lib/accessToken";
import { api, type ApiClient } from "../client";

/**
 * The generated API client, with the current bearer token in place.
 *
 * The client itself is a module, not an instance - what this hook adds is the
 * token. It writes it during render rather than from an effect on purpose:
 * child effects run before their parents', so a page that fetches from its own
 * effect would otherwise go out with the previous render's token on the first
 * render after a sign-in. Every caller reaches the API through this hook, so
 * every request is preceded by the write that makes it current. The write is
 * idempotent and the newest token is always the right one, which is what makes
 * it safe to do here (see lib/accessToken.ts).
 *
 * The return value is stable, so it never belongs in a dependency array as a
 * refetch trigger - depend on whatever actually changed.
 */
export function useApiClient(): ApiClient {
	const { user, signinSilent } = useAuth();
	setAccessToken(user?.access_token);
	setAccessTokenRefresher(
		user ? async () => (await signinSilent())?.access_token : undefined,
	);
	return api;
}

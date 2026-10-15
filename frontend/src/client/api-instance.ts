import { dispatchToast } from "../lib/toastBus";
import { notifySessionExpired } from "../lib/sessionExpiryBus";
import { clearAuthRecoveryAttempts } from "../lib/authRecovery";
import { runtimeConfig } from "../lib/runtimeConfig";
import { getAccessToken, refreshAccessToken } from "../lib/accessToken";
import i18next from "../i18n";
import type { CreateClientConfig } from "./generated/client.gen";

// Fallback wait when the server doesn't send Retry-After - matches the
// default rate-limit window (RateLimitingOptions.ReadOptions.WindowSeconds).
const DEFAULT_RATE_LIMIT_WAIT_SECONDS = 60;

// A single burst (several parallel requests against an already-exhausted
// bucket) would otherwise fire one identical toast per failed request.
// Track how long the last announcement is still valid for and stay quiet
// until then instead - the message itself already tells the user when it's
// safe to retry, so re-announcing sooner would just be noise (#2208).
let rateLimitSuppressedUntil = 0;

function parseRetryAfterSeconds(headerValue: string | null): number | null {
	if (!headerValue) return null;
	const seconds = Number.parseInt(headerValue, 10);
	return Number.isFinite(seconds) && seconds >= 0 ? seconds : null;
}

function handleRateLimited(response: Response): void {
	const now = Date.now();
	if (now < rateLimitSuppressedUntil) return;

	const retryAfterSeconds = parseRetryAfterSeconds(
		response.headers.get("Retry-After"),
	);
	rateLimitSuppressedUntil =
		now + (retryAfterSeconds ?? DEFAULT_RATE_LIMIT_WAIT_SECONDS) * 1000;

	dispatchToast(
		"error",
		retryAfterSeconds !== null
			? i18next.t("error.rateLimited", { count: retryAfterSeconds })
			: i18next.t("error.rateLimitedGeneric"),
	);
}

export async function handleErrorResponse(
	response: Response,
	hadAccessToken: boolean,
): Promise<void> {
	if (response.ok) {
		// Proves the token this client is using is actually accepted by the
		// backend - the one signal strong enough to end a recovery episode
		// (see useSessionExpiryHandler.ts and lib/authRecovery.ts for why
		// completing the Keycloak redirect alone isn't enough, #2208).
		if (hadAccessToken) clearAuthRecoveryAttempts();
		return;
	}

	if (response.status === 401) {
		if (hadAccessToken) {
			notifySessionExpired();
		}
		return;
	}

	if (response.status === 403) {
		dispatchToast("error", i18next.t("error.forbidden"));
		return;
	}

	if (response.status === 429) {
		handleRateLimited(response);
		return;
	}

	if (response.status >= 500) {
		let detail = i18next.t("error.serverError");
		try {
			const clone = response.clone();
			const body = await clone.json();
			if (body?.detail) detail = body.detail;
		} catch {
			// ignore parse errors
		}
		dispatchToast("error", detail);
		console.error("[API] Server error", response.status, response.url);
	}
}

/**
 * Guarantees that whatever the generated client throws carries an HTTP status.
 *
 * The client throws the parsed error body when it is JSON, and the raw text
 * when it is not. The backend's own failures are always `ProblemDetails`, which
 * carries `status` - but a failure in front of it (an nginx 502, a gateway
 * timeout) answers with HTML, and `lib/apiError.ts` reads a missing status as
 * "no response at all" and shows the offline state. Re-wrapping a non-JSON
 * error body as a minimal ProblemDetails keeps that classification honest.
 */
async function withJsonErrorBody(response: Response): Promise<Response> {
	if (response.headers.get("Content-Type")?.includes("json")) return response;

	const detail = await response
		.clone()
		.text()
		.catch(() => "");

	return new Response(
		JSON.stringify({ status: response.status, detail: detail.slice(0, 500) }),
		{
			status: response.status,
			statusText: response.statusText,
			headers: { "Content-Type": "application/problem+json" },
		},
	);
}

function send(
	input: RequestInfo | URL,
	init: RequestInit | undefined,
	accessToken: string | undefined,
): Promise<Response> {
	const headers = new Headers(
		input instanceof Request ? input.headers : init?.headers,
	);
	if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);
	headers.set("X-Timezone", Intl.DateTimeFormat().resolvedOptions().timeZone);
	headers.set("X-Language", i18next.language.split("-")[0]);

	return globalThis.fetch(input, { ...init, headers });
}

/**
 * Called once by the generated `client.gen.ts` to build the client's config.
 *
 * Everything the app needs on top of plain `fetch` is here, in one place, for
 * the same reason it was before: a 401 has to reach the session-expiry handler
 * and a 5xx has to reach a toast no matter which of the hundred endpoints
 * produced it, and a per-call-site version of that would be a hundred chances
 * to forget.
 *
 * A `fetch` override rather than the client's request/response interceptors:
 * `client.gen.ts` imports this module, so importing the client back from here
 * to attach interceptors would close a cycle (`pnpm check:deps` would say so).
 */
export const createClientConfig: CreateClientConfig = (config) => ({
	...config,
	baseUrl: runtimeConfig.apiUrl,
	throwOnError: true,
	fetch: async (input: RequestInfo | URL, init?: RequestInit) => {
		// A Request's body can be read once; the retry below needs its own copy.
		const retryInput = input instanceof Request ? input.clone() : input;

		const accessToken = getAccessToken();
		let response = await send(input, init, accessToken);

		// The API rejects a token issued before the user's permissions changed
		// (#2395, #2403, #2407). A silently fetched one carries the current
		// roles, so the request succeeds without the user noticing; only when
		// that fails too is the session really over.
		if (response.status === 401 && accessToken) {
			const renewedToken = await refreshAccessToken();
			if (renewedToken && renewedToken !== accessToken)
				response = await send(retryInput, init, renewedToken);
		}

		await handleErrorResponse(response, Boolean(accessToken));

		return response.ok ? response : withJsonErrorBody(response);
	},
});

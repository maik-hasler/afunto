import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

const dispatchToast = vi.fn();
const notifySessionExpired = vi.fn();
const clearAuthRecoveryAttempts = vi.fn();
const t = vi.fn((key: string, options?: Record<string, unknown>) =>
	options ? `${key}:${JSON.stringify(options)}` : key,
);

vi.mock("../lib/toastBus", () => ({
	dispatchToast: (level: string, message: string) =>
		dispatchToast(level, message),
}));
vi.mock("../lib/sessionExpiryBus", () => ({
	notifySessionExpired: () => notifySessionExpired(),
}));
vi.mock("../lib/authRecovery", () => ({
	clearAuthRecoveryAttempts: () => clearAuthRecoveryAttempts(),
}));
vi.mock("../lib/runtimeConfig", () => ({
	runtimeConfig: { apiUrl: "https://api.test" },
}));
const refreshAccessToken = vi.fn<() => Promise<string | undefined>>();
vi.mock("../lib/accessToken", () => ({
	getAccessToken: () => accessToken,
	refreshAccessToken: () => refreshAccessToken(),
}));
vi.mock("../i18n", () => ({
	// A rest/spread forward (rather than a fixed (key, options) signature)
	// preserves whether the real call site actually passed a second
	// argument - toHaveBeenCalledWith is exact about arity, and the source
	// calls t() with just one argument in the no-Retry-After-header path.
	default: {
		t: (...args: Parameters<typeof t>) => t(...args),
		// The fetch wrapper reads this for the X-Language header. Regional
		// variants are deliberate here: the header carries the base tag only.
		get language() {
			return language;
		},
	},
}));

let accessToken: string | undefined;
let language = "de-DE";

function makeResponse(status: number, headers: Record<string, string> = {}) {
	return new Response(null, { status, headers });
}

async function loadCreateClientConfig() {
	vi.resetModules();
	const module = await import("./api-instance");
	return module.createClientConfig;
}

/** The `fetch` the client config installs, with `globalThis.fetch` stubbed. */
async function loadFetch(response: Response) {
	const createClientConfig = await loadCreateClientConfig();
	const underlying = vi.fn().mockResolvedValue(response);
	vi.stubGlobal("fetch", underlying);
	const config = createClientConfig({});
	return { fetch: config.fetch as typeof globalThis.fetch, underlying };
}

function sentHeaders(underlying: ReturnType<typeof vi.fn>): Headers {
	return new Headers(
		(underlying.mock.calls[0][1] as RequestInit).headers as HeadersInit,
	);
}

// rateLimitSuppressedUntil is module-level state in api-instance.ts, scoped
// to a single recovery episode by design (see the comment there) - reset
// the module between tests so that state doesn't leak across cases (see
// frontend/AGENTS.md's vi.resetModules() convention for module singletons).
async function loadHandleErrorResponse() {
	vi.resetModules();
	const module = await import("./api-instance");
	return module.handleErrorResponse;
}

beforeEach(() => {
	vi.clearAllMocks();
	accessToken = undefined;
	language = "de-DE";
});

afterEach(() => {
	vi.useRealTimers();
	vi.unstubAllGlobals();
});

describe("handleErrorResponse", () => {
	it("does nothing for a successful anonymous response", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(200), false);
		expect(clearAuthRecoveryAttempts).not.toHaveBeenCalled();
		expect(dispatchToast).not.toHaveBeenCalled();
	});

	it("clears the auth-recovery counter on a successful authenticated response", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(200), true);
		expect(clearAuthRecoveryAttempts).toHaveBeenCalledTimes(1);
	});

	it("notifies session expiry on a 401 that carried an access token", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(401), true);
		expect(notifySessionExpired).toHaveBeenCalledTimes(1);
	});

	it("ignores a 401 with no access token attached", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(401), false);
		expect(notifySessionExpired).not.toHaveBeenCalled();
	});

	it("toasts the forbidden message on 403", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(403), true);
		expect(dispatchToast).toHaveBeenCalledWith("error", "error.forbidden");
	});

	it("toasts a count-aware message using Retry-After on 429", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(429, { "Retry-After": "30" }), true);
		expect(t).toHaveBeenCalledWith("error.rateLimited", { count: 30 });
		expect(dispatchToast).toHaveBeenCalledTimes(1);
	});

	it("falls back to a generic message when Retry-After is missing on 429", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(429), true);
		expect(t).toHaveBeenCalledWith("error.rateLimitedGeneric");
	});

	it("falls back to a generic message when Retry-After is not a valid number", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(
			makeResponse(429, { "Retry-After": "not-a-number" }),
			true,
		);
		expect(t).toHaveBeenCalledWith("error.rateLimitedGeneric");
	});

	it("suppresses a repeat 429 toast within the same backoff window", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(429, { "Retry-After": "30" }), true);
		await handleErrorResponse(makeResponse(429, { "Retry-After": "30" }), true);
		expect(dispatchToast).toHaveBeenCalledTimes(1);
	});

	it("announces again once the backoff window has passed", async () => {
		vi.useFakeTimers();
		const handleErrorResponse = await loadHandleErrorResponse();

		await handleErrorResponse(makeResponse(429, { "Retry-After": "1" }), true);
		vi.advanceTimersByTime(1100);
		await handleErrorResponse(makeResponse(429, { "Retry-After": "1" }), true);

		expect(dispatchToast).toHaveBeenCalledTimes(2);
	});

	it("dispatches a server error toast on 5xx", async () => {
		const handleErrorResponse = await loadHandleErrorResponse();
		await handleErrorResponse(makeResponse(503), true);
		expect(dispatchToast).toHaveBeenCalledWith("error", "error.serverError");
	});
});

describe("the client's fetch", () => {
	it("points the client at the runtime-configured API origin", async () => {
		const createClientConfig = await loadCreateClientConfig();

		expect(createClientConfig({}).baseUrl).toBe("https://api.test");
	});

	it("throws on a failed response rather than returning it beside the data", async () => {
		const createClientConfig = await loadCreateClientConfig();

		expect(createClientConfig({}).throwOnError).toBe(true);
	});

	it("sends no Authorization header while signed out", async () => {
		const { fetch, underlying } = await loadFetch(makeResponse(200));

		await fetch("https://api.test/v1/organizations");

		expect(sentHeaders(underlying).has("Authorization")).toBe(false);
	});

	it("sends the current bearer token while signed in", async () => {
		accessToken = "token-abc";
		const { fetch, underlying } = await loadFetch(makeResponse(200));

		await fetch("https://api.test/v1/organizations");

		expect(sentHeaders(underlying).get("Authorization")).toBe(
			"Bearer token-abc",
		);
	});

	// The backend resolves wall-clock times and localised copy from these, so
	// every request carries them whether or not anyone is signed in.
	it("always sends the timezone and language headers", async () => {
		const { fetch, underlying } = await loadFetch(makeResponse(200));

		await fetch("https://api.test/v1/organizations");

		const headers = sentHeaders(underlying);
		expect(headers.get("X-Timezone")).toBe(
			Intl.DateTimeFormat().resolvedOptions().timeZone,
		);
		expect(headers.get("X-Language")).toBe("de");
	});

	it("sends the base language tag, not the regional variant", async () => {
		language = "en-GB";
		const { fetch, underlying } = await loadFetch(makeResponse(200));

		await fetch("https://api.test/v1/organizations");

		expect(sentHeaders(underlying).get("X-Language")).toBe("en");
	});

	it("keeps the headers the caller already set", async () => {
		const { fetch, underlying } = await loadFetch(makeResponse(200));

		await fetch("https://api.test/v1/organizations", {
			headers: { "Content-Type": "application/json" },
		});

		expect(sentHeaders(underlying).get("Content-Type")).toBe(
			"application/json",
		);
	});

	it("runs the shared error handling for a failed response", async () => {
		accessToken = "token-abc";
		const { fetch } = await loadFetch(makeResponse(401));

		await fetch("https://api.test/v1/organizations");

		expect(notifySessionExpired).toHaveBeenCalledTimes(1);
	});

	describe("on a 401 to a signed-in request", () => {
		async function loadFetchAnswering(...responses: Response[]) {
			const createClientConfig = await loadCreateClientConfig();
			const underlying = vi.fn();
			for (const response of responses)
				underlying.mockResolvedValueOnce(response);
			vi.stubGlobal("fetch", underlying);
			const config = createClientConfig({});
			return { fetch: config.fetch as typeof globalThis.fetch, underlying };
		}

		// The API rejects a token issued before the user's permissions changed
		// (#2395, #2403, #2407) - a fresh one carries the current roles.
		it("retries once with a silently renewed token", async () => {
			accessToken = "stale-token";
			refreshAccessToken.mockResolvedValue("fresh-token");
			const { fetch, underlying } = await loadFetchAnswering(
				makeResponse(401),
				makeResponse(200),
			);

			const response = await fetch("https://api.test/v1/organizations");

			expect(response.status).toBe(200);
			expect(underlying).toHaveBeenCalledTimes(2);
			expect(
				new Headers(
					(underlying.mock.calls[1][1] as RequestInit).headers as HeadersInit,
				).get("Authorization"),
			).toBe("Bearer fresh-token");
			expect(notifySessionExpired).not.toHaveBeenCalled();
		});

		it("resends a request body the first attempt already consumed", async () => {
			accessToken = "stale-token";
			refreshAccessToken.mockResolvedValue("fresh-token");
			const { fetch, underlying } = await loadFetchAnswering(
				makeResponse(401),
				makeResponse(200),
			);

			await fetch(
				new Request("https://api.test/v1/organizations", {
					method: "POST",
					body: JSON.stringify({ name: "Verein" }),
				}),
			);

			const retried = underlying.mock.calls[1][0] as Request;
			expect(await retried.text()).toBe(JSON.stringify({ name: "Verein" }));
		});

		it("ends the session when no new token can be had", async () => {
			accessToken = "stale-token";
			refreshAccessToken.mockResolvedValue(undefined);
			const { fetch, underlying } = await loadFetchAnswering(makeResponse(401));

			await fetch("https://api.test/v1/organizations");

			expect(underlying).toHaveBeenCalledTimes(1);
			expect(notifySessionExpired).toHaveBeenCalledTimes(1);
		});

		it("ends the session when the renewed token is rejected too", async () => {
			accessToken = "stale-token";
			refreshAccessToken.mockResolvedValue("fresh-token");
			const { fetch, underlying } = await loadFetchAnswering(
				makeResponse(401),
				makeResponse(401),
			);

			await fetch("https://api.test/v1/organizations");

			expect(underlying).toHaveBeenCalledTimes(2);
			expect(notifySessionExpired).toHaveBeenCalledTimes(1);
		});

		it("reuses a token another request renewed meanwhile", async () => {
			accessToken = "stale-token";
			const { fetch, underlying } = await loadFetchAnswering();
			underlying
				.mockImplementationOnce(() => {
					// A parallel request's renewal lands while this one is in flight.
					accessToken = "fresh-token";
					return Promise.resolve(makeResponse(401));
				})
				.mockResolvedValueOnce(makeResponse(200));

			const response = await fetch("https://api.test/v1/organizations");

			expect(response.status).toBe(200);
			expect(refreshAccessToken).not.toHaveBeenCalled();
			expect(
				new Headers(
					(underlying.mock.calls[1][1] as RequestInit).headers as HeadersInit,
				).get("Authorization"),
			).toBe("Bearer fresh-token");
		});

		it("does not resend when renewal hands back the same token", async () => {
			accessToken = "stale-token";
			refreshAccessToken.mockResolvedValue("stale-token");
			const { fetch, underlying } = await loadFetchAnswering(makeResponse(401));

			await fetch("https://api.test/v1/organizations");

			expect(underlying).toHaveBeenCalledTimes(1);
			expect(notifySessionExpired).toHaveBeenCalledTimes(1);
		});
	});

	it("does not try to renew a token for a signed-out 401", async () => {
		const { fetch } = await loadFetch(makeResponse(401));

		await fetch("https://api.test/v1/organizations");

		expect(refreshAccessToken).not.toHaveBeenCalled();
	});

	it("passes a successful response through untouched", async () => {
		const body = new Response(JSON.stringify({ id: "1" }), {
			status: 200,
			headers: { "Content-Type": "application/json" },
		});
		const { fetch } = await loadFetch(body);

		expect(await fetch("https://api.test/v1/organizations")).toBe(body);
	});

	// lib/apiError.ts reads a missing status as "no response at all" and shows
	// the offline state. A gateway that answers with HTML would otherwise be
	// misreported as a lost connection.
	it("gives a non-JSON error body a status the app can classify", async () => {
		const { fetch } = await loadFetch(
			new Response("<html>502 Bad Gateway</html>", {
				status: 502,
				headers: { "Content-Type": "text/html" },
			}),
		);

		const response = await fetch("https://api.test/v1/organizations");

		expect(response.status).toBe(502);
		expect(await response.json()).toMatchObject({ status: 502 });
	});

	it("leaves a JSON error body exactly as the backend sent it", async () => {
		const problem = {
			status: 409,
			errorCode: "Engagement.AlreadySignedUp",
			detail: "internal detail",
		};
		const { fetch } = await loadFetch(
			new Response(JSON.stringify(problem), {
				status: 409,
				headers: { "Content-Type": "application/problem+json" },
			}),
		);

		const response = await fetch("https://api.test/v1/engagements");

		expect(await response.json()).toEqual(problem);
	});
});

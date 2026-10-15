import { beforeEach, describe, expect, it, vi } from "vitest";

async function loadModule() {
	vi.resetModules();
	return import("./accessToken");
}

describe("refreshAccessToken", () => {
	beforeEach(() => {
		vi.clearAllMocks();
	});

	it("resolves nothing while no refresher is installed", async () => {
		const { refreshAccessToken } = await loadModule();

		expect(await refreshAccessToken()).toBeUndefined();
	});

	it("makes the renewed token the one the client sends next", async () => {
		const {
			refreshAccessToken,
			setAccessToken,
			setAccessTokenRefresher,
			getAccessToken,
		} = await loadModule();
		setAccessToken("stale-token");
		setAccessTokenRefresher(() => Promise.resolve("fresh-token"));

		expect(await refreshAccessToken()).toBe("fresh-token");
		expect(getAccessToken()).toBe("fresh-token");
	});

	it("shares one renewal between parallel callers", async () => {
		const { refreshAccessToken, setAccessTokenRefresher } = await loadModule();
		const refresh = vi.fn(() => Promise.resolve("fresh-token"));
		setAccessTokenRefresher(refresh);

		await Promise.all([refreshAccessToken(), refreshAccessToken()]);

		expect(refresh).toHaveBeenCalledTimes(1);
	});

	it("starts a new renewal once the previous one has settled", async () => {
		const { refreshAccessToken, setAccessTokenRefresher } = await loadModule();
		const refresh = vi.fn(() => Promise.resolve("fresh-token"));
		setAccessTokenRefresher(refresh);

		await refreshAccessToken();
		await refreshAccessToken();

		expect(refresh).toHaveBeenCalledTimes(2);
	});

	it("resolves nothing and keeps the old token when renewal fails", async () => {
		const {
			refreshAccessToken,
			setAccessToken,
			setAccessTokenRefresher,
			getAccessToken,
		} = await loadModule();
		setAccessToken("stale-token");
		setAccessTokenRefresher(() => Promise.reject(new Error("login_required")));

		expect(await refreshAccessToken()).toBeUndefined();
		expect(getAccessToken()).toBe("stale-token");
	});
});

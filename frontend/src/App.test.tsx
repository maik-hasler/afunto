import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import App, { AppRoutes } from "./App";
import { renderWithProviders } from "./test/render";
import { expectNoA11yViolations } from "./test/a11y";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("./test/apiMock");
	return { api: createApiMock() };
});

vi.mock("./hooks/useApiClient", () => ({ useApiClient: () => api }));

beforeEach(() => {
	api.__reset();
});

const NOT_FOUND = "Page not found";

const renderAt = (route: string) =>
	renderWithProviders(<App />, { route, auth: { isAuthenticated: true } });

describe("retired routes", () => {
	it.each([
		[
			"/users/11111111-1111-1111-1111-111111111111/achievements",
			"the standalone achievements page, folded into the profile",
		],
		["/app", "the legacy org-app entry point, which now needs an org id"],
		[
			"/organizations/11111111-1111-1111-1111-111111111111/dashboard",
			"the legacy org dashboard URL, moved under /app",
		],
		["/impressum", "the old German imprint slug"],
		["/datenschutz", "the old German privacy-policy slug"],
	])("404s on %s (%s)", async (route) => {
		renderAt(route);

		expect(
			await screen.findByRole("heading", { level: 1, name: NOT_FOUND }),
		).toBeVisible();
		expect(
			screen.getByRole("link", { name: "Back to home" }),
		).toBeInTheDocument();
	});
});

describe("the bounded auth-recovery terminal state (#2208)", () => {
	it("replaces the route tree with a sign-in-failed page and can sign out", async () => {
		const signoutRedirect = vi.fn();
		// AppRoutes, not App: App wraps AppRoutes in its own AuthStatusProvider,
		// which would shadow the initialAuthRecoveryFailed one below.
		renderWithProviders(<AppRoutes />, {
			route: "/",
			auth: { isAuthenticated: true, signoutRedirect },
			authRecoveryFailed: true,
		});

		expect(
			await screen.findByRole("heading", { name: "Sign-in isn't working" }),
		).toBeVisible();
		expect(screen.queryByRole("link", { name: "Home" })).toBeNull();
		await expectNoA11yViolations();

		await userEvent.click(screen.getByRole("button", { name: "Sign out" }));
		expect(signoutRedirect).toHaveBeenCalledTimes(1);
	});
});

describe("the callback error retry", () => {
	it("never sends a successful retry back to the callback route itself", async () => {
		const signinRedirect = vi.fn().mockResolvedValue(undefined);
		renderWithProviders(<App />, {
			route: "/callback",
			auth: {
				isAuthenticated: false,
				error: new Error("token exchange failed"),
				signinRedirect,
			},
		});

		await userEvent.click(
			await screen.findByRole("button", { name: "Try again" }),
		);

		expect(signinRedirect).toHaveBeenCalledTimes(1);
		const args = signinRedirect.mock.calls[0][0] as
			{ state?: { returnTo?: string } } | undefined;
		// /callback has no route-away for "authenticated, no error, no code in
		// the URL" (see App.tsx), so returning here would strand the user on
		// the completing-signin screen forever - unlike the other signin call
		// sites, this one must NOT default returnTo to the current location.
		expect(args?.state?.returnTo).not.toBe("/callback");
	});
});

describe("the merged profile settings page (#2354)", () => {
	it("redirects /profile/settings to /profile", async () => {
		renderAt("/profile/settings");

		// ProfileOverviewPage keeps re-rendering as its own effects (profile,
		// streaks, engagement count) settle, so re-query on each retry instead
		// of asserting on a single findByRole snapshot that a later render can
		// detach from the document.
		await waitFor(() =>
			expect(
				screen.getByRole("heading", { level: 1, name: "My profile" }),
			).toBeVisible(),
		);
	});
});

describe("the legal pages", () => {
	it.each([
		["/imprint", "Imprint"],
		["/privacy-policy", "Privacy policy"],
	])(
		"renders %s with no breadcrumb and no in-band home link",
		async (route) => {
			renderAt(route);

			const heading = await screen.findByRole("heading", { level: 1 });
			expect(heading.textContent?.trim()).not.toBe("");

			const main = document.querySelector("main");
			expect(main).not.toBeNull();
			expect(document.querySelector('nav[aria-label="Breadcrumb"]')).toBeNull();
			expect(
				within(main as HTMLElement).queryByRole("link", { name: "Home" }),
			).toBeNull();
		},
	);
});

describe("the /callback dead ends (#2320)", () => {
	it("does not strand a bare /callback on the completing-sign-in line", async () => {
		renderWithProviders(<App />, {
			route: "/callback",
			auth: { isAuthenticated: false },
		});

		expect(
			await screen.findByTestId("callback-nothing-to-complete"),
		).toBeInTheDocument();
		expect(
			screen.getByRole("heading", {
				level: 1,
				name: "Nothing to complete here",
			}),
		).toBeInTheDocument();
		expect(
			screen.getByRole("link", { name: "Back to Afunto" }),
		).toHaveAttribute("href", "/");
		expect(screen.queryByText("Completing sign-in…")).toBeNull();
	});

	it("keeps completing while the sign-in response is being exchanged", async () => {
		renderWithProviders(<App />, {
			route: "/callback?state=abc&code=def",
			auth: { isAuthenticated: false },
		});

		expect(
			await screen.findByRole("heading", {
				level: 1,
				name: "Completing sign-in…",
			}),
		).toBeInTheDocument();
		expect(screen.queryByTestId("callback-nothing-to-complete")).toBeNull();
	});

	it("never prints the oidc-client internals in the error state", async () => {
		const consoleError = vi
			.spyOn(console, "error")
			.mockImplementation(() => {});
		renderWithProviders(<App />, {
			route: "/callback?state=abc&code=def",
			auth: {
				isAuthenticated: false,
				error: new Error("No matching state found in storage"),
			},
		});

		await screen.findByTestId("callback-error");

		expect(
			screen.getByRole("heading", { level: 1, name: "Sign-in didn't finish" }),
		).toBeInTheDocument();
		expect(document.body.textContent).not.toContain(
			"No matching state found in storage",
		);
		// Still recorded for whoever debugs this, just not shown to the visitor.
		expect(consoleError).toHaveBeenCalledWith(
			"[auth] sign-in callback failed:",
			"No matching state found in storage",
		);
		consoleError.mockRestore();
	});

	it("reports the identity provider's own refusal, not a later symptom", async () => {
		vi.spyOn(console, "error").mockImplementation(() => {});
		renderWithProviders(<App />, {
			route: "/callback?error=access_denied&state=abc",
			auth: { isAuthenticated: false },
		});

		await screen.findByTestId("callback-error");

		expect(
			screen.getByText(
				"Sign-in was declined, so you are not signed in. You can try again, or keep browsing without an account.",
			),
		).toBeInTheDocument();
		expect(
			screen.getByRole("button", { name: "Try again" }),
		).toBeInTheDocument();
		vi.restoreAllMocks();
	});
});

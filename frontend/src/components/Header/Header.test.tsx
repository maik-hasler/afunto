import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import Header from "./Header";
import { renderWithProviders } from "../../test/render";
import { hasExhaustedAuthRecovery } from "../../lib/authRecovery";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("../../test/apiMock");
	return { api: createApiMock() };
});

vi.mock("../../hooks/useApiClient", () => ({ useApiClient: () => api }));

beforeEach(() => {
	api.__reset();
	api.getOrganizations.mockResolvedValue([]);
	api.getNotifications.mockResolvedValue({ items: [], pageCount: 1 });
});

describe("Header for an anonymous visitor", () => {
	it("offers a way to sign in", () => {
		renderWithProviders(<Header />);

		expect(
			screen.getAllByRole("button", { name: "Sign in" })[0],
		).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "User menu" })).toBeNull();
	});
});

describe("Header while the silent-SSO probe is in flight (#2224)", () => {
	it("holds a neutral state instead of offering to sign in", () => {
		renderWithProviders(<Header />, {
			auth: { isAuthenticated: false, isLoading: true },
		});

		expect(screen.queryAllByRole("button", { name: "Sign in" })).toHaveLength(
			0,
		);
		expect(screen.queryByRole("button", { name: "Register" })).toBeNull();
		expect(screen.queryByRole("button", { name: "User menu" })).toBeNull();
		expect(
			screen.getAllByText("Checking sign-in status…")[0],
		).toBeInTheDocument();
	});
});

describe("Header after a failed token renewal (#2224)", () => {
	it("surfaces an explicit expired state instead of reverting to the anonymous interface", () => {
		renderWithProviders(<Header />, {
			auth: { isAuthenticated: true },
			sessionExpired: true,
		});

		expect(
			screen.getAllByRole("button", {
				name: "Sign in again",
			})[0],
		).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Sign in" })).toBeNull();
		expect(screen.queryByRole("button", { name: "User menu" })).toBeNull();
	});

	// #2208: only a sign-in that recovers an ended session uses up the one
	// allowed attempt - a reload alone must not.
	it("counts signing in again toward the bounded recovery", async () => {
		sessionStorage.clear();
		const signinRedirect = vi.fn().mockResolvedValue(undefined);
		renderWithProviders(<Header />, {
			auth: { isAuthenticated: true, signinRedirect },
			sessionExpired: true,
		});

		await userEvent.click(
			screen.getAllByRole("button", { name: "Sign in again" })[0],
		);

		expect(signinRedirect).toHaveBeenCalledTimes(1);
		expect(hasExhaustedAuthRecovery()).toBe(true);
	});
});

const ORG = {
	id: "77777777-7777-7777-7777-777777777777",
	name: "Freiwillige Feuerwehr Kiel",
};

const signedIn = { isAuthenticated: true, name: "Vera Volunteer" };

describe("Header primary navigation", () => {
	it("offers the opportunity list to an anonymous visitor", () => {
		const { container } = renderWithProviders(<Header />);

		const link = container.ownerDocument.querySelector(
			'[data-testid="nav-findOpportunities"]',
		);
		expect(link).toHaveAttribute("href", "/opportunities");
	});

	it("still carries the primary destinations once signed in", async () => {
		const { container } = renderWithProviders(<Header />, { auth: signedIn });

		await screen.findByRole("button", { name: "User menu" });
		expect(
			container.ownerDocument.querySelector(
				'[data-testid="nav-findOpportunities"]',
			),
		).toBeInTheDocument();
	});

	it("carries the way home in the nav", () => {
		const { container } = renderWithProviders(<Header />, {
			route: "/opportunities",
		});

		expect(
			container.ownerDocument.querySelector('[data-testid="nav-home"]'),
		).toHaveAttribute("href", "/");
	});
});

describe("Header organization entry", () => {
	beforeEach(() => {
		api.getOrganizations.mockResolvedValue([ORG]);
	});

	it("offers a member their organization as a top-level destination", async () => {
		renderWithProviders(<Header />, { auth: signedIn });

		const entry = await screen.findByTestId("nav-organization");
		expect(entry).toHaveAttribute("href", `/app/${ORG.id}/dashboard`);
	});

	it("offers a switcher alongside the direct link once a member belongs to more than one organization", async () => {
		const ORG_B = {
			id: "88888888-8888-8888-8888-888888888888",
			name: "Foerderverein Hamburg",
		};
		api.getOrganizations.mockResolvedValue([ORG, ORG_B]);

		renderWithProviders(<Header />, { auth: signedIn });

		expect(
			await screen.findByRole("button", {
				name: "Switch organization, currently Foerderverein Hamburg",
			}),
		).toBeInTheDocument();
		// The alphabetically-first org (ORG_B) is the resolved active one -
		// the direct nav entry still points at it, same as a single-org
		// member, so the mobile menu keeps offering direct sub-tab links.
		expect(await screen.findByTestId("nav-organization")).toHaveAttribute(
			"href",
			`/app/${ORG_B.id}/dashboard`,
		);
	});

	it("offers no organization entry for a signed-in non-member", async () => {
		api.getOrganizations.mockResolvedValue([]);

		renderWithProviders(<Header />, { auth: signedIn });

		await screen.findByRole("button", { name: "User menu" });
		expect(screen.queryByTestId("nav-organization")).toBeNull();
	});

	it("withholds the organization inside the org app, where the switcher names it", async () => {
		renderWithProviders(
			<Header
				orgSwitcher={{ currentOrgId: ORG.id, currentTab: "dashboard" }}
			/>,
			{ auth: signedIn },
		);

		expect(
			await screen.findByTestId("org-switcher-current-name"),
		).toHaveTextContent(/Feuerwehr/);
		expect(screen.queryByTestId("nav-organization")).toBeNull();
	});

	it("keeps the account menu to personal items only", async () => {
		const { container } = renderWithProviders(<Header />, { auth: signedIn });

		await userEvent.click(
			await screen.findByRole("button", { name: "User menu" }),
		);

		expect(
			screen.getByRole("link", { name: "My profile" }),
		).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Organization" })).toBeNull();
		expect(
			container.ownerDocument.querySelectorAll("a[href*='/dashboard/']"),
		).toHaveLength(0);
	});
});

describe("Header notifications", () => {
	it("offers the bell once authenticated, and opens its panel", async () => {
		renderWithProviders(<Header />, { auth: signedIn });

		const bell = await screen.findByTestId("notification-bell");
		expect(screen.queryByTestId("notification-panel")).toBeNull();

		await userEvent.click(bell);

		expect(await screen.findByTestId("notification-panel")).toBeVisible();
	});
});

describe("Header avatar", () => {
	it("shows an uploaded avatar in place of the initials", async () => {
		api.getUserProfile.mockResolvedValue({
			firstName: "Vera",
			lastName: "Volunteer",
			avatarUrl: "https://storage.example.test/a.png",
		});

		renderWithProviders(<Header />, { auth: signedIn });

		const menu = await screen.findByRole("button", { name: "User menu" });
		await waitFor(() => expect(menu.querySelector("img")).not.toBeNull());
		expect(menu.querySelector("img")).toHaveAttribute(
			"src",
			"https://storage.example.test/a.png",
		);
	});

	it("falls back to initials when no avatar was uploaded", async () => {
		api.getUserProfile.mockResolvedValue({
			firstName: "Vera",
			lastName: "Volunteer",
			avatarUrl: undefined,
		});

		renderWithProviders(<Header />, { auth: signedIn });

		const menu = await screen.findByRole("button", { name: "User menu" });
		expect(menu).toHaveTextContent("VV");
		expect(menu.querySelector("img")).toBeNull();
	});
});

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useOutletContext } from "react-router";
import OrgAppLayout from "./OrgAppLayout";
import type { OrgAppContext } from "./OrgAppLayout";
import OrgSettingsPage from "../pages/app/OrgSettingsPage";
import { renderWithProviders } from "../test/render";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("../test/apiMock");
	return { api: createApiMock() };
});

vi.mock("../hooks/useApiClient", () => ({ useApiClient: () => api }));

const ORG_ID = "11111111-1111-1111-1111-111111111111";

function rejectWith(status: number) {
	return Object.assign(new Error(`HTTP ${status}`), { status });
}

function setOnline(value: boolean) {
	Object.defineProperty(navigator, "onLine", {
		configurable: true,
		get: () => value,
	});
}

function renderOrgApp() {
	return renderWithProviders(
		<Routes>
			<Route path="/app/:organizationId" element={<OrgAppLayout />}>
				<Route path="dashboard" element={<div>Dashboard body</div>} />
			</Route>
		</Routes>,
		{ route: `/app/${ORG_ID}/dashboard`, auth: { isAuthenticated: true } },
	);
}

beforeEach(() => {
	api.__reset();
	setOnline(true);
	document.title = "";
});

afterEach(() => setOnline(true));

describe("OrgAppLayout org-load failures", () => {
	it("says the organization does not exist on a 404", async () => {
		api.getOrganizationDetails.mockRejectedValue(rejectWith(404));
		renderOrgApp();

		expect(
			await screen.findByRole("heading", { name: "Organization not found" }),
		).toBeInTheDocument();
		expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
		await waitFor(() =>
			expect(document.title).toBe("Organization not found | Afunto"),
		);
	});

	it("says the same for the 400 an all-zero organization id produces", async () => {
		api.getOrganizationDetails.mockRejectedValue(rejectWith(400));
		renderOrgApp();

		expect(
			await screen.findByRole("heading", { name: "Organization not found" }),
		).toBeInTheDocument();
		expect(
			screen.queryByRole("heading", { name: "Something went wrong" }),
		).toBeNull();
	});

	it("offers a recoverable state on a 500, and recovers on retry", async () => {
		api.getOrganizationDetails.mockRejectedValueOnce(rejectWith(500));
		api.getOrganizationDetails.mockResolvedValue({
			id: ORG_ID,
			name: "Freiwillige Feuerwehr Kiel",
			members: [],
			requestingUserRole: "Organizer",
			membersUnavailable: false,
			createdOn: new Date(Date.UTC(2026, 0, 1)),
		});
		renderOrgApp();

		expect(
			await screen.findByRole("heading", { name: "Something went wrong" }),
		).toBeInTheDocument();
		expect(
			screen.queryByText("You don't have access to this organization."),
		).toBeNull();

		await userEvent.click(screen.getByRole("button", { name: "Try again" }));

		expect(await screen.findByText("Dashboard body")).toBeInTheDocument();
		expect(
			screen.queryByRole("heading", { name: "Something went wrong" }),
		).toBeNull();
	});

	it("says it is offline rather than 'something went wrong'", async () => {
		setOnline(false);
		api.getOrganizationDetails.mockRejectedValue(new Error("network"));
		renderOrgApp();

		expect(
			await screen.findByRole("heading", { name: "You are offline" }),
		).toBeInTheDocument();
		expect(screen.queryByText(/An unexpected error occurred/)).toBeNull();
		expect(
			screen.getByRole("button", { name: "Try again" }),
		).toBeInTheDocument();
	});

	it("still says offline when navigator.onLine misreports true", async () => {
		setOnline(true);
		api.getOrganizationDetails.mockRejectedValue(new Error("network"));
		renderOrgApp();

		expect(
			await screen.findByRole("heading", { name: "You are offline" }),
		).toBeInTheDocument();
		expect(screen.queryByText(/An unexpected error occurred/)).toBeNull();
	});

	it("recovers from offline on the manual retry alone, with no online event", async () => {
		setOnline(true);
		api.getOrganizationDetails.mockRejectedValueOnce(new Error("network"));
		api.getOrganizationDetails.mockResolvedValue({
			id: ORG_ID,
			name: "Freiwillige Feuerwehr Kiel",
			members: [],
			requestingUserRole: "Organizer",
			membersUnavailable: false,
			createdOn: new Date(Date.UTC(2026, 0, 1)),
		});
		renderOrgApp();

		await screen.findByRole("heading", { name: "You are offline" });
		await userEvent.click(screen.getByRole("button", { name: "Try again" }));

		expect(await screen.findByText("Dashboard body")).toBeInTheDocument();
		expect(
			screen.queryByRole("heading", { name: "You are offline" }),
		).toBeNull();
	});
});

describe("OrgAppLayout shell", () => {
	beforeEach(() => {
		api.getOrganizationDetails.mockResolvedValue({
			id: ORG_ID,
			name: "Freiwillige Feuerwehr Kiel",
			members: [],
			requestingUserRole: "Organizer",
			membersUnavailable: false,
			createdOn: new Date(Date.UTC(2026, 0, 1)),
		});
		api.getMyAchievements.mockResolvedValue([]);
		api.getOrganizations.mockResolvedValue([
			{ id: ORG_ID, name: "Freiwillige Feuerwehr Kiel", role: "Organizer" },
		]);
	});

	it("names the page in the band, never the organization", async () => {
		renderOrgApp();

		const heading = await screen.findByRole("heading", { level: 1 });
		expect(heading).toHaveTextContent("Dashboard");
		expect(heading).not.toHaveTextContent("Freiwillige Feuerwehr Kiel");
		expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
	});

	it("keeps the way back and the way across in the chrome", async () => {
		renderOrgApp();

		expect(
			await screen.findByRole("button", {
				name: "Switch organization, currently Freiwillige Feuerwehr Kiel",
			}),
		).toBeInTheDocument();
		const rail = screen.getByRole("navigation", {
			name: "Organization sections",
		});
		expect(within(rail).getByTestId("org-tab-dashboard")).toHaveAttribute(
			"aria-current",
			"page",
		);
	});

	it("gives an organizer all five sections", async () => {
		renderOrgApp();

		const rail = await screen.findByRole("navigation", {
			name: "Organization sections",
		});
		expect(within(rail).getByTestId("org-tab-engagements")).toBeInTheDocument();
	});

	// The sign-ups listing is organizer-only: showing a plain member the tab
	// only offers them a request that is guaranteed to 403 (#2316). Every
	// other section reads an endpoint a member may call, so they stay.
	it("hides the sign-ups section from a plain member", async () => {
		api.getOrganizationDetails.mockResolvedValue({
			id: ORG_ID,
			name: "Freiwillige Feuerwehr Kiel",
			members: [],
			requestingUserRole: "Member",
			membersUnavailable: false,
			createdOn: new Date(Date.UTC(2026, 0, 1)),
		});
		renderOrgApp();

		const rail = await screen.findByRole("navigation", {
			name: "Organization sections",
		});
		expect(within(rail).queryByTestId("org-tab-engagements")).toBeNull();
		expect(within(rail).getByTestId("org-tab-dashboard")).toBeInTheDocument();
		expect(
			within(rail).getByTestId("org-tab-opportunities"),
		).toBeInTheDocument();
		expect(within(rail).getByTestId("org-tab-settings")).toBeInTheDocument();
		expect(within(rail).getByTestId("org-tab-members")).toBeInTheDocument();
	});

	it("runs its own achievements check on entry", async () => {
		renderOrgApp();

		await waitFor(() => expect(api.getMyAchievements).toHaveBeenCalled());
	});
});

describe("OrgAppLayout background refresh", () => {
	function orgNamed(name: string) {
		return {
			id: ORG_ID,
			name,
			members: [],
			requestingUserRole: "Organizer",
			membersUnavailable: false,
			createdOn: new Date(Date.UTC(2026, 0, 1)),
		};
	}

	// Stands in for any page that reloads the organization after a mutation:
	// it holds unsaved local state the way the settings form does, so a
	// remount is visible as that state disappearing.
	function ReloadingChild() {
		const { org, reloadOrg } = useOutletContext<OrgAppContext>();
		return (
			<div>
				<p>Org is {org.name}</p>
				<label htmlFor="draft">Draft</label>
				<input id="draft" defaultValue="" />
				<button type="button" onClick={reloadOrg}>
					Reload org
				</button>
			</div>
		);
	}

	function renderWithChild() {
		return renderWithProviders(
			<Routes>
				<Route path="/app/:organizationId" element={<OrgAppLayout />}>
					<Route path="dashboard" element={<ReloadingChild />} />
				</Route>
			</Routes>,
			{ route: `/app/${ORG_ID}/dashboard`, auth: { isAuthenticated: true } },
		);
	}

	beforeEach(() => {
		api.getMyAchievements.mockResolvedValue([]);
		api.getOrganizations.mockResolvedValue([]);
	});

	it("keeps the page and its unsaved input while refreshing", async () => {
		let releaseRefresh = () => {};
		api.getOrganizationDetails
			.mockResolvedValueOnce(orgNamed("Freiwillige Feuerwehr Kiel"))
			.mockImplementationOnce(
				() =>
					new Promise((resolve) => {
						releaseRefresh = () => resolve(orgNamed("Feuerwehr Kiel e.V."));
					}),
			);
		renderWithChild();

		await screen.findByText("Org is Freiwillige Feuerwehr Kiel");
		await userEvent.type(screen.getByLabelText("Draft"), "unsaved work");
		await userEvent.click(screen.getByRole("button", { name: "Reload org" }));

		// The full-screen spinner used to replace the whole shell here, which
		// unmounted the page and took its unsaved input with it (#2315).
		expect(screen.queryByText("Loading…")).toBeNull();
		expect(screen.getByLabelText("Draft")).toHaveValue("unsaved work");

		releaseRefresh();
		expect(
			await screen.findByText("Org is Feuerwehr Kiel e.V."),
		).toBeInTheDocument();
		expect(screen.getByLabelText("Draft")).toHaveValue("unsaved work");
	});

	it("leaves the loaded organization on screen when the refresh fails", async () => {
		api.getOrganizationDetails
			.mockResolvedValueOnce(orgNamed("Freiwillige Feuerwehr Kiel"))
			.mockRejectedValueOnce(rejectWith(500));
		renderWithChild();

		await screen.findByText("Org is Freiwillige Feuerwehr Kiel");
		await userEvent.click(screen.getByRole("button", { name: "Reload org" }));

		await waitFor(() =>
			expect(api.getOrganizationDetails).toHaveBeenCalledTimes(2),
		);
		expect(
			screen.queryByRole("heading", { name: "Something went wrong" }),
		).toBeNull();
		expect(
			screen.getByText("Org is Freiwillige Feuerwehr Kiel"),
		).toBeInTheDocument();
	});

	it("still blanks to a spinner for the first load of the organization", async () => {
		api.getOrganizationDetails.mockImplementation(() => new Promise(() => {}));
		renderWithChild();

		expect(await screen.findByText("Loading…")).toBeInTheDocument();
		expect(screen.queryByLabelText("Draft")).toBeNull();
	});
});

describe("OrgSettingsPage inside the org shell", () => {
	const ORG_WITH_LOGO = {
		id: ORG_ID,
		name: "Freiwillige Feuerwehr Kiel",
		description: "Wir helfen, wo Hilfe gebraucht wird.",
		logoUrl: "https://storage.test/logo.png",
		members: [],
		requestingUserRole: "Organizer",
		membersUnavailable: false,
		createdOn: new Date(Date.UTC(2026, 0, 1)),
	};

	function renderSettings() {
		return renderWithProviders(
			<Routes>
				<Route path="/app/:organizationId" element={<OrgAppLayout />}>
					<Route path="dashboard/settings" element={<OrgSettingsPage />} />
				</Route>
			</Routes>,
			{
				route: `/app/${ORG_ID}/dashboard/settings`,
				auth: { isAuthenticated: true },
			},
		);
	}

	beforeEach(() => {
		api.getMyAchievements.mockResolvedValue([]);
		api.getOrganizations.mockResolvedValue([]);
	});

	// The refresh has to still be in flight for the assertions to mean
	// anything: an already-resolved mock settles inside the same act() flush,
	// so React coalesces the blanking render away and the old bug hides.
	function deferRefresh(next: unknown) {
		let release = () => {};
		api.getOrganizationDetails
			.mockResolvedValueOnce(ORG_WITH_LOGO)
			.mockImplementationOnce(
				() =>
					new Promise((resolve) => {
						release = () => resolve(next);
					}),
			);
		return () => release();
	}

	async function openEditForm() {
		await userEvent.click(await screen.findByRole("button", { name: "Edit" }));
		return (await waitFor(() => {
			const el = document.querySelector("#org-description");
			expect(el).not.toBeNull();
			return el;
		})) as HTMLTextAreaElement;
	}

	it("keeps unsaved settings edits when the logo is removed", async () => {
		const releaseRefresh = deferRefresh({
			...ORG_WITH_LOGO,
			logoUrl: undefined,
		});
		api.deleteOrganizationLogo.mockResolvedValue(undefined);
		renderSettings();

		const description = await openEditForm();
		await userEvent.clear(description);
		await userEvent.type(description, "Noch nicht gespeichert");

		await userEvent.click(screen.getByTestId("logo-remove"));
		await waitFor(() =>
			expect(api.getOrganizationDetails).toHaveBeenCalledTimes(2),
		);

		// The refresh used to unmount the whole shell, dropping the user back
		// into the read-only view with the typed text gone (#2315).
		expect(screen.queryByText("Loading…")).toBeNull();
		expect(document.querySelector("#org-description")).toHaveValue(
			"Noch nicht gespeichert",
		);

		releaseRefresh();
		await waitFor(() =>
			expect(document.querySelector("#org-description")).toHaveValue(
				"Noch nicht gespeichert",
			),
		);
	});

	it("shows the save confirmation instead of blanking the shell", async () => {
		const releaseRefresh = deferRefresh(ORG_WITH_LOGO);
		api.updateOrganization.mockResolvedValue(undefined);
		renderSettings();

		await openEditForm();
		await userEvent.click(screen.getByTestId("org-settings-form-save"));
		await waitFor(() =>
			expect(api.getOrganizationDetails).toHaveBeenCalledTimes(2),
		);

		expect(await screen.findByText("Changes saved.")).toBeInTheDocument();
		expect(screen.queryByText("Loading…")).toBeNull();

		releaseRefresh();
		await waitFor(() =>
			expect(screen.getByText("Changes saved.")).toBeInTheDocument(),
		);
	});
});

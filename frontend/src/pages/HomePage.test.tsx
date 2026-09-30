import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import HomePage from "./HomePage";
import { useLocation } from "react-router";
import { renderWithProviders } from "../test/render";

function LocationProbe() {
	const location = useLocation();
	return (
		<span data-testid="location-probe">{`${location.pathname}${location.search}`}</span>
	);
}

const { api } = vi.hoisted(() => ({
	api: {
		getOrganizations: vi.fn(),
		getVolunteerOpportunities: vi.fn(),
		createOrganization: vi.fn(),
		uploadOrganizationLogo: vi.fn(),
		searchCities: vi.fn(),
	},
}));

vi.mock("../hooks/useApiClient", () => ({ useApiClient: () => api }));

const org = {
	id: "11111111-1111-1111-1111-111111111111",
	name: "Freiwillige Feuerwehr Kiel",
	logoUrl: undefined,
};

beforeEach(() => {
	vi.clearAllMocks();
	api.getOrganizations.mockResolvedValue([]);
	api.getVolunteerOpportunities.mockResolvedValue({
		items: [],
		hasMore: false,
	});
	localStorage.clear();
	document.cookie = "active-org=; Max-Age=0; path=/";
});

describe("HomePage hero organization CTA", () => {
	it("opens organization creation in place for a signed-in visitor with no organizations", async () => {
		renderWithProviders(<HomePage />, { auth: { isAuthenticated: true } });

		const cta = await screen.findByRole("button", {
			name: "Create an organization",
		});
		await userEvent.click(cta);

		const dialog = await screen.findByRole("dialog", {
			name: /Create organization/,
		});
		expect(dialog).toBeInTheDocument();

		expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
	});

	it("swaps the CTA for a dashboard link once the visitor organizes one", async () => {
		api.getOrganizations.mockResolvedValue([org]);
		renderWithProviders(<HomePage />, { auth: { isAuthenticated: true } });

		const link = await screen.findByRole("link", {
			name: "Go to dashboard",
		});
		expect(link).toHaveAttribute("href", `/app/${org.id}/dashboard`);
		expect(
			screen.queryByRole("button", { name: "Create an organization" }),
		).toBeNull();
	});

	it("shows neither branch while the organization list failed to load", async () => {
		api.getOrganizations.mockRejectedValue(new Error("network"));
		renderWithProviders(<HomePage />, { auth: { isAuthenticated: true } });

		await screen.findByRole("heading", { level: 1 });
		await vi.waitFor(() => {
			expect(api.getOrganizations).toHaveBeenCalled();
		});

		expect(
			screen.queryByRole("button", { name: "Create an organization" }),
		).toBeNull();
		expect(screen.queryByRole("link", { name: "Go to dashboard" })).toBeNull();
	});
});

describe("HomePage FAQ", () => {
	it("draws its questions from the ones /help answers", async () => {
		renderWithProviders(<HomePage />);

		expect(
			await screen.findByText("Does using Afunto cost anything?"),
		).toBeInTheDocument();
		expect(screen.getByRole("link", { name: /Help/ })).toBeInTheDocument();
	});
});

describe("HomePage structure", () => {
	it("leads with a main heading", async () => {
		renderWithProviders(<HomePage />);

		const heading = await screen.findByRole("heading", { level: 1 });
		expect(heading.textContent?.trim()).not.toBe("");
	});

	it("carries no breadcrumb, being the root of every trail", async () => {
		renderWithProviders(<HomePage />);

		await screen.findByRole("heading", { level: 1 });
		expect(document.querySelector('nav[aria-label="Breadcrumb"]')).toBeNull();
	});
});

describe("HomePage hero search", () => {
	it("carries its keyword into the browse list", async () => {
		renderWithProviders(
			<>
				<HomePage />
				<LocationProbe />
			</>,
		);

		await userEvent.type(
			screen.getByTestId("hero-keyword-input"),
			"Erste Hilfe",
		);
		await userEvent.click(screen.getByRole("button", { name: /Search/i }));

		const probe = await screen.findByTestId("location-probe");

		expect(probe.textContent).toBe("/opportunities?q=Erste+Hilfe");
		expect(api.searchCities).not.toHaveBeenCalled();
	});
});

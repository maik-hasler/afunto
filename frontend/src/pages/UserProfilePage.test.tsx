import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import { Route, Routes } from "react-router";
import UserProfilePage from "./UserProfilePage";
import { renderWithProviders } from "../test/render";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("../test/apiMock");
	return { api: createApiMock() };
});

vi.mock("../hooks/useApiClient", () => ({ useApiClient: () => api }));

const USER_ID = "11111111-1111-1111-1111-111111111111";

beforeEach(() => {
	api.__reset();
	api.getPublicUserProfile.mockResolvedValue({
		displayName: "Vera Volunteer",
		engagementCount: 3,
		badges: [],
		avatarUrl: undefined,
		bio: undefined,
		skills: [],
		languages: [],
	});
	api.getBadgeCatalog.mockResolvedValue([]);
});

describe("UserProfilePage badges", () => {
	it("shows them inline, with no link to the retired achievements page", async () => {
		renderWithProviders(
			<Routes>
				<Route path="/users/:userId" element={<UserProfilePage />} />
			</Routes>,
			{ route: `/users/${USER_ID}`, auth: { isAuthenticated: true } },
		);

		expect(
			await screen.findByRole("heading", { name: "Badges" }),
		).toBeVisible();
		expect(screen.queryByRole("link", { name: /achievements/i })).toBeNull();
		expect(
			document.querySelector(`a[href="/users/${USER_ID}/achievements"]`),
		).toBeNull();
	});
});

describe("UserProfilePage missing profile", () => {
	function renderProfile() {
		return renderWithProviders(
			<Routes>
				<Route path="/users/:userId" element={<UserProfilePage />} />
			</Routes>,
			{ route: `/users/${USER_ID}` },
		);
	}

	it("shows the not-found state with a way back instead of an endless retry", async () => {
		api.getPublicUserProfile.mockRejectedValue({ status: 404 });
		renderProfile();

		await screen.findByTestId("user-profile-load-failure");

		expect(
			screen.getByRole("heading", { level: 1, name: "Profile not found" }),
		).toBeInTheDocument();
		expect(
			screen.queryByRole("button", { name: "Try again" }),
		).not.toBeInTheDocument();
		expect(screen.getByRole("link", { name: "Back to home" })).toHaveAttribute(
			"href",
			"/",
		);
	});

	it("does not leave the tab title on the loading placeholder", async () => {
		api.getPublicUserProfile.mockRejectedValue({ status: 404 });
		renderProfile();

		await screen.findByTestId("user-profile-load-failure");

		await waitFor(() =>
			expect(document.title).toBe("Profile not found | Afunto"),
		);
	});

	it("keeps a retry for a genuine server error", async () => {
		api.getPublicUserProfile.mockRejectedValue({ status: 500 });
		renderProfile();

		await screen.findByTestId("user-profile-load-failure");

		expect(
			screen.getByRole("heading", { level: 1, name: "Something went wrong" }),
		).toBeInTheDocument();
		expect(
			screen.getByRole("button", { name: "Try again" }),
		).toBeInTheDocument();
	});
});

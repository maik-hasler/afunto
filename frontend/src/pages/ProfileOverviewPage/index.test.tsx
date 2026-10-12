import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ProfileOverviewPage from "./index";
import { inputClass, labelClass } from "../../lib/formClasses";
import { useLocation } from "react-router";
import { renderWithProviders } from "../../test/render";
import {
	clearDisplayNameOverride,
	getDisplayNameOverride,
} from "../../lib/displayName";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("../../test/apiMock");
	return { api: createApiMock() };
});

vi.mock("../../hooks/useApiClient", () => ({ useApiClient: () => api }));

beforeEach(() => {
	api.__reset();
	clearDisplayNameOverride();
	api.getUserProfile.mockResolvedValue({
		firstName: "Vera",
		lastName: "Volunteer",
		bio: "Ich helfe gern.",
		skills: [],
		languages: [],
		phone: undefined,
		preferredContact: undefined,
		avatarUrl: undefined,
	});
	api.getMyStreaks.mockResolvedValue({
		loginStreak: 0,
		activityStreak: 0,
		confirmedEngagements: 0,
	});
	api.getMyAchievements.mockResolvedValue([]);
	api.getBadgeCatalog.mockResolvedValue([]);
});

describe("ProfileOverviewPage edit form", () => {
	it("uses the shared input and label classes rather than page-local copies", async () => {
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		const edit = await screen.findByTestId("profile-edit");
		await userEvent.click(edit);

		await waitFor(() =>
			expect(document.querySelector("#first-name")).not.toBeNull(),
		);
		expect(document.querySelector("#first-name")).toHaveAttribute(
			"class",
			inputClass,
		);
		expect(document.querySelector("label[for='first-name']")).toHaveAttribute(
			"class",
			labelClass,
		);
	});
});

describe("ProfileOverviewPage save feedback", () => {
	it("keeps the success region mounted and empty until a save succeeds", async () => {
		api.updateUserProfile.mockResolvedValue(undefined);
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		const banner = await screen.findByRole("status");
		expect(banner).toHaveAttribute("aria-live", "polite");
		expect(banner).toHaveTextContent("");

		await userEvent.click(await screen.findByTestId("profile-edit"));
		await userEvent.click(
			await screen.findByRole("button", { name: /^Save$/ }),
		);

		await waitFor(() =>
			expect(screen.getByRole("status")).toHaveTextContent("Profile saved."),
		);
	});
});

describe("ProfileOverviewPage streak stat tiles (German)", () => {
	const renderWithStreaks = (streaks: {
		loginStreak: number;
		activityStreak: number;
	}) => {
		api.getMyStreaks.mockResolvedValue({
			...streaks,
			confirmedEngagements: 0,
		});
		return renderWithProviders(<ProfileOverviewPage />, {
			lng: "de",
			auth: { isAuthenticated: true },
		});
	};

	it("gives the activity-streak tile a week unit instead of a bare number", async () => {
		renderWithStreaks({ loginStreak: 0, activityStreak: 1 });

		const tile = await screen.findByTestId("profile-stat-streak");
		expect(tile).toHaveTextContent("Woche in Serie");
		expect(tile).not.toHaveTextContent("Aktivitätsserie");
	});

	it("inflects the activity-streak unit for a count above one", async () => {
		renderWithStreaks({ loginStreak: 0, activityStreak: 3 });

		expect(await screen.findByTestId("profile-stat-streak")).toHaveTextContent(
			"Wochen in Serie",
		);
	});

	it("names each streak's own badge so the two tiles read as distinct", async () => {
		renderWithStreaks({ loginStreak: 2, activityStreak: 1 });

		const streakTile = await screen.findByTestId("profile-stat-streak");
		const loginTile = await screen.findByTestId("profile-stat-login-streak");

		expect(streakTile).toHaveTextContent("Wochenheld");
		expect(streakTile).not.toHaveTextContent("Anmeldeserie");
		expect(loginTile).toHaveTextContent("Tage in Folge");
		expect(loginTile).toHaveTextContent("Anmeldeserie");
		expect(loginTile).not.toHaveTextContent("Wochenheld");
	});

	it("inflects the login-streak unit for a single day", async () => {
		renderWithStreaks({ loginStreak: 1, activityStreak: 0 });

		const loginTile = await screen.findByTestId("profile-stat-login-streak");
		expect(loginTile).toHaveTextContent("Tag in Folge");
		expect(loginTile).not.toHaveTextContent("Login-Serie");
	});
});

describe("ProfileOverviewPage composition", () => {
	it("renders one page with no tab switcher", async () => {
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		expect(
			await screen.findByRole("heading", { name: "Profile details" }),
		).toBeInTheDocument();
		expect(screen.getByRole("heading", { name: "Badges" })).toBeInTheDocument();

		for (const name of ["Profile", "Activity", "Share achievements"]) {
			expect(screen.queryByRole("button", { name })).toBeNull();
		}
	});

	it("no longer carries an organizations section", async () => {
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await screen.findByRole("heading", { name: "Profile details" });
		expect(
			screen.queryByRole("heading", { name: /organi[sz]ations/i }),
		).toBeNull();
	});

	it("carries no in-page Home link, since the header nav owns that", async () => {
		const { container } = renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await screen.findByRole("heading", { name: "Profile details" });
		expect(container.querySelector("nav[aria-label='Breadcrumb']")).toBeNull();
		expect(screen.queryByRole("link", { name: "Home" })).toBeNull();
	});
});

describe("ProfileOverviewPage identity fields", () => {
	it("shows username and email as read-only, beside a Save action", async () => {
		api.getUserProfile.mockResolvedValue({
			firstName: "Vera",
			lastName: "Volunteer",
			username: "vera",
			email: "vera@example.com",
			bio: "",
			skills: [],
			languages: [],
		});

		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await userEvent.click(await screen.findByTestId("profile-edit"));

		const username = screen.getByLabelText("Username");
		const email = screen.getByLabelText("Email address");
		expect(username).toHaveValue("vera");
		expect(email).toHaveValue("vera@example.com");
		expect(username).toBeDisabled();
		expect(email).toBeDisabled();
		expect(screen.getByRole("button", { name: /^Save$/ })).toBeInTheDocument();
	});

	it("sends the edited name on save", async () => {
		api.updateUserProfile.mockResolvedValue(undefined);

		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await userEvent.click(await screen.findByTestId("profile-edit"));
		const first = screen.getByLabelText("First name");
		await userEvent.clear(first);
		await userEvent.type(first, "Vera");
		const last = screen.getByLabelText("Last name");
		await userEvent.clear(last);
		await userEvent.type(last, "Sample");

		await userEvent.click(screen.getByRole("button", { name: /^Save$/ }));

		await waitFor(() =>
			expect(api.updateUserProfile).toHaveBeenCalledWith({
				body: expect.objectContaining({
					firstName: "Vera",
					lastName: "Sample",
				}),
			}),
		);
	});
});

describe("ProfileOverviewPage legacy tab deep links", () => {
	function LocationProbe() {
		const location = useLocation();
		return <output data-testid="location">{location.pathname}</output>;
	}

	it("redirects the old engagements tab to /my-signups", async () => {
		renderWithProviders(
			<>
				<ProfileOverviewPage />
				<LocationProbe />
			</>,
			{ auth: { isAuthenticated: true }, route: "/profile?tab=engagements" },
		);

		await waitFor(() =>
			expect(screen.getByTestId("location")).toHaveTextContent("/my-signups"),
		);
	});

	it("stays on /profile for a tab that is not a legacy alias", async () => {
		renderWithProviders(
			<>
				<ProfileOverviewPage />
				<LocationProbe />
			</>,
			{ auth: { isAuthenticated: true }, route: "/profile" },
		);

		await screen.findByRole("heading", { name: "Profile details" });
		expect(screen.getByTestId("location")).toHaveTextContent("/profile");
	});
});

describe("ProfileOverviewPage failed save", () => {
	async function editAndFailSave() {
		api.updateUserProfile.mockRejectedValue(new Error("400"));
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await userEvent.click(await screen.findByTestId("profile-edit"));
		const bio = screen.getByLabelText("Bio");
		await userEvent.clear(bio);
		await userEvent.type(bio, "Precious unsaved text.");
		const phone = screen.getByLabelText("Phone");
		await userEvent.type(phone, "+49 341 555 0100");

		await userEvent.click(screen.getByRole("button", { name: /^Save$/ }));
		expect(await screen.findByTestId("profile-save-error")).toHaveTextContent(
			"Failed to save profile.",
		);
	}

	it("reports the failure without offering a retry that reloads the profile", async () => {
		await editAndFailSave();

		// "Retry" here belonged to the load-error component: it re-fetched the
		// profile and reset every field, so the button next to a failed save
		// discarded the user's work instead of re-sending it (#2315).
		expect(screen.queryByRole("button", { name: "Retry" })).toBeNull();
		expect(api.getUserProfile).toHaveBeenCalledTimes(1);
	});

	it("leaves the typed values in place, ready to save again", async () => {
		await editAndFailSave();

		expect(screen.getByLabelText("Bio")).toHaveValue("Precious unsaved text.");
		expect(screen.getByLabelText("Phone")).toHaveValue("+49 341 555 0100");

		api.updateUserProfile.mockResolvedValue(undefined);
		await userEvent.click(screen.getByRole("button", { name: /^Save$/ }));

		await waitFor(() =>
			expect(api.updateUserProfile).toHaveBeenLastCalledWith({
				body: expect.objectContaining({
					bio: "Precious unsaved text.",
					phone: "+49 341 555 0100",
				}),
			}),
		);
		expect(screen.queryByTestId("profile-save-error")).toBeNull();
	});

	it("still offers the reloading retry when it is the load that failed", async () => {
		api.getUserProfile.mockRejectedValue(new Error("500"));
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		expect(
			await screen.findByText("Could not load profile.", {}, { timeout: 6000 }),
		).toBeInTheDocument();
		expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
	}, 15000);
});

describe("ProfileOverviewPage field limits", () => {
	it("bounds every field the server would reject as too long", async () => {
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});
		await userEvent.click(await screen.findByTestId("profile-edit"));

		expect(screen.getByLabelText("First name")).toHaveAttribute(
			"maxlength",
			"100",
		);
		expect(screen.getByLabelText("Last name")).toHaveAttribute(
			"maxlength",
			"100",
		);
		expect(screen.getByLabelText("Bio")).toHaveAttribute("maxlength", "1000");
		expect(screen.getByLabelText("Phone")).toHaveAttribute("maxlength", "30");
		expect(screen.getByLabelText("Skills & interests")).toHaveAttribute(
			"maxlength",
			"100",
		);
		expect(screen.getByLabelText("Languages")).toHaveAttribute(
			"maxlength",
			"50",
		);
	});
});

describe("ProfileOverviewPage bio counter", () => {
	it("counts the bio's characters like the report dialog does", async () => {
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await userEvent.click(await screen.findByTestId("profile-edit"));

		expect(await screen.findByText("15 / 1000")).toBeInTheDocument();
	});
});

describe("ProfileOverviewPage rejected save", () => {
	it("names the fields the server rejected instead of a bare failure", async () => {
		api.updateUserProfile.mockRejectedValue({
			status: 400,
			response: JSON.stringify({
				errors: {
					FirstName: [
						"The field FirstName must be a string or array type with a maximum length of '100'.",
					],
				},
			}),
		});
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await userEvent.click(await screen.findByTestId("profile-edit"));
		await userEvent.click(
			await screen.findByRole("button", { name: /^Save$/ }),
		);

		expect(
			await screen.findByText(
				"Some entries are too long. Shorten the highlighted fields and save again.",
			),
		).toBeInTheDocument();
		expect(document.querySelector("#first-name")).toHaveAttribute(
			"aria-invalid",
			"true",
		);
		expect(
			screen.getByText("Must not be longer than 100 characters."),
		).toBeInTheDocument();
		expect(document.querySelector("#last-name")).not.toHaveAttribute(
			"aria-invalid",
		);
	});

	it("falls back to the generic message when the server blames no field", async () => {
		api.updateUserProfile.mockRejectedValue({ status: 500 });
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await userEvent.click(await screen.findByTestId("profile-edit"));
		await userEvent.click(
			await screen.findByRole("button", { name: /^Save$/ }),
		);

		expect(
			await screen.findByText("Failed to save profile."),
		).toBeInTheDocument();
	});
});

describe("ProfileOverviewPage rejected field a11y", () => {
	it("ties the field's error text to the input that carries it", async () => {
		api.updateUserProfile.mockRejectedValue({
			status: 400,
			response: JSON.stringify({ errors: { Bio: ["too long"] } }),
		});
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});

		await userEvent.click(await screen.findByTestId("profile-edit"));
		await userEvent.click(
			await screen.findByRole("button", { name: /^Save$/ }),
		);

		// Wait on the rendered message, like the sibling rejected-save tests do,
		// then read the wiring off the settled DOM.
		const error = await screen.findByText(
			"Must not be longer than 1000 characters.",
		);

		expect(error).toHaveAttribute("id", "bio-error");
		// Field derives the id as `${id}-error`; without the pointer the message
		// is visible but never announced with the control.
		expect(document.querySelector("#bio")).toHaveAttribute(
			"aria-describedby",
			"bio-error",
		);
		expect(document.querySelector("#bio")).toHaveAttribute(
			"aria-invalid",
			"true",
		);
	});
});

describe("ProfileOverviewPage streak cards (#2330)", () => {
	const AUTH = { isAuthenticated: true };

	it("states progress toward a badge the volunteer has not earned yet", async () => {
		api.getMyStreaks.mockResolvedValue({
			loginStreak: 1,
			activityStreak: 1,
			confirmedEngagements: 0,
		});

		renderWithProviders(<ProfileOverviewPage />, { auth: AUTH });

		const streak = await screen.findByTestId("profile-stat-streak");
		// The badge grid on this same page shows both of these locked, so the
		// card may only name what is still missing.
		expect(streak).toHaveTextContent("Weekly Hero: 1 of 4");
		expect(
			await screen.findByTestId("profile-stat-login-streak"),
		).toHaveTextContent("On a Roll: 1 of 7");
	});

	it("names the badge once the streak actually reaches its target", async () => {
		api.getMyStreaks.mockResolvedValue({
			loginStreak: 7,
			activityStreak: 4,
			confirmedEngagements: 0,
		});

		renderWithProviders(<ProfileOverviewPage />, { auth: AUTH });

		const streak = await screen.findByTestId("profile-stat-streak");
		expect(streak).toHaveTextContent("Weekly Hero");
		expect(streak).not.toHaveTextContent("of 4");
	});

	it("gives every stat tile the same width rather than shrink-to-fit", async () => {
		api.getMyStreaks.mockResolvedValue({
			loginStreak: 3,
			activityStreak: 2,
			confirmedEngagements: 0,
		});

		renderWithProviders(<ProfileOverviewPage />, { auth: AUTH });

		const streak = await screen.findByTestId("profile-stat-streak");
		const login = screen.getByTestId("profile-stat-login-streak");
		// jsdom has no layout engine; equal width comes from flex-1 basis-0.
		for (const tile of [streak, login]) {
			expect(tile).toHaveClass("flex-1", "basis-0");
		}
	});
});

describe("ProfileOverviewPage public profile link (#2330)", () => {
	it("links the public profile page the notice promises", async () => {
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true, sub: "user-1" },
		});

		expect(await screen.findByTestId("view-public-profile")).toHaveAttribute(
			"href",
			"/users/user-1",
		);
	});
});

describe("ProfileOverviewPage editor actions (#2330)", () => {
	it("keeps one Save/Cancel pair, pinned to the bottom of the form", async () => {
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});
		await userEvent.click(await screen.findByTestId("profile-edit"));

		const save = await screen.findByTestId("profile-save");
		expect(screen.getAllByRole("button", { name: /^Save$/ })).toHaveLength(1);
		expect(save.closest("form")).not.toBeNull();
		expect(save.parentElement).toHaveClass("sticky", "bottom-0");
	});

	it("submits the form from the pinned Save button", async () => {
		api.updateUserProfile.mockResolvedValue(undefined);
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true },
		});
		await userEvent.click(await screen.findByTestId("profile-edit"));

		await userEvent.click(await screen.findByTestId("profile-save"));

		await waitFor(() => expect(api.updateUserProfile).toHaveBeenCalled());
	});

	it("hands the saved name to the header so the pill stops contradicting it", async () => {
		api.updateUserProfile.mockResolvedValue(undefined);
		renderWithProviders(<ProfileOverviewPage />, {
			auth: { isAuthenticated: true, sub: "user-1" },
		});
		await userEvent.click(await screen.findByTestId("profile-edit"));

		const lastName = document.querySelector("#last-name") as HTMLInputElement;
		await userEvent.clear(lastName);
		await userEvent.type(lastName, "Schmidt-Neuberger");
		await userEvent.click(await screen.findByTestId("profile-save"));

		await waitFor(() =>
			expect(getDisplayNameOverride("user-1")).toBe("Vera Schmidt-Neuberger"),
		);
	});
});

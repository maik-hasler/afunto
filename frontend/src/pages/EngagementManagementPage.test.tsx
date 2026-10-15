import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Outlet, Route, Routes } from "react-router";
import EngagementManagementPage from "./EngagementManagementPage";
import type { OrganizationDetailsResponse } from "../client";
import { renderWithProviders } from "../test/render";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("../test/apiMock");
	return { api: createApiMock() };
});

vi.mock("../hooks/useApiClient", () => ({ useApiClient: () => api }));

const ORG_ID = "11111111-1111-1111-1111-111111111111";
const OPPORTUNITY_ID = "22222222-2222-2222-2222-222222222222";

const org = {
	id: ORG_ID,
	name: "Freiwillige Feuerwehr Kiel",
	members: [],
	requestingUserRole: "Organizer",
	membersUnavailable: false,
	createdOn: new Date(Date.UTC(2026, 0, 1)),
} as unknown as OrganizationDetailsResponse;

const opportunityDetails = {
	id: OPPORTUNITY_ID,
	organizationId: ORG_ID,
	titleDe: "Deutscher Titel",
	titleEn: "English Title",
	descriptionDe: "Beschreibung.",
	status: "Published",
	occurrence: "OneTime",
	participationType: "ScheduledSlots",
	checkInMethod: "None",
	isRemote: true,
	timeSlots: [],
	tags: [],
	createdOn: new Date(Date.UTC(2026, 7, 1)),
};

const engagement = (
	id: string,
	name: string,
	extra: Record<string, unknown> = {},
) => ({
	id,
	volunteerId: "33333333-3333-3333-3333-333333333333",
	volunteerName: name,
	status: "Pending",
	isCheckedIn: false,
	createdOn: new Date(Date.UTC(2026, 7, 10)),
	...extra,
});

function mockPage(items: ReturnType<typeof engagement>[]) {
	api.getEngagements.mockResolvedValue({
		items,
		pageCount: 1,
		totalCount: items.length,
		currentPage: 1,
	});
}

beforeEach(() => {
	api.__reset();
	api.getVolunteerOpportunityDetails.mockResolvedValue(opportunityDetails);

	api.getOpportunityFeedback.mockResolvedValue({
		feedbackCount: 0,
		averageRating: undefined,
		items: { items: [], pageCount: 0 },
	});
	mockPage([]);
});

function renderPage(lng: "de" | "en" = "en", isOrganizer = true) {
	return renderWithProviders(
		<Routes>
			<Route
				element={<Outlet context={{ org, reloadOrg: () => {}, isOrganizer }} />}
			>
				<Route
					path="/app/:organizationId/dashboard/opportunities/:opportunityId/engagements"
					element={<EngagementManagementPage />}
				/>
			</Route>
		</Routes>,
		{
			lng,
			route: `/app/${ORG_ID}/dashboard/opportunities/${OPPORTUNITY_ID}/engagements`,
			auth: { isAuthenticated: true, roles: ["user", "organisator"] },
		},
	);
}

describe("EngagementManagementPage bulk confirm", () => {
	it("reports a partial failure without pretending the whole batch worked", async () => {
		const first = engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera");
		const second = engagement("aaaaaaaa-0000-0000-0000-000000000002", "Olaf");
		mockPage([first, second]);
		api.bulkConfirmEngagements.mockResolvedValue({
			succeeded: [{ id: first.id, status: "Confirmed" }],
			failed: [{ id: second.id, errorCode: "Engagement.NotPending" }],
		});

		renderPage();

		await screen.findByText("Vera");
		await userEvent.click(
			screen.getByRole("checkbox", { name: /Select all/i }),
		);
		await userEvent.click(
			screen.getByRole("button", { name: "Confirm selected" }),
		);

		expect(await screen.findByText("1 confirmed, 1 failed.")).toBeVisible();

		await waitFor(() =>
			expect(
				screen.queryByTestId(`engagement-revoke-${first.id}`),
			).not.toBeNull(),
		);
		expect(screen.queryByTestId(`engagement-revoke-${second.id}`)).toBeNull();
	});
});

describe("EngagementManagementPage for a deleted opportunity", () => {
	it("renders the not-found page rather than an empty management view", async () => {
		api.getEngagements.mockRejectedValue({ status: 404 });

		renderPage();

		expect(
			await screen.findByRole("heading", { level: 1, name: /not found/i }),
		).toBeVisible();
		expect(
			screen.queryByRole("button", { name: "Confirm selected" }),
		).toBeNull();
	});
});

describe("EngagementManagementPage check-in state", () => {
	it("swaps the check-in control for the badge, and back again on undo", async () => {
		const confirmed = engagement(
			"aaaaaaaa-0000-0000-0000-000000000001",
			"Vera",
			{
				status: "Confirmed",
			},
		);
		mockPage([confirmed]);
		api.getVolunteerOpportunityDetails.mockResolvedValue({
			...opportunityDetails,
			checkInMethod: "Manual",
		});
		api.checkInEngagement.mockResolvedValue(undefined);
		api.undoCheckInEngagement.mockResolvedValue(undefined);

		renderPage();

		await screen.findByText("Vera");
		await userEvent.click(
			screen.getByRole("button", { name: "Mark as checked in" }),
		);

		expect(await screen.findByText("Checked in")).toBeVisible();
		expect(
			screen.queryByRole("button", { name: "Mark as checked in" }),
		).toBeNull();

		await userEvent.click(
			screen.getByTestId(`engagement-undo-checkin-${confirmed.id}`),
		);

		expect(
			await screen.findByRole("button", { name: "Mark as checked in" }),
		).toBeVisible();
		expect(screen.queryByText("Checked in")).toBeNull();
	});
});

describe("EngagementManagementPage status filter", () => {
	it("re-fetches by status and shows the empty state when nothing matches", async () => {
		const pending = engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera", {
			status: "Pending",
		});
		const confirmed = engagement(
			"aaaaaaaa-0000-0000-0000-000000000002",
			"Vera",
			{ status: "Confirmed" },
		);
		api.getEngagements.mockImplementation(
			(options: { query: { status?: string } }) => {
				const status = options.query.status;
				const items =
					status === "Pending"
						? [pending]
						: status === "Confirmed"
							? [confirmed]
							: [pending, confirmed];
				return Promise.resolve({
					items,
					pageCount: 1,
					totalCount: items.length,
					currentPage: 1,
				});
			},
		);

		renderPage();

		await screen.findAllByText("Vera");

		await userEvent.selectOptions(screen.getByLabelText("Status"), "Pending");
		expect(await screen.findAllByText("Vera")).toHaveLength(1);

		await userEvent.selectOptions(screen.getByLabelText("Status"), "Confirmed");
		await waitFor(() =>
			expect(api.getEngagements).toHaveBeenLastCalledWith({
				path: { opportunityId: OPPORTUNITY_ID },
				query: {
					pageNumber: 1,
					pageSize: 10,
					status: "Confirmed",
					timeSlotId: undefined,
					search: undefined,
				},
			}),
		);
		expect(await screen.findAllByText("Vera")).toHaveLength(1);
	});
});

describe("EngagementManagementPage confirm button (#2148 core journey)", () => {
	it("moves a pending application to confirmed", async () => {
		const pending = engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera", {
			status: "Pending",
		});
		mockPage([pending]);
		api.confirmEngagement.mockResolvedValue({
			...pending,
			status: "Confirmed",
		});

		renderPage();

		const row = (await screen.findByText("Vera")).closest("li") as HTMLElement;
		expect(within(row).getByText("Pending")).toBeInTheDocument();

		await userEvent.click(
			within(row).getByRole("button", { name: /^Confirm/ }),
		);

		await waitFor(() =>
			expect(within(row).getByText("Confirmed")).toBeInTheDocument(),
		);
		expect(within(row).queryByText("Pending")).toBeNull();
	});
});

describe("EngagementManagementPage in German", () => {
	it("cancels with 'absagen', never 'stornieren'", async () => {
		const confirmed = engagement(
			"aaaaaaaa-0000-0000-0000-000000000001",
			"Vera",
			{
				status: "Confirmed",
			},
		);
		mockPage([confirmed]);

		renderPage("de");

		const revoke = await screen.findByTestId(
			`engagement-revoke-${confirmed.id}`,
		);
		expect(revoke).toHaveTextContent("Absagen");
		expect(document.body.textContent).not.toMatch(/[Ss]tornieren/);

		await userEvent.click(revoke);
		expect(await screen.findByRole("dialog")).toBeVisible();
		expect(api.cancelEngagement).not.toHaveBeenCalled();
	});
});

describe("EngagementManagementPage cancellation reason", () => {
	it("sends the reason the organizer typed", async () => {
		const confirmed = engagement(
			"aaaaaaaa-0000-0000-0000-000000000001",
			"Vera",
			{
				status: "Confirmed",
			},
		);
		mockPage([confirmed]);

		api.cancelEngagement.mockResolvedValue({
			id: confirmed.id,
			status: "Cancelled",
			cancellationReason: "Shift is overstaffed.",
		});

		renderPage();

		await userEvent.click(
			await screen.findByTestId(`engagement-revoke-${confirmed.id}`),
		);

		const dialog = await screen.findByRole("dialog");
		const reason = document.querySelector("#cancel-reason");
		expect(reason).not.toBeNull();
		await userEvent.type(reason as HTMLElement, "Shift is overstaffed.");

		await userEvent.click(
			within(dialog).getByRole("button", { name: /Cancel sign-up|Yes/i }),
		);

		await waitFor(() => expect(api.cancelEngagement).toHaveBeenCalledTimes(1));
		expect(api.cancelEngagement).toHaveBeenCalledWith({
			path: { engagementId: confirmed.id },
			body: expect.objectContaining({ reason: "Shift is overstaffed." }),
		});
	});
});

describe("EngagementManagementPage feedback section", () => {
	it("omits it entirely while no feedback has been submitted", async () => {
		mockPage([engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera")]);

		renderPage();

		await screen.findByText("Vera");
		expect(screen.queryByRole("heading", { name: "Feedback" })).toBeNull();
	});

	it("renders it once there is feedback to show", async () => {
		mockPage([engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera")]);
		api.getOpportunityFeedback.mockResolvedValue({
			feedbackCount: 2,
			averageRating: 4.5,
			items: { items: [], pageCount: 1 },
		});

		renderPage();

		expect(
			await screen.findByRole("heading", { name: "Feedback" }),
		).toBeVisible();
	});
});

describe("EngagementManagementPage check-in PIN", () => {
	it("asks for the PIN exactly once, and only for a PIN-code opportunity", async () => {
		api.getVolunteerOpportunityDetails.mockResolvedValue({
			...opportunityDetails,
			checkInMethod: "PINCode",
		});

		api.getOpportunityCheckInPin.mockResolvedValue("123456");

		renderPage();

		await waitFor(() =>
			expect(api.getOpportunityCheckInPin).toHaveBeenCalledTimes(1),
		);
		await new Promise((resolve) => setTimeout(resolve, 50));
		expect(api.getOpportunityCheckInPin).toHaveBeenCalledTimes(1);
	});

	it("never asks for it when the opportunity does not use one", async () => {
		mockPage([engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera")]);

		renderPage();

		await screen.findByText("Vera");
		expect(api.getOpportunityCheckInPin).not.toHaveBeenCalled();
	});

	it("never asks for it as a plain member, who would only get a 403", async () => {
		api.getVolunteerOpportunityDetails.mockResolvedValue({
			...opportunityDetails,
			checkInMethod: "PINCode",
		});

		mockPage([engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera")]);

		renderPage("en", false);

		await screen.findByText("Vera");
		expect(api.getOpportunityCheckInPin).not.toHaveBeenCalled();
	});
});

describe("EngagementManagementPage public profile link", () => {
	it("links a named applicant's row to their public profile", async () => {
		const vera = engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera");
		mockPage([vera]);

		renderPage();

		const link = await screen.findByRole("link", {
			name: "View Vera's public profile",
		});
		expect(link).toHaveAttribute("href", `/users/${vera.volunteerId}`);
	});

	it("gives an anonymized applicant no profile link", async () => {
		mockPage([
			engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera", {
				volunteerId: undefined,
				volunteerName: undefined,
			}),
		]);

		renderPage();

		await screen.findByText("Anonymized volunteer");
		expect(screen.queryByRole("link", { name: /public profile/i })).toBeNull();
	});
});

describe("EngagementManagementPage meetings (#2413)", () => {
	const interestOpportunity = {
		...opportunityDetails,
		participationType: "IndividualContact",
	};
	const NEXT_YEAR = new Date().getUTCFullYear() + 1;
	const groupMeeting = {
		id: "44444444-4444-4444-4444-444444444444",
		startsAt: new Date(`${NEXT_YEAR}-03-10T17:00:00Z`),
		endsAt: new Date(`${NEXT_YEAR}-03-10T18:00:00Z`),
		location: "Clubhouse",
		note: null,
		proposedBy: "Organization",
		response: "Pending",
		participantCount: 2,
	};

	it("offers no meetings for a time-slot opportunity", async () => {
		mockPage([engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera")]);

		renderPage();

		await screen.findByText("Vera");
		expect(
			screen.queryByRole("button", { name: /Propose a meeting/ }),
		).not.toBeInTheDocument();
	});

	it("proposes one group meeting to everyone selected", async () => {
		api.getVolunteerOpportunityDetails.mockResolvedValue(interestOpportunity);
		const first = engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera");
		const second = engagement("aaaaaaaa-0000-0000-0000-000000000002", "Olaf");
		mockPage([first, second]);
		api.proposeMeeting.mockResolvedValue({ meetingId: groupMeeting.id });

		renderPage();

		await screen.findByRole("button", { name: "Propose a meeting with Vera" });
		await userEvent.click(
			screen.getByRole("checkbox", { name: /Select all/i }),
		);
		await userEvent.click(
			screen.getByRole("button", {
				name: "Propose a group meeting with 2 people",
			}),
		);
		const dialog = await screen.findByRole("dialog");
		expect(
			within(dialog).getByText(
				"All 2 people get the same proposal and each answers for themselves.",
			),
		).toBeVisible();
		await userEvent.click(
			within(dialog).getByRole("button", { name: "Cancel" }),
		);
		expect(api.proposeMeeting).not.toHaveBeenCalled();
	});

	it("moves a group meeting for everyone who still has it", async () => {
		api.getVolunteerOpportunityDetails.mockResolvedValue(interestOpportunity);
		mockPage([
			engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera", {
				meeting: groupMeeting,
			}),
		]);
		api.proposeMeeting.mockResolvedValue({ meetingId: "new" });

		renderPage();

		await userEvent.click(
			await screen.findByRole("button", {
				name: "Move the meeting with Vera",
			}),
		);
		const dialog = await screen.findByRole("dialog");
		await userEvent.click(
			within(dialog).getByRole("button", { name: "Send proposal" }),
		);

		await waitFor(() =>
			expect(api.proposeMeeting).toHaveBeenCalledWith({
				path: { opportunityId: OPPORTUNITY_ID },
				body: expect.objectContaining({
					engagementIds: [],
					replacesMeetingId: groupMeeting.id,
					startsAt: groupMeeting.startsAt,
					endsAt: groupMeeting.endsAt,
					location: "Clubhouse",
				}),
			}),
		);
	});

	it("answers a volunteer's proposal", async () => {
		api.getVolunteerOpportunityDetails.mockResolvedValue(interestOpportunity);
		mockPage([
			engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera", {
				meeting: {
					...groupMeeting,
					proposedBy: "Volunteer",
					participantCount: 1,
				},
			}),
		]);

		renderPage();

		await userEvent.click(
			await screen.findByRole("button", {
				name: "Accept the meeting with Vera",
			}),
		);

		await waitFor(() =>
			expect(api.respondToMeetingAsOrganizer).toHaveBeenCalledWith({
				path: { opportunityId: OPPORTUNITY_ID, meetingId: groupMeeting.id },
				body: { accept: true },
			}),
		);
	});

	it("cancels a meeting after asking, naming how many will hear about it", async () => {
		api.getVolunteerOpportunityDetails.mockResolvedValue(interestOpportunity);
		mockPage([
			engagement("aaaaaaaa-0000-0000-0000-000000000001", "Vera", {
				meeting: groupMeeting,
			}),
		]);

		renderPage();

		await userEvent.click(
			await screen.findByRole("button", {
				name: "Cancel the meeting with Vera",
			}),
		);
		const dialog = await screen.findByRole("dialog");
		expect(
			within(dialog).getByText("All 2 people invited will get a notification."),
		).toBeVisible();
		await userEvent.click(
			within(dialog).getByRole("button", { name: "Cancel meeting" }),
		);

		await waitFor(() =>
			expect(api.cancelMeetingAsOrganizer).toHaveBeenCalledWith({
				path: { opportunityId: OPPORTUNITY_ID, meetingId: groupMeeting.id },
			}),
		);
	});
});

import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import CreateVolunteerOpportunityModal from "./CreateVolunteerOpportunityModal";
import { renderWithProviders } from "../test/render";
import { pickDateTime } from "../test/pickDateTime";
import { expectNoA11yViolations } from "../test/a11y";

const { api } = vi.hoisted(() => ({
	api: {
		createVolunteerOpportunity: vi.fn(),
		updateVolunteerOpportunity: vi.fn(),
		createTimeSlot: vi.fn(),
		publishVolunteerOpportunity: vi.fn(),
		uploadOpportunityBanner: vi.fn(),
		getVolunteerOpportunityDetails: vi.fn(),
		getOrganizationDetails: vi.fn(),
	},
}));

vi.mock("../hooks/useApiClient", () => ({ useApiClient: () => api }));

beforeEach(() => {
	vi.clearAllMocks();
	api.getOrganizationDetails.mockResolvedValue({
		id: "org-1",
		name: "Freiwillige Feuerwehr Kiel",
		address: {
			street: "Strandweg",
			houseNumber: "1",
			zipCode: "24103",
			city: "Kiel",
		},
	});
});

function open() {
	return renderWithProviders(
		<CreateVolunteerOpportunityModal
			organizationId="org-1"
			onClose={() => {}}
			onSuccess={() => {}}
		/>,
		{ auth: { isAuthenticated: true } },
	);
}

describe("CreateVolunteerOpportunityModal a11y", () => {
	it("has no violations on the first step", async () => {
		open();
		expect(screen.getByRole("dialog")).toBeInTheDocument();
		await expectNoA11yViolations();
	});

	it("has no violations once a blocked Next has produced an inline error", async () => {
		open();
		await userEvent.click(screen.getByRole("button", { name: "Next" }));

		await waitFor(() =>
			expect(document.querySelector('[aria-invalid="true"]')).not.toBeNull(),
		);
		await expectNoA11yViolations();
	});

	async function gotoDetails() {
		await userEvent.type(
			screen.getAllByRole("textbox", { name: /title/i })[0],
			"Strandreinigung",
		);
		await userEvent.type(
			screen.getAllByRole("textbox", { name: /description/i })[0],
			"Wir sammeln einen Vormittag lang Muell am Strand.",
		);
		for (const stepTitle of ["Location", "Format", "Details"]) {
			await userEvent.click(screen.getByRole("button", { name: "Next" }));
			await waitFor(() =>
				screen.getByRole("heading", { name: new RegExp(stepTitle, "i") }),
			);
		}
	}

	async function openSlotEditor() {
		await userEvent.click(
			screen.getByRole("button", { name: "Add time slot" }),
		);
		await screen.findByTestId("time-slot-editor");
	}

	async function addSlot(start: string, end: string) {
		await openSlotEditor();
		await pickDateTime("slot-start", start);
		await pickDateTime("slot-end", end);
		await userEvent.click(screen.getByRole("button", { name: "Add" }));
	}

	// See the equivalent test in CreateVolunteerOpportunityModal.test.tsx for
	// why this no longer tries to enter a past-dated slot: `DatePicker` marks
	// every day before today `aria-disabled`, so it can't be picked at all.
	it("has no violations with a day disabled for being in the past (#2325)", async () => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		try {
			vi.setSystemTime(new Date("2026-09-10T06:00:00Z"));
			open();
			await gotoDetails();
			await openSlotEditor();

			await userEvent.click(screen.getByTestId("slot-start-trigger"));
			const grid = await screen.findByRole("grid");
			expect(within(grid).getByText("9").closest("button")).toHaveAttribute(
				"aria-disabled",
				"true",
			);
			await expectNoA11yViolations();
		} finally {
			vi.useRealTimers();
		}
	});

	it("has no violations while the list and the slot editor flag an overlap (#2325)", async () => {
		// Fixed clock-face fixture times stand in for "in the future" - pin the
		// wall clock behind them, same as the past-dated-day test above, or
		// DatePicker's future-only day guard (#2325) refuses every pick below.
		vi.useFakeTimers({ shouldAdvanceTime: true });
		try {
			vi.setSystemTime(new Date("2026-09-10T06:00:00Z"));
			open();
			await gotoDetails();
			await addSlot("2026-09-10T10:00", "2026-09-10T12:00");
			await addSlot("2026-09-10T11:00", "2026-09-10T13:00");

			// The list and the slot editor (#2388) are never on screen together,
			// so each gets its own scan.
			await waitFor(() =>
				expect(screen.getAllByText("Overlaps another time slot")).toHaveLength(
					2,
				),
			);
			await expectNoA11yViolations();

			await openSlotEditor();
			await pickDateTime("slot-start", "2026-09-10T11:30");
			await pickDateTime("slot-end", "2026-09-10T12:30");
			expect(
				await screen.findByText(
					"This overlaps a time slot already on the list - a volunteer could sign up for both.",
				),
			).toBeInTheDocument();
			await expectNoA11yViolations();
		} finally {
			vi.useRealTimers();
		}
	});

	it("has no violations while a banner can be replaced or removed (#2325)", async () => {
		renderWithProviders(
			<CreateVolunteerOpportunityModal
				organizationId="org-1"
				initialOpportunity={
					{
						id: "existing-opp-id",
						organizationId: "org-1",
						titleDe: "Bestehende Chance",
						descriptionDe: "Beschreibung.",
						isRemote: true,
						occurrence: "OneTime",
						participationType: "ScheduledSlots",
						checkInMethod: "None",
						tags: [],
						timeSlots: [],
						bannerImageUrl: "https://storage.example/banner.jpg",
					} as unknown as Parameters<
						typeof CreateVolunteerOpportunityModal
					>[0]["initialOpportunity"]
				}
				onClose={() => {}}
				onSuccess={() => {}}
			/>,
			{ auth: { isAuthenticated: true } },
		);

		expect(await screen.findByLabelText("Replace")).toBeInTheDocument();
		await expectNoA11yViolations();
	});

	it("has no violations on every step of the wizard", async () => {
		open();

		await userEvent.type(
			screen.getAllByRole("textbox", { name: /title/i })[0],
			"Strandreinigung",
		);
		await userEvent.type(
			screen.getAllByRole("textbox", { name: /description/i })[0],
			"Wir sammeln einen Vormittag lang Muell am Strand.",
		);

		for (const stepTitle of ["Location", "Format", "Details"]) {
			await userEvent.click(screen.getByRole("button", { name: "Next" }));
			const reached = await waitFor(() =>
				screen.getByRole("heading", { name: new RegExp(stepTitle, "i") }),
			);
			expect(reached).toBeInTheDocument();
			await expectNoA11yViolations();
		}
	});
});

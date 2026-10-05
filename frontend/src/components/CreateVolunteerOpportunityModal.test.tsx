import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import CreateVolunteerOpportunityModal from "./CreateVolunteerOpportunityModal";
import { renderWithProviders } from "../test/render";
import { pickDate, pickDateTime } from "../test/pickDateTime";
import { formatDate } from "../lib/format";

const { api } = vi.hoisted(() => ({
	api: {
		getOrganizationDetails: vi.fn(),
		createVolunteerOpportunity: vi.fn(),
		updateVolunteerOpportunity: vi.fn(),
		createTimeSlot: vi.fn(),
		publishVolunteerOpportunity: vi.fn(),
		uploadOpportunityBanner: vi.fn(),
		getVolunteerOpportunityDetails: vi.fn(),
		getOpportunityCheckInPin: vi.fn(),
		deleteTimeSlot: vi.fn(),
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

function openWizard() {
	return renderWithProviders(
		<CreateVolunteerOpportunityModal
			organizationId="org-1"
			onClose={() => {}}
			onSuccess={() => {}}
		/>,
		{ auth: { isAuthenticated: true } },
	);
}

const title = () => document.querySelector("#opportunity-title") as HTMLElement;
const description = () =>
	document.querySelector("#opportunity-description") as HTMLElement;
const titleError = () => document.querySelector("#opportunity-title-error");
const descriptionError = () =>
	document.querySelector("#opportunity-description-error");
const blocked = () =>
	document.querySelector("#create-opportunity-step-blocked");

describe("create-opportunity wizard: focus on the first invalid field (#2077)", () => {
	it("focuses the title when both required fields are blank", async () => {
		openWizard();
		await userEvent.click(screen.getByTestId("modal-next"));

		await waitFor(() =>
			expect(titleError()).toHaveTextContent(
				"Enter a title - it appears in search.",
			),
		);
		expect(title()).toHaveFocus();
	});

	it("focuses the description when only it is blank, not the title", async () => {
		openWizard();
		await userEvent.type(title(), "Focus regression test");
		await userEvent.click(screen.getByTestId("modal-next"));

		await waitFor(() =>
			expect(descriptionError()).toHaveTextContent(
				"Describe briefly what volunteers can expect.",
			),
		);
		expect(description()).toHaveFocus();
		expect(titleError()).toBeNull();
	});

	it("focuses the blocking field when a stepper jump is refused", async () => {
		openWizard();
		await userEvent.click(screen.getByTestId("wizard-stepper-4"));

		await waitFor(() => expect(blocked()).not.toBeNull());
		expect(screen.getByTestId("wizard-step-1")).toBeInTheDocument();
		expect(title()).toHaveFocus();
	});
});

describe("create-opportunity wizard: language tab error indicator (#2234)", () => {
	it("exposes the error on the German tab through its accessible name, not colour alone", async () => {
		openWizard();
		await userEvent.click(screen.getByTestId("modal-next"));

		await waitFor(() => expect(titleError()).not.toBeNull());

		const deTab = screen.getByTestId("opportunity-content-language-de");
		expect(deTab).toHaveAccessibleName(/Contains errors/);
	});

	it("carries no error indicator on the English tab, which has none", async () => {
		openWizard();
		await userEvent.click(screen.getByTestId("modal-next"));

		await waitFor(() => expect(titleError()).not.toBeNull());

		const enTab = screen.getByTestId("opportunity-content-language-en");
		expect(enTab).not.toHaveAccessibleName(/Contains errors/);
	});
});

describe("create-opportunity wizard: blocked stepper jumps (#1782)", () => {
	it("names the step standing in the way, in an assertive live region tied to the refused control", async () => {
		openWizard();
		await userEvent.click(screen.getByTestId("wizard-stepper-4"));

		await waitFor(() => expect(blocked()).not.toBeNull());
		const message = blocked();
		expect(message).toHaveAttribute("role", "alert");
		expect(message).toHaveAttribute("aria-live", "assertive");
		expect(message).toHaveTextContent("Step 4 is not available yet");
		expect(message).toHaveTextContent("step 1 (Basics)");

		expect(screen.getByTestId("wizard-stepper-4")).toHaveAttribute(
			"aria-describedby",
			"create-opportunity-step-blocked",
		);

		expect(screen.getByTestId("wizard-step-1")).toBeInTheDocument();
		expect(screen.queryByTestId("wizard-step-4")).toBeNull();
	});

	it("retires the refusal once the named step is fixed, and lets the same click through", async () => {
		openWizard();
		await userEvent.click(screen.getByTestId("wizard-stepper-4"));
		await waitFor(() => expect(blocked()).not.toBeNull());

		await userEvent.type(title(), "Blocked step jump regression");
		await userEvent.type(description(), "Regression test for #1782.");
		await userEvent.tab();

		await waitFor(() => expect(blocked()).toBeNull());
		expect(screen.getByTestId("wizard-stepper-4")).not.toHaveAttribute(
			"aria-describedby",
			"create-opportunity-step-blocked",
		);

		await userEvent.click(screen.getByTestId("wizard-stepper-4"));
		await waitFor(() =>
			expect(screen.getByTestId("wizard-step-4")).toBeInTheDocument(),
		);
		expect(blocked()).toBeNull();
	});

	it("names an intermediate step rather than the one being looked at", async () => {
		openWizard();
		await userEvent.type(title(), "Intermediate step block");
		await userEvent.type(description(), "Regression test for #1782.");

		await userEvent.click(screen.getByTestId("wizard-stepper-2"));
		await waitFor(() =>
			expect(screen.getByTestId("wizard-step-2")).toBeInTheDocument(),
		);
		const city = document.querySelector("#opportunity-city") as HTMLElement;
		await userEvent.clear(city);

		await userEvent.click(screen.getByTestId("wizard-stepper-1"));
		await waitFor(() =>
			expect(screen.getByTestId("wizard-step-1")).toBeInTheDocument(),
		);
		await userEvent.click(screen.getByTestId("wizard-stepper-4"));

		await waitFor(() =>
			expect(blocked()).toHaveTextContent("step 2 (Location)"),
		);
		expect(screen.getByTestId("wizard-step-1")).toBeInTheDocument();
	});
});

describe("create-opportunity wizard: live revalidation (#1928)", () => {
	it("clears a field's error as it is fixed, without a second Next click", async () => {
		openWizard();
		await userEvent.click(screen.getByTestId("modal-next"));

		await waitFor(() =>
			expect(titleError()).toHaveTextContent(
				"Enter a title - it appears in search.",
			),
		);
		expect(descriptionError()).toHaveTextContent(
			"Describe briefly what volunteers can expect.",
		);
		expect(title()).toHaveAttribute("aria-invalid", "true");

		await userEvent.type(title(), "Erste-Hilfe-Kurs fuer Anfaenger");

		await waitFor(() => expect(titleError()).toBeNull());
		expect(title()).not.toHaveAttribute("aria-invalid");

		expect(descriptionError()).toHaveTextContent(
			"Describe briefly what volunteers can expect.",
		);
	});
});

describe("create-opportunity wizard: the shared required-field marker (#1797)", () => {
	it("marks the title field with the one aria-hidden asterisk, once explained per form", async () => {
		openWizard();

		const field = document.querySelector("#opportunity-title");
		expect(field).toHaveAccessibleName("Title");

		const label = document.querySelector("label[for='opportunity-title']");
		expect(label).toHaveTextContent(/^Title\*$/);
		const marks = label?.querySelectorAll("span[aria-hidden='true']") ?? [];
		expect(marks).toHaveLength(1);
		expect(marks[0]).toHaveTextContent("*");

		const legends = Array.from(document.querySelectorAll("p")).filter(
			(p) => p.textContent?.trim() === "* Required field",
		);
		expect(legends).toHaveLength(1);
	});
});

describe("create-opportunity wizard: distinct control names (#1957)", () => {
	it("names the header close button differently from the footer cancel button", () => {
		openWizard();

		const dialog = screen.getByRole("dialog");
		expect(within(dialog).getByTestId("modal-cancel")).toHaveTextContent(
			"Cancel",
		);
		expect(
			within(dialog).getByRole("button", { name: "Close" }),
		).toBeInTheDocument();
		expect(
			within(dialog).getAllByRole("button", { name: "Cancel" }),
		).toHaveLength(1);
	});
});

describe("create-opportunity wizard: shape and the draft gate", () => {
	it("offers all four steps and a draft button, with no accent band", async () => {
		openWizard();

		for (const n of [1, 2, 3, 4]) {
			expect(await screen.findByTestId(`wizard-stepper-${n}`)).toBeVisible();
		}
		expect(screen.getByTestId("modal-save-draft")).toBeInTheDocument();
		expect(document.querySelector(".from-brand-600")).toBeNull();
	});

	it("lets a step be reached directly, once the step before it validates", async () => {
		openWizard();

		await userEvent.click(await screen.findByTestId("wizard-stepper-2"));

		await waitFor(() => expect(titleError()).not.toBeNull());
		expect(screen.getByTestId("wizard-stepper-1")).toHaveAttribute(
			"aria-current",
			"step",
		);

		await userEvent.type(title(), "Deutscher Titel");
		await userEvent.type(description(), "Eine ausreichend lange Beschreibung.");
		await userEvent.click(screen.getByTestId("wizard-stepper-2"));

		await waitFor(() =>
			expect(screen.getByTestId("wizard-stepper-2")).toHaveAttribute(
				"aria-current",
				"step",
			),
		);
	});

	it("keeps Save draft disabled until a real title is typed", async () => {
		openWizard();

		const saveDraft = await screen.findByTestId("modal-save-draft");
		expect(saveDraft).toBeDisabled();

		await userEvent.type(title(), "   ");
		expect(saveDraft).toBeDisabled();

		await userEvent.clear(title());
		await userEvent.type(title(), "Deutscher Titel");
		await waitFor(() => expect(saveDraft).toBeEnabled());
	});
});

async function fillBasicsAndFormat(pinCode: string) {
	await userEvent.type(title(), "PIN Test Opportunity");
	await userEvent.type(
		description(),
		"Regression test for #549 organizer-settable check-in PIN.",
	);
	await userEvent.click(screen.getByTestId("wizard-stepper-2"));
	await userEvent.click(
		await screen.findByLabelText(/remote|Remote/i, { selector: "input" }),
	);
	await userEvent.click(screen.getByTestId("wizard-stepper-3"));
	await userEvent.click(
		await screen.findByRole("radio", { name: "Express interest" }),
	);
	await userEvent.click(screen.getByRole("radio", { name: "PIN code" }));
	const pinInput = screen.getByLabelText(/Check-in PIN/i);
	await userEvent.clear(pinInput);
	await userEvent.type(pinInput, pinCode);
	return pinInput;
}

describe("create-opportunity wizard: organizer-set check-in PIN (#549)", () => {
	it("sends the organizer-typed PIN exactly, in the create payload", async () => {
		openWizard();
		await fillBasicsAndFormat("482170");

		await userEvent.click(screen.getByTestId("wizard-stepper-4"));
		await screen.findByLabelText(/Interest deadline/i);
		await pickDate("create-valid-until", "2027-06-30");

		api.createVolunteerOpportunity.mockResolvedValue({ id: "new-opp-id" });
		await userEvent.click(screen.getByTestId("modal-submit"));

		await waitFor(() =>
			expect(api.createVolunteerOpportunity).toHaveBeenCalledWith({
				body: expect.objectContaining({ checkInPin: "482170" }),
			}),
		);
	});

	it("prefills the existing PIN on edit, and Generate random replaces it with a different one", async () => {
		api.getOpportunityCheckInPin.mockResolvedValue("135790");
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
						participationType: "IndividualContact",
						checkInMethod: "PINCode",
						category: undefined,
						tags: [],
						validUntil: new Date(Date.UTC(2027, 5, 30)),
					} as unknown as Parameters<
						typeof CreateVolunteerOpportunityModal
					>[0]["initialOpportunity"]
				}
				onClose={() => {}}
				onSuccess={() => {}}
			/>,
			{ auth: { isAuthenticated: true } },
		);

		await userEvent.click(await screen.findByTestId("wizard-stepper-3"));
		const pinInput = await screen.findByLabelText(/Check-in PIN/i);
		await waitFor(() => expect(pinInput).toHaveValue("135790"));

		await userEvent.click(
			screen.getByRole("button", { name: /Generate random/i }),
		);
		const generatedPin = (pinInput as HTMLInputElement).value;
		expect(generatedPin).toMatch(/^\d{6}$/);
		expect(generatedPin).not.toBe("135790");

		await userEvent.click(screen.getByTestId("wizard-stepper-4"));
		api.updateVolunteerOpportunity.mockResolvedValue(undefined);
		await userEvent.click(screen.getByTestId("modal-submit"));

		await waitFor(() =>
			expect(api.updateVolunteerOpportunity).toHaveBeenCalledWith({
				path: { opportunityId: "existing-opp-id" },
				body: expect.objectContaining({ checkInPin: generatedPin }),
			}),
		);
	});
});

describe("create-opportunity wizard: an address the backend cannot locate (#2320)", () => {
	it("sends the organizer back to the address step with the fields flagged", async () => {
		api.createVolunteerOpportunity.mockRejectedValue({
			status: 400,
			errorCode: "Address.NotGeocodable",
			detail:
				"Address could not be located. Please check the street, house number, zip code, and city.",
		});
		openWizard();

		await userEvent.type(title(), "Deutscher Titel");
		await userEvent.click(await screen.findByTestId("modal-save-draft"));

		await waitFor(() =>
			expect(screen.getByTestId("wizard-stepper-2")).toHaveAttribute(
				"aria-current",
				"step",
			),
		);
		// The banner's wording comes from getApiErrorMessage against the app's
		// own i18n singleton, which a component test does not drive - that
		// mapping is covered in lib/apiError.test.ts. What matters here is that
		// the four address fields are the ones flagged, on their own step.
		expect(screen.getAllByText("Check this entry")).toHaveLength(4);
		for (const id of [
			"#opportunity-street",
			"#opportunity-house",
			"#opportunity-zip",
			"#opportunity-city",
		]) {
			expect(document.querySelector(id)).toHaveAttribute(
				"aria-invalid",
				"true",
			);
		}
	});

	it("leaves the step alone for a failure that is not about the address", async () => {
		api.createVolunteerOpportunity.mockRejectedValue({ status: 500 });
		openWizard();

		await userEvent.type(title(), "Deutscher Titel");
		await userEvent.click(await screen.findByTestId("modal-save-draft"));

		await waitFor(() =>
			expect(screen.getByTestId("wizard-stepper-1")).toHaveAttribute(
				"aria-current",
				"step",
			),
		);
		expect(document.querySelector("#opportunity-street")).toBeNull();
	});
});

describe("create-opportunity wizard: a time slot that ends before it starts (#2320)", () => {
	it("says the end must be after the start, and marks both inputs", async () => {
		openWizard();

		await userEvent.type(title(), "Zeitslot Regression");
		await userEvent.type(description(), "Regression test for #2320.");
		await userEvent.click(await screen.findByTestId("wizard-stepper-4"));
		await waitFor(() =>
			expect(screen.getByTestId("wizard-step-4")).toBeInTheDocument(),
		);

		await pickDateTime("slot-start", "2027-06-01T12:00");
		await pickDateTime("slot-end", "2027-06-01T09:00");
		await userEvent.click(screen.getByRole("button", { name: "Add" }));

		const start = document.querySelector("#slot-start") as HTMLElement;
		const end = document.querySelector("#slot-end") as HTMLElement;
		expect(
			await screen.findByText("End date must be after start date."),
		).toBeInTheDocument();
		expect(start).toHaveAttribute("aria-invalid", "true");
		expect(end).toHaveAttribute("aria-invalid", "true");
		// Also references the "pick a date, then a time" hint (#2373 follow-up) -
		// aria-describedby accepts a space-separated list of ids, and a screen
		// reader announces every one of them.
		expect(end.getAttribute("aria-describedby")?.split(" ")).toEqual(
			expect.arrayContaining(["time-slot-error"]),
		);
	});
});

describe("edit wizard: time slot changes are not staged (#2315)", () => {
	const SLOT = {
		id: "slot-1",
		startDateTime: new Date(Date.UTC(2027, 5, 1, 7, 0)),
		endDateTime: new Date(Date.UTC(2027, 5, 1, 9, 0)),
		maxParticipants: 4,
		bookedCount: 0,
	};

	function openEditWizard(slot: Record<string, unknown> = SLOT) {
		return renderWithProviders(
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
						category: undefined,
						tags: [],
						validUntil: undefined,
						timeSlots: [slot],
					} as unknown as Parameters<
						typeof CreateVolunteerOpportunityModal
					>[0]["initialOpportunity"]
				}
				onClose={() => {}}
				onSuccess={() => {}}
			/>,
			{ auth: { isAuthenticated: true } },
		);
	}

	const IMMEDIATE_HINT =
		"Time slot changes take effect immediately - Save and Cancel below do not cover them.";

	it("says up front that slot changes do not wait for Save", async () => {
		openEditWizard();
		await userEvent.click(await screen.findByTestId("wizard-stepper-4"));

		expect(await screen.findByText(IMMEDIATE_HINT)).toBeInTheDocument();
	});

	it("stays silent about that while creating, where slots really are staged", async () => {
		openWizard();
		await userEvent.type(title(), "Neue Chance");
		await userEvent.type(description(), "Eine ausreichend lange Beschreibung.");
		await userEvent.click(screen.getByTestId("wizard-stepper-2"));
		await userEvent.click(
			await screen.findByLabelText(/remote|Remote/i, { selector: "input" }),
		);
		await userEvent.click(screen.getByTestId("wizard-stepper-4"));

		await screen.findByTestId("wizard-step-4");
		expect(screen.queryByText(IMMEDIATE_HINT)).toBeNull();
	});

	it("asks before deleting a slot that is already on the server", async () => {
		openEditWizard();
		await userEvent.click(await screen.findByTestId("wizard-stepper-4"));

		await userEvent.click(
			await screen.findByRole("button", { name: "Remove" }),
		);

		const dialog = await screen.findByRole("dialog", {
			name: "Remove time slot?",
		});
		expect(api.deleteTimeSlot).not.toHaveBeenCalled();

		await userEvent.click(within(dialog).getByRole("button", { name: "Keep" }));
		await waitFor(() =>
			expect(
				screen.queryByRole("dialog", { name: "Remove time slot?" }),
			).toBeNull(),
		);
		expect(api.deleteTimeSlot).not.toHaveBeenCalled();
		expect(screen.getByRole("button", { name: "Remove" })).toBeInTheDocument();
	});

	it("deletes the slot once the removal is confirmed", async () => {
		api.deleteTimeSlot.mockResolvedValue({ deletedTimeSlotIds: ["slot-1"] });
		openEditWizard();
		await userEvent.click(await screen.findByTestId("wizard-stepper-4"));

		await userEvent.click(
			await screen.findByRole("button", { name: "Remove" }),
		);
		await userEvent.click(
			within(
				await screen.findByRole("dialog", { name: "Remove time slot?" }),
			).getByRole("button", { name: "Yes, remove" }),
		);

		await waitFor(() =>
			expect(api.deleteTimeSlot).toHaveBeenCalledWith({
				path: { opportunityId: "existing-opp-id", timeSlotId: "slot-1" },
				query: { scope: "Only" },
			}),
		);
		await waitFor(() =>
			expect(screen.queryByRole("button", { name: "Remove" })).toBeNull(),
		);
	});

	it("keeps the dialog open, with the reason, when the delete fails", async () => {
		api.deleteTimeSlot.mockRejectedValue(new Error("500"));
		openEditWizard();
		await userEvent.click(await screen.findByTestId("wizard-stepper-4"));

		await userEvent.click(
			await screen.findByRole("button", { name: "Remove" }),
		);
		await userEvent.click(
			within(
				await screen.findByRole("dialog", { name: "Remove time slot?" }),
			).getByRole("button", { name: "Yes, remove" }),
		);

		const dialog = await screen.findByRole("dialog", {
			name: "Remove time slot?",
		});
		expect(
			within(dialog).getByText("Could not remove time slot."),
		).toBeInTheDocument();

		await userEvent.click(
			within(dialog).getByRole("button", { name: "Understood" }),
		);
		expect(screen.getByRole("button", { name: "Remove" })).toBeInTheDocument();
	});

	it("spells out that volunteers lose their spot when the slot is booked", async () => {
		openEditWizard({ ...SLOT, bookedCount: 2 });
		await userEvent.click(await screen.findByTestId("wizard-stepper-4"));

		await userEvent.click(
			await screen.findByRole("button", { name: "Remove" }),
		);

		const dialog = await screen.findByRole("dialog", {
			name: "Remove time slot?",
		});
		expect(
			within(dialog).getByText(
				/Volunteers signed up for the affected occurrences/,
			),
		).toBeInTheDocument();
	});

	it("tells the closing organizer which changes the discard does not cover", async () => {
		api.deleteTimeSlot.mockResolvedValue({ deletedTimeSlotIds: ["slot-1"] });
		openEditWizard();
		await userEvent.click(await screen.findByTestId("wizard-stepper-4"));

		await userEvent.click(
			await screen.findByRole("button", { name: "Remove" }),
		);
		await userEvent.click(
			within(
				await screen.findByRole("dialog", { name: "Remove time slot?" }),
			).getByRole("button", { name: "Yes, remove" }),
		);
		await waitFor(() => expect(api.deleteTimeSlot).toHaveBeenCalled());

		await userEvent.click(await screen.findByTestId("wizard-stepper-1"));
		await waitFor(() => expect(title()).toBeInTheDocument());
		await userEvent.type(title(), " geaendert");
		await userEvent.click(screen.getByRole("button", { name: "Close" }));

		const discard = await screen.findByRole("dialog", {
			name: "Discard unsaved changes?",
		});
		expect(
			within(discard).getByText(
				"Your time slot changes were already saved and stay in place - only the other edits are discarded.",
			),
		).toBeInTheDocument();
	});
});

describe("create-opportunity wizard: time slot guards (#2325)", () => {
	const slotStart = () => document.querySelector("#slot-start") as HTMLElement;
	const slotMax = () => document.querySelector("#slot-max") as HTMLInputElement;
	const slotRows = () =>
		within(screen.getByTestId("time-slot-list")).getAllByRole("listitem");

	// Every fixture below is a fixed clock-face value on a fixed date standing
	// in for "a time slot in the future". `DatePicker` refuses a day before
	// today and the wizard refuses a start in the past (#2325), so the wall
	// clock is pinned behind all of them - left on the real clock, each fixture
	// silently turns into a past date the day it is reached.
	beforeEach(() => {
		vi.useFakeTimers({ shouldAdvanceTime: true });
		vi.setSystemTime(new Date("2026-09-10T06:00:00Z"));
	});

	afterEach(() => {
		vi.useRealTimers();
	});

	async function gotoTimeSlots() {
		openWizard();
		await userEvent.type(title(), "Zeitfenster");
		await userEvent.type(description(), "Eine ausreichend lange Beschreibung.");
		await userEvent.click(screen.getByTestId("wizard-stepper-2"));
		await userEvent.click(
			await screen.findByLabelText(/remote|Remote/i, { selector: "input" }),
		);
		await userEvent.click(screen.getByTestId("wizard-stepper-4"));
		await screen.findByTestId("wizard-step-4");
	}

	async function addSlot(start: string, end: string, max?: string) {
		await pickDateTime("slot-start", start);
		await pickDateTime("slot-end", end);
		if (max !== undefined)
			fireEvent.change(slotMax(), { target: { value: max } });
		await userEvent.click(screen.getByRole("button", { name: "Add" }));
	}

	// #2373 follow-up: the "pick a date, then a time - both are required" hint
	// used to be plain, sequential prose with no ARIA relationship to the
	// fields it explains. Wiring it via aria-describedby (the same pattern
	// this file already uses for the slot-error message) means a screen-reader
	// user hears it however they land on the field, not only by reading
	// forward through the DOM.
	it("describes the Start/End fields with the date-then-time hint", async () => {
		await gotoTimeSlots();

		const hint = screen.getByText(
			"Choose a date first, then set the time - both are required before this slot can be added.",
		);
		expect(hint).toHaveAttribute("id", "add-slot-datetime-hint");
		expect(slotStart().getAttribute("aria-describedby")?.split(" ")).toEqual(
			expect.arrayContaining(["add-slot-datetime-hint"]),
		);
		const slotEnd = document.querySelector("#slot-end") as HTMLElement;
		expect(slotEnd.getAttribute("aria-describedby")?.split(" ")).toEqual(
			expect.arrayContaining(["add-slot-datetime-hint"]),
		);
	});

	// The organizer-facing "Start date must be in the future." guard (#2325)
	// used to be reachable by typing an out-of-range value straight into a
	// native `<input type="datetime-local">`. `DatePicker` closed that path at
	// the source instead: a day before today is `aria-disabled` in the
	// calendar grid and cannot be picked at all, so there is no longer a
	// past-dated value for the guard to catch. This asserts the replacement
	// guarantee - the day is unpickable in the first place - rather than a
	// scenario the UI no longer allows.
	it("disables days before today so a past-dated slot cannot be entered", async () => {
		await gotoTimeSlots();

		await userEvent.click(screen.getByTestId("slot-start-trigger"));
		const grid = await screen.findByRole("grid");
		const yesterday = within(grid).getByText("9");
		expect(yesterday.closest("button")).toHaveAttribute(
			"aria-disabled",
			"true",
		);

		await userEvent.click(yesterday);
		expect(slotStart()).toHaveAttribute("aria-expanded", "true");
		expect(screen.getByText("No time slots added yet.")).toBeInTheDocument();
		expect(api.createVolunteerOpportunity).not.toHaveBeenCalled();
		expect(api.createTimeSlot).not.toHaveBeenCalled();
	});

	it("says which end of the range is wrong when it ends before it starts", async () => {
		await gotoTimeSlots();

		await addSlot("2026-10-05T12:00", "2026-10-05T10:00");

		expect(
			await screen.findByText("End date must be after start date."),
		).toBeInTheDocument();
		expect(screen.getByText("No time slots added yet.")).toBeInTheDocument();
	});

	it("lists slots in chronological order, not the order they were added", async () => {
		await gotoTimeSlots();

		await addSlot("2026-12-20T10:00", "2026-12-20T12:00");
		await addSlot("2026-10-05T10:00", "2026-10-05T12:00");
		await addSlot("2026-11-11T10:00", "2026-11-11T12:00");

		await waitFor(() => expect(slotRows()).toHaveLength(3));
		// Built with the app's own formatter rather than hardcoded, and matched
		// without splitting on a comma: which of the day and the month comes
		// first, and whether a comma separates the year, both follow the
		// visitor's own English locale (#2328). The ordering is the assertion.
		const expected = [
			"2026-10-05T10:00",
			"2026-11-11T10:00",
			"2026-12-20T10:00",
		].map((start) => formatDate(start, "en"));
		const rows = slotRows().map((row) => row.textContent ?? "");
		expected.forEach((date, index) => expect(rows[index]).toContain(date));
	});

	it("says what the number after a slot means instead of bracketing it", async () => {
		await gotoTimeSlots();

		await addSlot("2026-10-05T10:00", "2026-10-05T12:00", "3");

		const row = await waitFor(() => slotRows()[0]);
		expect(row).toHaveTextContent("3 spots");
		expect(row).not.toHaveTextContent("(3)");
	});

	it("labels an unlimited slot rather than showing a bare word in brackets", async () => {
		await gotoTimeSlots();
		await userEvent.click(screen.getByLabelText("Unlimited"));

		await addSlot("2026-10-05T10:00", "2026-10-05T12:00");

		const row = await waitFor(() => slotRows()[0]);
		expect(row).toHaveTextContent("Unlimited spots");
	});

	it("refuses a spot limit of 0 instead of silently rewriting it to 1", async () => {
		await gotoTimeSlots();

		await addSlot("2026-10-05T10:00", "2026-10-05T12:00", "0");

		expect(
			await screen.findByText("Enter a number of spots between 1 and 10,000."),
		).toBeInTheDocument();
		expect(slotMax()).toHaveAttribute("aria-invalid", "true");
		expect(slotMax()).toHaveAttribute("aria-describedby", "time-slot-error");
		expect(slotStart()).not.toHaveAttribute("aria-invalid");
		expect(slotMax()).toHaveValue(0);
		expect(screen.getByText("No time slots added yet.")).toBeInTheDocument();
	});

	it("refuses a spot limit past the cap, and bounds the box itself", async () => {
		await gotoTimeSlots();
		expect(slotMax()).toHaveAttribute("max", "10000");

		await addSlot("2026-10-05T10:00", "2026-10-05T12:00", "999999999");

		expect(
			await screen.findByText("Enter a number of spots between 1 and 10,000."),
		).toBeInTheDocument();
		expect(screen.getByText("No time slots added yet.")).toBeInTheDocument();
	});

	it("warns about an overlap before it is added, and flags both slots after", async () => {
		await gotoTimeSlots();
		await addSlot("2026-09-10T10:00", "2026-09-10T12:00");

		await pickDateTime("slot-start", "2026-09-10T11:00");
		await pickDateTime("slot-end", "2026-09-10T13:00");

		expect(
			await screen.findByText(
				"This overlaps a time slot already on the list - a volunteer could sign up for both.",
			),
		).toBeInTheDocument();

		await userEvent.click(screen.getByRole("button", { name: "Add" }));

		await waitFor(() => expect(slotRows()).toHaveLength(2));
		expect(screen.getAllByText("Overlaps another time slot")).toHaveLength(2);
	});

	it("never creates the opportunity when a staged slot has aged into the past", async () => {
		await gotoTimeSlots();
		await addSlot("2026-09-10T10:00", "2026-09-10T12:00");
		await waitFor(() => expect(slotRows()).toHaveLength(1));

		// The slot was in the future when it was added; the organizer took
		// their time over the rest of the wizard.
		vi.setSystemTime(new Date("2026-09-11T06:00:00Z"));
		await userEvent.click(screen.getByTestId("modal-submit"));

		expect(
			await screen.findByText(
				"Some time slots have moved into the past. Correct or remove them before publishing.",
			),
		).toBeInTheDocument();
		expect(api.createVolunteerOpportunity).not.toHaveBeenCalled();
	});

	it("stays quiet about slots that merely sit back to back", async () => {
		await gotoTimeSlots();
		await addSlot("2026-09-10T10:00", "2026-09-10T12:00");

		await pickDateTime("slot-start", "2026-09-10T12:00");
		await pickDateTime("slot-end", "2026-09-10T14:00");
		await userEvent.click(screen.getByRole("button", { name: "Add" }));

		await waitFor(() => expect(slotRows()).toHaveLength(2));
		expect(screen.queryByText("Overlaps another time slot")).toBeNull();
	});
});

describe("edit wizard: replacing the banner (#2325)", () => {
	function openEditWizardWithBanner() {
		return renderWithProviders(
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
	}

	it("offers a file picker while a banner is already set", async () => {
		openEditWizardWithBanner();

		await screen.findByTestId("wizard-step-1");
		const picker = document.querySelector(
			"#opportunity-banner",
		) as HTMLInputElement | null;
		expect(picker).not.toBeNull();
		expect(picker?.type).toBe("file");
		expect(screen.getByLabelText("Replace")).toBe(picker);
		expect(screen.getByRole("button", { name: "Remove" })).toBeInTheDocument();
	});

	it("keeps the picker once the banner is removed", async () => {
		openEditWizardWithBanner();

		await screen.findByTestId("wizard-step-1");
		await userEvent.click(screen.getByRole("button", { name: "Remove" }));

		expect(document.querySelector("#opportunity-banner")).not.toBeNull();
		expect(screen.queryByLabelText("Replace")).toBeNull();
	});
});

import { describe, it, expect, vi } from "vitest";
import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import MeetingFormModal, { type MeetingFormValues } from "./MeetingFormModal";
import { renderWithProviders } from "../test/render";

const NEXT_YEAR = new Date().getUTCFullYear() + 1;
// 18:00-19:00 in Berlin (CET, UTC+1).
const STARTS_AT = new Date(`${NEXT_YEAR}-03-10T17:00:00Z`);
const ENDS_AT = new Date(`${NEXT_YEAR}-03-10T18:00:00Z`);

const PREFILL: Partial<MeetingFormValues> = {
	startsAt: STARTS_AT,
	endsAt: ENDS_AT,
	format: "InPerson",
	location: "Clubhouse",
};

function renderForm(
	initial: Partial<MeetingFormValues> | undefined = PREFILL,
	onSubmit = vi.fn().mockResolvedValue(undefined),
	onClose = vi.fn(),
) {
	renderWithProviders(
		<MeetingFormModal
			title="Propose a meeting"
			intro="Vera gets a notification."
			initial={initial}
			onSubmit={onSubmit}
			onClose={onClose}
		/>,
		{ lng: "en" },
	);
	return { onSubmit, onClose };
}

describe("MeetingFormModal", () => {
	it("asks for one day and two times instead of two full date-times", () => {
		renderForm();

		expect(screen.getByLabelText(/^Day/)).toBeInTheDocument();
		expect(screen.getByLabelText(/^From/)).toHaveValue("18:00");
		expect(screen.getByLabelText(/^To/)).toHaveValue("19:00");
	});

	it("sends the times as UTC instants plus format, trimmed place and note, then closes", async () => {
		const user = userEvent.setup();
		const { onSubmit, onClose } = renderForm();

		await user.type(screen.getByLabelText("Note"), "  Bring your ID  ");
		await user.click(screen.getByRole("button", { name: "Send proposal" }));

		expect(onSubmit).toHaveBeenCalledWith({
			startsAt: STARTS_AT,
			endsAt: ENDS_AT,
			format: "InPerson",
			location: "Clubhouse",
			note: "Bring your ID",
		});
		expect(onClose).toHaveBeenCalled();
	});

	it("fills the end in an hour after a newly picked start", () => {
		renderForm({});

		fireEvent.change(screen.getByLabelText(/^From/), {
			target: { value: "09:30" },
		});

		expect(screen.getByLabelText(/^To/)).toHaveValue("10:30");
	});

	it("keeps an end the user already set later than the new start", () => {
		renderForm();

		fireEvent.change(screen.getByLabelText(/^From/), {
			target: { value: "17:30" },
		});

		expect(screen.getByLabelText(/^To/)).toHaveValue("19:00");
	});

	it("asks for an address in person and a link online, keeping what was typed in each", async () => {
		const user = userEvent.setup();
		renderForm();

		expect(screen.getByLabelText(/Address or meeting point/)).toHaveValue(
			"Clubhouse",
		);
		expect(screen.queryByLabelText(/Video call link/)).not.toBeInTheDocument();

		await user.click(screen.getByRole("radio", { name: "Online" }));
		expect(screen.getByLabelText(/Video call link/)).toHaveValue("");
		expect(
			screen.queryByLabelText(/Address or meeting point/),
		).not.toBeInTheDocument();

		await user.click(screen.getByRole("radio", { name: "In person" }));
		expect(screen.getByLabelText(/Address or meeting point/)).toHaveValue(
			"Clubhouse",
		);
	});

	it("sends an online meeting with its link", async () => {
		const user = userEvent.setup();
		const { onSubmit } = renderForm();

		await user.click(screen.getByRole("radio", { name: "Online" }));
		await user.type(
			screen.getByLabelText(/Video call link/),
			"https://meet.example.org/abc",
		);
		await user.click(screen.getByRole("button", { name: "Send proposal" }));

		expect(onSubmit).toHaveBeenCalledWith(
			expect.objectContaining({
				format: "Online",
				location: "https://meet.example.org/abc",
			}),
		);
	});

	it.each(["http://meet.example.org/abc", "meet.example.org/abc"])(
		"refuses %j as an online link before sending",
		async (link) => {
			const user = userEvent.setup();
			const { onSubmit } = renderForm();

			await user.click(screen.getByRole("radio", { name: "Online" }));
			await user.type(screen.getByLabelText(/Video call link/), link);
			await user.click(screen.getByRole("button", { name: "Send proposal" }));

			expect(onSubmit).not.toHaveBeenCalled();
			expect(screen.getByRole("alert")).toHaveTextContent(
				"Enter a complete link starting with https://.",
			);
			expect(screen.getByLabelText(/Video call link/)).toHaveAttribute(
				"aria-invalid",
				"true",
			);
		},
	);

	it("prefills an online meeting's link when moving it", () => {
		renderForm({ ...PREFILL, format: "Online", location: "https://x.org/a" });

		expect(screen.getByRole("radio", { name: "Online" })).toBeChecked();
		expect(screen.getByLabelText(/Video call link/)).toHaveValue(
			"https://x.org/a",
		);
	});

	it("names the missing times instead of sending an incomplete proposal", async () => {
		const user = userEvent.setup();
		const { onSubmit } = renderForm({
			format: "InPerson",
			location: "Clubhouse",
		});

		await user.click(screen.getByRole("button", { name: "Send proposal" }));

		expect(onSubmit).not.toHaveBeenCalled();
		expect(screen.getByRole("alert")).toHaveTextContent(
			"Choose a day and a start and end time.",
		);
	});

	it("refuses an end before the start instead of running past midnight", async () => {
		const user = userEvent.setup();
		const { onSubmit } = renderForm();

		fireEvent.change(screen.getByLabelText(/^To/), {
			target: { value: "01:00" },
		});
		await user.click(screen.getByRole("button", { name: "Send proposal" }));

		expect(onSubmit).not.toHaveBeenCalled();
		expect(screen.getByRole("alert")).toHaveTextContent(
			"The end has to be after the start, on the same day.",
		);
	});

	it("keeps the form open and shows the server's reason when sending fails", async () => {
		const user = userEvent.setup();
		const onSubmit = vi
			.fn()
			.mockRejectedValue({ errorCode: "Meeting.ProposalLimitReached" });
		const { onClose } = renderForm(PREFILL, onSubmit);

		await user.click(screen.getByRole("button", { name: "Send proposal" }));

		expect(
			await screen.findByText(/Too many meetings have been proposed/),
		).toBeInTheDocument();
		expect(onClose).not.toHaveBeenCalled();
	});

	it("does not offer sending without a place", () => {
		renderForm({});

		expect(
			screen.getByRole("button", { name: "Send proposal" }),
		).toBeDisabled();
	});
});

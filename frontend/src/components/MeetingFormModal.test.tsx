import { describe, it, expect, vi } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import MeetingFormModal from "./MeetingFormModal";
import { renderWithProviders } from "../test/render";

const NEXT_YEAR = new Date().getUTCFullYear() + 1;
const STARTS_AT = new Date(`${NEXT_YEAR}-03-10T17:00:00Z`);
const ENDS_AT = new Date(`${NEXT_YEAR}-03-10T18:00:00Z`);

function renderForm(
	onSubmit = vi.fn().mockResolvedValue(undefined),
	onClose = vi.fn(),
	prefill = true,
) {
	renderWithProviders(
		<MeetingFormModal
			title="Propose a meeting"
			intro="Vera gets a notification."
			initialStartsAt={prefill ? STARTS_AT : undefined}
			initialEndsAt={prefill ? ENDS_AT : undefined}
			initialLocation={prefill ? "Clubhouse" : ""}
			onSubmit={onSubmit}
			onClose={onClose}
		/>,
		{ lng: "en" },
	);
	return { onSubmit, onClose };
}

describe("MeetingFormModal", () => {
	it("sends the times as UTC instants plus the trimmed place and note, then closes", async () => {
		const user = userEvent.setup();
		const { onSubmit, onClose } = renderForm();

		await user.type(screen.getByLabelText("Note"), "  Bring your ID  ");
		await user.click(screen.getByRole("button", { name: "Send proposal" }));

		expect(onSubmit).toHaveBeenCalledWith({
			startsAt: STARTS_AT,
			endsAt: ENDS_AT,
			location: "Clubhouse",
			note: "Bring your ID",
		});
		expect(onClose).toHaveBeenCalled();
	});

	it("names the missing times instead of sending an incomplete proposal", async () => {
		const user = userEvent.setup();
		const { onSubmit } = renderForm(undefined, undefined, false);

		await user.type(
			screen.getByLabelText(/Place or online link/),
			"https://meet.example.org/abc",
		);
		await user.click(screen.getByRole("button", { name: "Send proposal" }));

		expect(onSubmit).not.toHaveBeenCalled();
		expect(screen.getByRole("alert")).toHaveTextContent(
			"Choose a date and time for the start and the end.",
		);
	});

	it("keeps the form open and shows the server's reason when sending fails", async () => {
		const user = userEvent.setup();
		const onSubmit = vi
			.fn()
			.mockRejectedValue({ errorCode: "Meeting.ProposalLimitReached" });
		const { onClose } = renderForm(onSubmit);

		await user.click(screen.getByRole("button", { name: "Send proposal" }));

		expect(
			await screen.findByText(/Too many meetings have been proposed/),
		).toBeInTheDocument();
		expect(onClose).not.toHaveBeenCalled();
	});

	it("does not offer sending without a place", () => {
		renderForm(undefined, undefined, false);

		expect(
			screen.getByRole("button", { name: "Send proposal" }),
		).toBeDisabled();
	});
});

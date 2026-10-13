import { describe, it, expect } from "vitest";
import userEvent from "@testing-library/user-event";
import { act, screen, waitFor } from "@testing-library/react";
import { dispatchToast } from "../lib/toastBus";
import { renderWithProviders } from "../test/render";

describe("ToastProvider", () => {
	it("coalesces identical simultaneous toasts into one", () => {
		renderWithProviders(<div />);

		act(() => {
			for (let i = 0; i < 4; i++) {
				dispatchToast("error", "You do not have permission to do this.");
			}
		});

		const toasts = screen
			.getAllByRole("alert")
			.filter((el) => el.textContent?.includes("You do not have permission"));
		expect(toasts).toHaveLength(1);
	});

	it("still shows genuinely different messages side by side", async () => {
		renderWithProviders(<div />);

		act(() => {
			dispatchToast("error", "First problem.");
			dispatchToast("error", "Second problem.");
		});

		await waitFor(() => {
			expect(screen.getByText("First problem.")).toBeInTheDocument();
			expect(screen.getByText("Second problem.")).toBeInTheDocument();
		});
	});

	it("mounts an empty live-region sentinel before any toast has fired", () => {
		renderWithProviders(<div />);

		const sentinel = document.querySelector(
			"[data-testid='toast-live-region'][aria-live='polite']",
		);
		expect(sentinel).not.toBeNull();
		expect(sentinel).toBeEmptyDOMElement();
	});

	it("keeps each toast's role=alert a sibling of the sentinel, never nested inside it", () => {
		renderWithProviders(<div />);
		act(() => {
			dispatchToast("error", "You do not have permission to do this.");
		});

		const sentinel = document.querySelector(
			"[data-testid='toast-live-region']",
		);
		expect(sentinel).not.toBeNull();
		expect(sentinel?.querySelectorAll("[role='alert']")).toHaveLength(0);
		expect(screen.getByRole("alert")).toBeInTheDocument();
	});

	it("removes a toast when its dismiss button is clicked", async () => {
		renderWithProviders(<div />);
		act(() => {
			dispatchToast("info", "Saved somewhere.");
		});

		await userEvent.click(screen.getByRole("button", { name: "Dismiss" }));

		expect(screen.queryByText("Saved somewhere.")).not.toBeInTheDocument();
	});

	it("tints each level differently", () => {
		renderWithProviders(<div />);
		act(() => {
			dispatchToast("error", "An error.");
			dispatchToast("warning", "A warning.");
			dispatchToast("success", "A success.");
			dispatchToast("info", "An info.");
		});

		const tint = (text: string) =>
			screen.getByText(text).closest("[role='alert']")?.className;
		expect(tint("An error.")).toContain("bg-red-600");
		expect(tint("A warning.")).toContain("bg-yellow-700");
		expect(tint("A success.")).toContain("bg-green-700");
		expect(tint("An info.")).toContain("bg-gray-700");
	});
});

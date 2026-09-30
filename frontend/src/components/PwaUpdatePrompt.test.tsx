import { describe, it, expect, vi, afterEach } from "vitest";
import { screen, fireEvent } from "@testing-library/react";
import PwaUpdatePrompt from "./PwaUpdatePrompt";
import { renderWithProviders } from "../test/render";

const { useRegisterSW, updateServiceWorker } = vi.hoisted(() => ({
	useRegisterSW: vi.fn(),
	updateServiceWorker: vi.fn(),
}));

vi.mock("virtual:pwa-register/react", () => ({ useRegisterSW }));

function mockNeedRefresh(needRefresh: boolean) {
	useRegisterSW.mockReturnValue({
		needRefresh: [needRefresh, vi.fn()],
		offlineReady: [false, vi.fn()],
		updateServiceWorker,
	});
}

describe("PwaUpdatePrompt", () => {
	afterEach(() => {
		vi.clearAllMocks();
	});

	it("renders nothing when no update is available", () => {
		mockNeedRefresh(false);

		renderWithProviders(<PwaUpdatePrompt />);

		expect(screen.queryByRole("status")).not.toBeInTheDocument();
	});

	it("shows a reload prompt once a new version is available", () => {
		mockNeedRefresh(true);

		renderWithProviders(<PwaUpdatePrompt />);

		expect(
			screen.getByText("A new version of Afunto is available."),
		).toBeInTheDocument();
	});

	it("updates and reloads the service worker when Reload is clicked", () => {
		mockNeedRefresh(true);

		renderWithProviders(<PwaUpdatePrompt />);
		fireEvent.click(
			screen.getByRole("button", { name: "Reload to update the app" }),
		);

		expect(updateServiceWorker).toHaveBeenCalledWith(true);
	});

	it('gives its Reload button a distinct accessible name from ConfigGate/ErrorBoundary\'s plain "Reload" button, since this banner can be on screen at the same time as either', () => {
		mockNeedRefresh(true);

		renderWithProviders(<PwaUpdatePrompt />);

		expect(
			screen.queryByRole("button", { name: "Reload" }),
		).not.toBeInTheDocument();
	});
});

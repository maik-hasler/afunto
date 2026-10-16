import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, screen, waitFor } from "@testing-library/react";
import { useSessionExpiryHandler } from "./useSessionExpiryHandler";
import { useAuthDisplayStatus } from "./useAuthDisplayStatus";
import { notifySessionExpired } from "../lib/sessionExpiryBus";
import {
	clearAuthRecoveryAttempts,
	recordAuthRecoveryAttempt,
} from "../lib/authRecovery";
import { useAuthRecoveryFailedFlag } from "../contexts/AuthStatusContext";
import { renderWithProviders } from "../test/render";

function Harness() {
	useSessionExpiryHandler();
	return <div>App body</div>;
}

function StatusHarness() {
	useSessionExpiryHandler();
	const status = useAuthDisplayStatus();
	return <div data-testid="auth-display-status">{status}</div>;
}

function RecoveryHarness() {
	useSessionExpiryHandler();
	const recoveryFailed = useAuthRecoveryFailedFlag();
	return <div data-testid="recovery-failed">{String(recoveryFailed)}</div>;
}

const signinRedirect = vi.fn();

beforeEach(() => {
	vi.useFakeTimers({ shouldAdvanceTime: true });
	signinRedirect.mockReset().mockResolvedValue(undefined);
	sessionStorage.clear();
});

afterEach(() => {
	vi.useRealTimers();
});

describe("session expiry", () => {
	// #2392: both fired seconds after someone returned to an idle tab, and
	// took the page away mid-read.
	it("neither toasts nor leaves the page when the session ends", async () => {
		renderWithProviders(<Harness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});

		act(() => notifySessionExpired());
		await act(async () => {
			await vi.advanceTimersByTimeAsync(5000);
		});

		expect(screen.queryByRole("alert")).toBeNull();
		expect(signinRedirect).not.toHaveBeenCalled();
		expect(screen.getByText("App body")).toBeInTheDocument();
	});

	it("stays quiet for a visitor who was never signed in", async () => {
		renderWithProviders(<Harness />, { auth: { isAuthenticated: false } });

		act(() => notifySessionExpired());

		await act(async () => {
			await vi.advanceTimersByTimeAsync(2100);
		});
		await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
		expect(signinRedirect).not.toHaveBeenCalled();
	});

	it("flips the shared auth display status to sessionExpired instead of signedOut (#2224)", async () => {
		renderWithProviders(<StatusHarness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});

		expect(screen.getByTestId("auth-display-status")).toHaveTextContent(
			"signedIn",
		);

		act(() => notifySessionExpired());

		await waitFor(() =>
			expect(screen.getByTestId("auth-display-status")).toHaveTextContent(
				"sessionExpired",
			),
		);
	});
});

describe("bounded auth recovery (#2208)", () => {
	it("offers signing in again on the first expiry of a recovery episode", async () => {
		renderWithProviders(<RecoveryHarness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});

		act(() => notifySessionExpired());

		expect(screen.getByTestId("recovery-failed")).toHaveTextContent("false");
	});

	it("gives up on a second consecutive expiry across a sign-in round trip", async () => {
		// Signing in again is a full-page navigation, which remounts the whole
		// app - simulate that by unmounting and rendering a fresh instance,
		// while the sessionStorage-backed counter survives.
		const first = renderWithProviders(<RecoveryHarness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});
		act(() => notifySessionExpired());
		// Stands in for the header's "Sign in again" button.
		recordAuthRecoveryAttempt();
		first.unmount();

		renderWithProviders(<RecoveryHarness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});
		act(() => notifySessionExpired());

		expect(screen.getByTestId("recovery-failed")).toHaveTextContent("true");
	});

	it("keeps offering to sign in when a reload meets the same ended session", async () => {
		const first = renderWithProviders(<RecoveryHarness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});
		act(() => notifySessionExpired());
		first.unmount();

		renderWithProviders(<RecoveryHarness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});
		act(() => notifySessionExpired());

		expect(screen.getByTestId("recovery-failed")).toHaveTextContent("false");
	});

	it("starts a fresh episode once a successful API call clears the counter", async () => {
		const first = renderWithProviders(<RecoveryHarness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});
		act(() => notifySessionExpired());
		recordAuthRecoveryAttempt();
		first.unmount();

		// Stands in for api-instance.ts's own clear-on-success call.
		clearAuthRecoveryAttempts();

		renderWithProviders(<RecoveryHarness />, {
			auth: { isAuthenticated: true, signinRedirect },
		});
		act(() => notifySessionExpired());

		expect(screen.getByTestId("recovery-failed")).toHaveTextContent("false");
	});
});

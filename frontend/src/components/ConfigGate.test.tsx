import { describe, it, expect, vi, afterEach } from "vitest";
import { screen, fireEvent, cleanup, act } from "@testing-library/react";
import ConfigGate from "./ConfigGate";
import { renderWithProviders } from "../test/render";

const { mockRuntimeConfig } = vi.hoisted(() => ({
	mockRuntimeConfig: { isConfigured: true },
}));

vi.mock("../lib/runtimeConfig", () => ({ runtimeConfig: mockRuntimeConfig }));

function setNavigatorOnLine(value: boolean) {
	Object.defineProperty(navigator, "onLine", {
		configurable: true,
		get: () => value,
	});
}

// The listener ConfigGate subscribes with now sets state as well as reloading,
// so a bare window.dispatchEvent would update React outside act().
function goOnline() {
	setNavigatorOnLine(true);
	act(() => {
		window.dispatchEvent(new Event("online"));
	});
}

function goOffline() {
	setNavigatorOnLine(false);
	act(() => {
		window.dispatchEvent(new Event("offline"));
	});
}

function renderGate() {
	return renderWithProviders(
		<ConfigGate>
			<p>protected content</p>
		</ConfigGate>,
	);
}

describe("ConfigGate", () => {
	afterEach(() => {
		mockRuntimeConfig.isConfigured = true;
		setNavigatorOnLine(true);
		vi.unstubAllGlobals();
		cleanup();
	});

	it("renders its children when the runtime config is valid", () => {
		mockRuntimeConfig.isConfigured = true;

		renderGate();

		expect(screen.getByText("protected content")).toBeInTheDocument();
	});

	it("shows a configuration-missing screen instead of the app when unconfigured", () => {
		mockRuntimeConfig.isConfigured = false;

		renderGate();

		expect(screen.queryByText("protected content")).not.toBeInTheDocument();
		expect(
			screen.getByRole("heading", { name: "Configuration missing" }),
		).toBeInTheDocument();
	});

	it("reloads once the browser comes back online while unconfigured", () => {
		mockRuntimeConfig.isConfigured = false;
		const reload = vi.fn();
		vi.stubGlobal("location", { ...window.location, reload });

		renderGate();
		goOnline();

		expect(reload).toHaveBeenCalledTimes(1);
	});

	it("does not reload on an offline event", () => {
		mockRuntimeConfig.isConfigured = false;
		const reload = vi.fn();
		vi.stubGlobal("location", { ...window.location, reload });

		renderGate();
		goOffline();

		expect(reload).not.toHaveBeenCalled();
	});

	it("does not reload on reconnect once already configured", () => {
		mockRuntimeConfig.isConfigured = true;
		const reload = vi.fn();
		vi.stubGlobal("location", { ...window.location, reload });

		renderGate();
		goOnline();

		expect(reload).not.toHaveBeenCalled();
	});

	it("reloads the page when Reload is clicked", () => {
		mockRuntimeConfig.isConfigured = false;
		const reload = vi.fn();
		vi.stubGlobal("location", { ...window.location, reload });

		renderGate();
		fireEvent.click(screen.getByRole("button", { name: "Reload" }));

		expect(reload).toHaveBeenCalledTimes(1);
	});

	// #2317: an offline cold start is the by-far likeliest way /config.js goes
	// missing, and it is not the operator's fault - blaming the deployment for
	// the visitor's own lost connection is both wrong and un-actionable.
	it("shows the offline state, not the operator message, when unconfigured while offline", () => {
		mockRuntimeConfig.isConfigured = false;
		setNavigatorOnLine(false);

		renderGate();

		expect(
			screen.getByRole("heading", { name: "You are offline" }),
		).toBeInTheDocument();
		expect(
			screen.queryByRole("heading", { name: "Configuration missing" }),
		).not.toBeInTheDocument();
		expect(screen.queryByText("protected content")).not.toBeInTheDocument();
	});

	it("titles the page after the offline state rather than the missing config", () => {
		mockRuntimeConfig.isConfigured = false;
		setNavigatorOnLine(false);

		renderGate();

		expect(document.title).toBe("You are offline | Afunto");
	});

	it("swaps to the offline state when the connection drops while unconfigured", () => {
		mockRuntimeConfig.isConfigured = false;

		renderGate();
		expect(
			screen.getByRole("heading", { name: "Configuration missing" }),
		).toBeInTheDocument();

		goOffline();

		expect(
			screen.getByRole("heading", { name: "You are offline" }),
		).toBeInTheDocument();
		expect(
			screen.queryByRole("heading", { name: "Configuration missing" }),
		).not.toBeInTheDocument();
	});

	it("reloads from the offline state's retry button", () => {
		mockRuntimeConfig.isConfigured = false;
		setNavigatorOnLine(false);
		const reload = vi.fn();
		vi.stubGlobal("location", { ...window.location, reload });

		renderGate();
		fireEvent.click(screen.getByRole("button", { name: "Try again" }));

		expect(reload).toHaveBeenCalledTimes(1);
	});

	it("still renders the app offline when the config did resolve", () => {
		mockRuntimeConfig.isConfigured = true;
		setNavigatorOnLine(false);

		renderGate();

		expect(screen.getByText("protected content")).toBeInTheDocument();
	});
});

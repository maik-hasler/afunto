import { describe, it, expect, vi, beforeEach } from "vitest";
import { waitFor, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
	useInvalidateMyEngagementStates,
	useMyEngagementStates,
} from "./useMyEngagementStates";
import { renderWithProviders } from "../test/render";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("../test/apiMock");
	return { api: createApiMock() };
});

vi.mock("./useApiClient", () => ({ useApiClient: () => api }));

function Consumer({ opportunityId }: { opportunityId: string }) {
	const states = useMyEngagementStates();
	return (
		<span data-testid={`state-${opportunityId}`}>
			{states.get(opportunityId) ?? "none"}
		</span>
	);
}

function Invalidator() {
	const invalidate = useInvalidateMyEngagementStates();
	return (
		<button type="button" onClick={() => void invalidate()}>
			invalidate
		</button>
	);
}

beforeEach(() => {
	api.__reset();
	api.getMyEngagements.mockResolvedValue({
		items: [
			{ opportunityId: "a", status: "Confirmed" },
			{ opportunityId: "b", status: "Withdrawn" },
		],
	});
});

describe("useMyEngagementStates", () => {
	it("asks for nothing and reports no state when signed out", async () => {
		renderWithProviders(<Consumer opportunityId="a" />);

		expect(screen.getByTestId("state-a")).toHaveTextContent("none");
		await waitFor(() => expect(api.getMyEngagements).not.toHaveBeenCalled());
	});

	it("maps the viewer's upcoming engagements per opportunity, with one request for every card", async () => {
		renderWithProviders(
			<>
				<Consumer opportunityId="a" />
				<Consumer opportunityId="b" />
			</>,
			{ auth: { isAuthenticated: true } },
		);

		await waitFor(() =>
			expect(screen.getByTestId("state-a")).toHaveTextContent("Confirmed"),
		);
		expect(screen.getByTestId("state-b")).toHaveTextContent("none");
		expect(api.getMyEngagements).toHaveBeenCalledTimes(1);
		expect(api.getMyEngagements).toHaveBeenCalledWith({
			query: { PageNumber: 1, PageSize: 100, Upcoming: true },
		});
	});

	it("refetches when a sign-up or withdrawal invalidates the states", async () => {
		const user = userEvent.setup();
		renderWithProviders(
			<>
				<Consumer opportunityId="a" />
				<Invalidator />
			</>,
			{ auth: { isAuthenticated: true } },
		);

		await waitFor(() => expect(api.getMyEngagements).toHaveBeenCalledTimes(1));

		await user.click(screen.getByRole("button", { name: "invalidate" }));

		await waitFor(() => expect(api.getMyEngagements).toHaveBeenCalledTimes(2));
	});
});

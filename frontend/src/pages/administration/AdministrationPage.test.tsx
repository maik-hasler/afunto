import { describe, it, expect, vi, beforeEach } from "vitest";
import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Navigate, Route, Routes, useLocation } from "react-router";
import AdministrationPage from "./AdministrationPage";
import AdminAuditLogPage from "./AdminAuditLogPage";
import AdminOrganizationsPage from "./AdminOrganizationsPage";
import AdminReportsPage from "./AdminReportsPage";
import AdminUsersPage from "./AdminUsersPage";
import ProtectedRoute from "../../layouts/ProtectedRoute";
import { renderWithProviders } from "../../test/render";

const { api } = await vi.hoisted(async () => {
	const { createApiMock } = await import("../../test/apiMock");
	return { api: createApiMock() };
});

vi.mock("../../hooks/useApiClient", () => ({ useApiClient: () => api }));

function LocationProbe() {
	const location = useLocation();
	return <span data-testid="location">{location.pathname}</span>;
}

function renderAdministration(route: string, roles: string[]) {
	return renderWithProviders(
		<>
			<LocationProbe />
			<Routes>
				<Route
					path="/administration"
					element={
						<ProtectedRoute requiredRole="admin">
							<AdministrationPage />
						</ProtectedRoute>
					}
				>
					<Route index element={<Navigate to="organizations" replace />} />
					<Route path="organizations" element={<AdminOrganizationsPage />} />
					<Route path="users" element={<AdminUsersPage />} />
					<Route path="reports" element={<AdminReportsPage />} />
					<Route path="audit-log" element={<AdminAuditLogPage />} />
				</Route>
			</Routes>
		</>,
		{ route, auth: { isAuthenticated: true, roles } },
	);
}

beforeEach(() => {
	api.__reset();
	const emptyPage = { items: [], pageCount: 1, totalCount: 0 };
	api.listOrganizations.mockResolvedValue(emptyPage);
	api.listUsers.mockResolvedValue(emptyPage);
	api.listFlaggedTargets.mockResolvedValue(emptyPage);
	api.listAuditLogs.mockResolvedValue(emptyPage);
	document.title = "";
});

describe("administration sections", () => {
	const sections = [
		["organizations", "Organizations"],
		["users", "Users"],
		["reports", "Reports"],
		["audit-log", "Audit log"],
	] as const;

	for (const [section, name] of sections) {
		it(`/${section} has its own distinct title and heading`, async () => {
			renderAdministration(`/administration/${section}`, ["admin"]);

			await waitFor(() =>
				expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(
					name,
				),
			);
			await waitFor(() =>
				expect(document.title).toBe(`${name} - Administration | Afunto`),
			);
		});
	}

	it("sends /administration itself on to the first section", async () => {
		renderAdministration("/administration", ["admin"]);

		await waitFor(() =>
			expect(screen.getByTestId("location")).toHaveTextContent(
				"/administration/organizations",
			),
		);
		expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(
			"Organizations",
		);
	});
});

describe("administration access for a non-admin", () => {
	it("keeps the page from mounting, stays on the URL, and says why", async () => {
		renderAdministration("/administration", ["user"]);

		expect(
			await screen.findByRole("heading", { name: "Admin rights required" }),
		).toBeInTheDocument();
		expect(
			screen.getByText(/Your account does not have admin rights/),
		).toBeInTheDocument();

		expect(screen.queryByRole("heading", { name: "Organizations" })).toBeNull();

		expect(screen.getByTestId("location")).toHaveTextContent("/administration");

		await waitFor(() =>
			expect(document.title).toBe("Admin rights required | Afunto"),
		);
		expect(
			screen.getByRole("link", { name: "Back to home" }),
		).toBeInTheDocument();
	});
});

describe("administration loading state", () => {
	it("shows a labelled pulsing skeleton while the organizations page loads", async () => {
		let resolvePage: (value: unknown) => void = () => {};
		api.listOrganizations.mockReturnValue(
			new Promise((resolve) => {
				resolvePage = resolve;
			}),
		);

		renderAdministration("/administration/organizations", ["admin"]);

		const skeleton = document.querySelector("[role='status'] .animate-pulse");
		expect(skeleton).not.toBeNull();
		expect(skeleton?.closest("[role='status']")).toHaveTextContent(/Loading/);

		resolvePage({ items: [], pageCount: 1, totalCount: 0 });
		await waitFor(() =>
			expect(
				document.querySelector("[role='status'] .animate-pulse"),
			).toBeNull(),
		);
	});
});

const FLAGGED_OPPORTUNITY = {
	targetType: "VolunteerOpportunity",
	targetId: "11111111-1111-1111-1111-111111111111",
	targetTitle: "Gassi-Dienst für Tierheimhunde",
	targetTitleEn: "Dog Walking for Shelter Dogs",
	openReportCount: 1,
	totalReportCount: 1,
	lastReportedOn: new Date(Date.UTC(2026, 7, 29, 11, 28)),
	isDeleted: false,
};

describe("moderation queue", () => {
	it("asks the API for open work only, and for resolved history on request", async () => {
		api.listFlaggedTargets.mockResolvedValue({
			items: [FLAGGED_OPPORTUNITY],
			pageCount: 1,
			totalCount: 1,
		});

		renderAdministration("/administration/reports", ["admin"]);

		await screen.findByRole("link", { name: "Dog Walking for Shelter Dogs" });
		expect(api.listFlaggedTargets).toHaveBeenLastCalledWith({
			query: { pageNumber: 1, pageSize: 10, includeResolved: false },
		});

		await userEvent.click(
			screen.getByRole("checkbox", { name: /Include resolved targets/ }),
		);

		await waitFor(() =>
			expect(api.listFlaggedTargets).toHaveBeenLastCalledWith({
				query: { pageNumber: 1, pageSize: 10, includeResolved: true },
			}),
		);
	});

	// The row's counts are what the queue is for; leaving them at "1 open flag" until a manual
	// reload made the dismissal look like it had not happened (#2326).
	it("updates the row's open count as reports are dismissed, then drops the resolved row", async () => {
		api.listFlaggedTargets.mockResolvedValue({
			items: [FLAGGED_OPPORTUNITY],
			pageCount: 1,
			totalCount: 1,
		});
		api.getReportHistoryForTarget.mockResolvedValue([
			{
				id: "aaaaaaaa-0000-0000-0000-000000000001",
				reporterId: "bbbbbbbb-0000-0000-0000-000000000001",
				reason: "Spam",
				details: undefined,
				status: "Open",
				createdOn: new Date(Date.UTC(2026, 7, 29, 11, 28)),
			},
		]);
		api.dismissReport.mockResolvedValue(undefined);

		renderAdministration("/administration/reports", ["admin"]);

		const row = (
			await screen.findByRole("link", {
				name: "Dog Walking for Shelter Dogs",
			})
		).closest("li") as HTMLElement;
		expect(row).toHaveTextContent("1 open flag");

		await userEvent.click(
			within(row).getByRole("button", { name: /View report history/ }),
		);
		await userEvent.click(
			await screen.findByRole("button", { name: "Dismiss" }),
		);

		await waitFor(() => expect(row).toHaveTextContent("0 open flags"));

		// The modal has no close button - Escape and the backdrop are the ways out.
		await userEvent.keyboard("{Escape}");

		await waitFor(() =>
			expect(
				screen.queryByRole("link", { name: "Dog Walking for Shelter Dogs" }),
			).toBeNull(),
		);
		expect(
			screen.getByText("No flagged content. All caught up."),
		).toBeInTheDocument();
	});

	// Every shadow-delete handler marks the target's open reports Actioned, so the queue row is
	// resolved work the moment the hide succeeds.
	it("drops the row once hiding the target has resolved its open reports", async () => {
		api.listFlaggedTargets.mockResolvedValue({
			items: [FLAGGED_OPPORTUNITY],
			pageCount: 1,
			totalCount: 1,
		});
		api.adminShadowDeleteVolunteerOpportunity.mockResolvedValue(undefined);

		renderAdministration("/administration/reports", ["admin"]);

		await userEvent.click(
			await screen.findByRole("button", {
				name: "Hide Dog Walking for Shelter Dogs",
			}),
		);
		await userEvent.click(screen.getByRole("button", { name: "Yes, hide it" }));

		await waitFor(() =>
			expect(
				screen.queryByRole("link", { name: "Dog Walking for Shelter Dogs" }),
			).toBeNull(),
		);
		expect(
			screen.getByText("No flagged content. All caught up."),
		).toBeInTheDocument();
	});

	it("shows the admin's own language, marking a German title that has no translation", async () => {
		api.listFlaggedTargets.mockResolvedValue({
			items: [{ ...FLAGGED_OPPORTUNITY, targetTitleEn: undefined }],
			pageCount: 1,
			totalCount: 1,
		});

		renderAdministration("/administration/reports", ["admin"]);

		const link = await screen.findByRole("link", {
			name: "Gassi-Dienst für Tierheimhunde",
		});
		expect(link).toHaveAttribute("lang", "de");
	});
});

describe("audit log filtering", () => {
	it("narrows the request by action type and clears back to the unfiltered log", async () => {
		renderAdministration("/administration/audit-log", ["admin"]);

		await waitFor(() =>
			expect(api.listAuditLogs).toHaveBeenCalledWith({
				query: {
					pageNumber: 1,
					pageSize: 10,
					actionType: undefined,
					subjectType: undefined,
					actorUserId: undefined,
					from: undefined,
					to: undefined,
					oldestFirst: false,
				},
			}),
		);

		await userEvent.click(screen.getByRole("combobox", { name: "Action" }));
		await userEvent.click(
			screen.getByRole("option", { name: "Dismissed a report" }),
		);

		await waitFor(() =>
			expect(api.listAuditLogs).toHaveBeenLastCalledWith({
				query: {
					pageNumber: 1,
					pageSize: 10,
					actionType: "ReportDismissed",
					subjectType: undefined,
					actorUserId: undefined,
					from: undefined,
					to: undefined,
					oldestFirst: false,
				},
			}),
		);

		await userEvent.click(
			screen.getByRole("button", { name: "Clear filters" }),
		);

		await waitFor(() =>
			expect(api.listAuditLogs).toHaveBeenLastCalledWith({
				query: {
					pageNumber: 1,
					pageSize: 10,
					actionType: undefined,
					subjectType: undefined,
					actorUserId: undefined,
					from: undefined,
					to: undefined,
					oldestFirst: false,
				},
			}),
		);
	});

	it("reverses the sort order", async () => {
		renderAdministration("/administration/audit-log", ["admin"]);

		await userEvent.click(
			await screen.findByRole("button", { name: "Newest first" }),
		);

		await waitFor(() =>
			expect(api.listAuditLogs).toHaveBeenLastCalledWith({
				query: {
					pageNumber: 1,
					pageSize: 10,
					actionType: undefined,
					subjectType: undefined,
					actorUserId: undefined,
					from: undefined,
					to: undefined,
					oldestFirst: true,
				},
			}),
		);
	});

	it("filters to one admin from the entry that named them", async () => {
		const actorUserId = "cccccccc-0000-0000-0000-000000000001";
		api.listAuditLogs.mockResolvedValue({
			items: [
				{
					id: "dddddddd-0000-0000-0000-000000000001",
					actorUserId,
					actorDisplayName: "Admina Admin",
					actionType: "ReportDismissed",
					subjectType: "Organization",
					subjectId: "eeeeeeee-0000-0000-0000-000000000001",
					subjectDisplayName: "Nachbarschaftshilfe Leipzig",
					subjectDisplayNameEn: undefined,
					reason: undefined,
					createdOn: new Date(Date.UTC(2026, 7, 29, 11, 28)),
				},
			],
			pageCount: 1,
			totalCount: 1,
		});

		renderAdministration("/administration/audit-log", ["admin"]);

		await userEvent.click(
			await screen.findByRole("button", { name: "Only Admina Admin" }),
		);

		await waitFor(() =>
			expect(api.listAuditLogs).toHaveBeenLastCalledWith({
				query: {
					pageNumber: 1,
					pageSize: 10,
					actionType: undefined,
					subjectType: undefined,
					actorUserId: actorUserId,
					from: undefined,
					to: undefined,
					oldestFirst: false,
				},
			}),
		);
		expect(
			screen.getByRole("button", { name: /Stop filtering by Admina Admin/ }),
		).toBeInTheDocument();
	});
});

describe("administration empty states", () => {
	it("says a search is filtering the list and offers a way out of it", async () => {
		renderAdministration("/administration/users", ["admin"]);

		await userEvent.type(
			await screen.findByLabelText("Search users"),
			"nobody",
		);
		await userEvent.click(screen.getByRole("button", { name: "Search" }));

		expect(
			await screen.findByText("No users match this search."),
		).toBeInTheDocument();
		expect(screen.getByText(/Nothing matches "nobody"/)).toBeInTheDocument();

		await userEvent.click(screen.getByRole("button", { name: "Clear search" }));

		expect(await screen.findByText("No users found.")).toBeInTheDocument();
		await waitFor(() =>
			expect(api.listUsers).toHaveBeenLastCalledWith({
				query: { search: undefined, pageNumber: 1, pageSize: 10 },
			}),
		);
	});
});

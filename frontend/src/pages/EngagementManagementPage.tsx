import { lazy, Suspense, useEffect, useMemo, useState } from "react";
import { useParams, useOutletContext, Link } from "react-router";
import { useTranslation } from "react-i18next";
import { quoteMarks } from "../lib/quotes";
import type {
	EngagementSummary,
	FeedbackItemDto,
	TimeSlotDetail,
	VolunteerOpportunityDetails,
} from "../client";
import { useApiClient } from "../hooks/useApiClient";
import { useLoadMore } from "../hooks/useLoadMore";
import AssignMemberDialog from "../components/AssignMemberDialog";
import ConfirmDialog from "../components/ConfirmDialog";
import EmptyState from "../components/EmptyState";
import Skeleton from "../components/Skeleton";
import Button from "../components/Button";
import Chip from "../components/Chip";
import LoadMoreError from "../components/LoadMoreError";
import LoadMoreButton from "../components/LoadMoreButton";
import ModalLoadingFallback from "../components/ModalLoadingFallback";
import Select from "../components/Select";
import NotFoundPage from "./NotFoundPage";
import {
	formatDate,
	formatDateTimeRange,
	pickLocalizedText,
	resolveDateLocale,
} from "../lib/format";
import { usePageTitle } from "../hooks/usePageTitle";
import { useSetOrgBreadcrumbExtra } from "../contexts/OrgBreadcrumbContext";
import { dispatchToast } from "../lib/toastBus";
import { getApiErrorMessage, isApiNotFoundError } from "../lib/apiError";
import { ENGAGEMENT_STATUS_COLORS } from "../lib/engagementStatus";
import {
	checkboxClass,
	inputClass,
	labelClass,
	textareaClass,
} from "../lib/formClasses";
import { cardClass } from "../lib/surfaceClasses";
import {
	ArrowTopRightOnSquareIcon,
	CheckIconSolid,
	QrCodeIcon,
	TrashIcon,
} from "../components/icons";
import StarRating from "../components/StarRating";
import type { OrgAppContext } from "../layouts/OrgAppLayout";

const STATUS_COLORS = ENGAGEMENT_STATUS_COLORS;
const ENGAGEMENTS_PAGE_SIZE = 10;
const FEEDBACK_PAGE_SIZE = 10;

const QRScannerModal = lazy(() => import("../components/QRScannerModal"));

export default function EngagementManagementPage() {
	const { opportunityId } = useParams<{ opportunityId: string }>();
	const { isOrganizer, org } = useOutletContext<OrgAppContext>();
	const api = useApiClient();
	const { t, i18n } = useTranslation();
	const quotes = quoteMarks(i18n.language);
	const [opportunity, setOpportunity] =
		useState<VolunteerOpportunityDetails | null>(null);
	const [opportunityError, setOpportunityError] = useState<string | null>(null);
	const [retryingOpportunity, setRetryingOpportunity] = useState(false);
	const resolvedOpportunityTitle =
		opportunity &&
		pickLocalizedText(opportunity.titleDe, opportunity.titleEn, i18n.language);
	const opportunityTitle = resolvedOpportunityTitle?.text;
	usePageTitle(
		opportunityTitle
			? `${t("engagementManagement.title")} - ${opportunityTitle}`
			: t("engagementManagement.title"),
	);

	useSetOrgBreadcrumbExtra(opportunityTitle, resolvedOpportunityTitle?.lang);

	const STATUS_LABELS: Record<string, string> = {
		Pending: t("engagementManagement.status.Pending"),
		Confirmed: t("engagementManagement.status.Confirmed"),
		Cancelled: t("engagementManagement.status.Cancelled"),
		Withdrawn: t("engagementManagement.status.Withdrawn"),
	};

	const locale = resolveDateLocale(i18n.language);

	const timeSlotsById = useMemo(() => {
		const map = new Map<string, TimeSlotDetail>();
		for (const slot of opportunity?.timeSlots ?? []) {
			map.set(slot.id, slot);
		}
		return map;
	}, [opportunity]);

	const [feedbackStats, setFeedbackStats] = useState<{
		averageRating: number | null;
		feedbackCount: number;
	} | null>(null);
	const [checkInPin, setCheckInPin] = useState<string | null>(null);
	const [checkInPinError, setCheckInPinError] = useState<string | null>(null);
	const [retryingCheckInPin, setRetryingCheckInPin] = useState(false);
	const [notFound, setNotFound] = useState(false);
	const [confirming, setConfirming] = useState<string | null>(null);
	const [confirmCancelId, setConfirmCancelId] = useState<string | null>(null);
	const [cancelReason, setCancelReason] = useState("");
	const [cancelling, setCancelling] = useState(false);
	const [cancelError, setCancelError] = useState<string | null>(null);
	const [checkingIn, setCheckingIn] = useState<string | null>(null);
	const [undoingCheckIn, setUndoingCheckIn] = useState<string | null>(null);
	const [qrScannerOpen, setQrScannerOpen] = useState(false);
	const [assignOpen, setAssignOpen] = useState(false);

	const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
	const [bulkConfirming, setBulkConfirming] = useState(false);
	const [bulkCancelOpen, setBulkCancelOpen] = useState(false);
	const [bulkCancelReason, setBulkCancelReason] = useState("");
	const [bulkCancelling, setBulkCancelling] = useState(false);
	const [bulkCancelError, setBulkCancelError] = useState<string | null>(null);

	const [statusFilter, setStatusFilter] = useState("");
	const [timeSlotFilter, setTimeSlotFilter] = useState("");
	const [search, setSearch] = useState("");
	const [appliedSearch, setAppliedSearch] = useState("");
	const hasActiveFilters =
		statusFilter !== "" || timeSlotFilter !== "" || appliedSearch !== "";

	const {
		items: engagements,
		setItems: setEngagements,
		loading,
		loadingMore: engagementsLoadingMore,
		error,
		loadMoreError: engagementsLoadMoreError,
		hasMore: hasMoreEngagements,
		loadMore: loadMoreEngagements,
		retryLoadMore: retryLoadMoreEngagements,
		reset: resetEngagements,
	} = useLoadMore<EngagementSummary>(
		async (page) => {
			if (!opportunityId) return { items: [], pageCount: 0 };
			try {
				const result = await api.getEngagements({
					path: { opportunityId },
					query: {
						pageNumber: page,
						pageSize: ENGAGEMENTS_PAGE_SIZE,
						status: statusFilter || undefined,
						timeSlotId: timeSlotFilter || undefined,
						search: appliedSearch.trim() || undefined,
					},
				});
				return {
					items: result.items,
					pageCount: result.pageCount,
					totalItems: result.totalItems,
				};
			} catch (err) {
				if (isApiNotFoundError(err)) setNotFound(true);
				throw err;
			}
		},
		{
			deps: [statusFilter, timeSlotFilter, appliedSearch],
			getErrorMessage: (err) => getApiErrorMessage(err, t("error.serverError")),
		},
	);

	function handleSearchSubmit(e: React.FormEvent) {
		e.preventDefault();
		setAppliedSearch(search);
	}

	useEffect(() => {
		setSelectedIds(new Set());
	}, [statusFilter, timeSlotFilter, appliedSearch]);

	const pendingIds = engagements
		.filter((e) => e.status === "Pending")
		.map((e) => e.id);
	const actionableCount = engagements.filter(
		(e) => e.status === "Pending" || e.status === "Confirmed",
	).length;
	const selectedPendingCount = pendingIds.filter((id) =>
		selectedIds.has(id),
	).length;
	const allPendingSelected =
		pendingIds.length > 0 && pendingIds.every((id) => selectedIds.has(id));

	function toggleSelected(engagementId: string) {
		setSelectedIds((prev) => {
			const next = new Set(prev);
			if (next.has(engagementId)) next.delete(engagementId);
			else next.add(engagementId);
			return next;
		});
	}

	function toggleSelectAllPending() {
		setSelectedIds((prev) => {
			const next = new Set(prev);
			if (allPendingSelected) {
				for (const id of pendingIds) next.delete(id);
			} else {
				for (const id of pendingIds) next.add(id);
			}
			return next;
		});
	}

	async function handleBulkConfirm() {
		const idsToConfirm = pendingIds.filter((id) => selectedIds.has(id));
		if (!opportunityId || idsToConfirm.length === 0) return;
		setBulkConfirming(true);
		try {
			const result = await api.bulkConfirmEngagements({
				path: { opportunityId },
				body: { engagementIds: idsToConfirm },
			});
			const statusById = new Map(result.succeeded.map((s) => [s.id, s.status]));
			setEngagements((prev) =>
				prev.map((e) => {
					const newStatus = statusById.get(e.id);
					return newStatus ? { ...e, status: newStatus } : e;
				}),
			);
			setSelectedIds((prev) => {
				const next = new Set(prev);
				for (const s of result.succeeded) next.delete(s.id);
				return next;
			});
			if (result.failed.length > 0) {
				dispatchToast(
					"error",
					t("engagementManagement.bulkConfirmPartial", {
						succeeded: result.succeeded.length,
						failed: result.failed.length,
					}),
				);
			} else {
				dispatchToast(
					"success",
					t("engagementManagement.bulkConfirmSuccess", {
						count: result.succeeded.length,
					}),
				);
			}
		} catch (err) {
			dispatchToast(
				"error",
				getApiErrorMessage(err, t("engagementManagement.bulkConfirmError")),
			);
		} finally {
			setBulkConfirming(false);
		}
	}

	async function handleBulkCancelConfirm() {
		if (!opportunityId) return;
		setBulkCancelling(true);
		setBulkCancelError(null);
		try {
			const idsToCancel = Array.from(selectedIds);
			const trimmedReason = bulkCancelReason.trim();
			const result = await api.bulkCancelEngagements({
				path: { opportunityId },
				body: {
					engagementIds: idsToCancel,
					reason: trimmedReason.length > 0 ? trimmedReason : undefined,
				},
			});
			const succeededById = new Map(result.succeeded.map((s) => [s.id, s]));
			setEngagements((prev) =>
				prev.map((e) => {
					const succeeded = succeededById.get(e.id);
					return succeeded
						? {
								...e,
								status: succeeded.status,
								cancellationReason: succeeded.cancellationReason,
							}
						: e;
				}),
			);
			setSelectedIds((prev) => {
				const next = new Set(prev);
				for (const id of idsToCancel) next.delete(id);
				return next;
			});
			setBulkCancelOpen(false);
			setBulkCancelReason("");
			if (result.failed.length > 0) {
				dispatchToast(
					"error",
					t("engagementManagement.bulkCancelPartial", {
						succeeded: result.succeeded.length,
						failed: result.failed.length,
					}),
				);
			} else {
				dispatchToast(
					"success",
					t("engagementManagement.bulkCancelSuccess", {
						count: result.succeeded.length,
					}),
				);
			}
		} catch (err) {
			setBulkCancelError(
				err instanceof Error
					? err.message
					: t("engagementManagement.bulkCancelError"),
			);
		} finally {
			setBulkCancelling(false);
		}
	}

	function handleBulkCancelClose() {
		setBulkCancelOpen(false);
		setBulkCancelReason("");
		setBulkCancelError(null);
	}

	const {
		items: feedbackItems,
		loading: feedbackLoading,
		loadingMore: feedbackLoadingMore,
		error: feedbackError,
		hasMore: hasMoreFeedback,
		loadMore: loadMoreFeedback,
		retryLoadMore: retryLoadMoreFeedback,
	} = useLoadMore<FeedbackItemDto>(async (page) => {
		if (!opportunityId) return { items: [], pageCount: 0 };
		const result = await api.getOpportunityFeedback({
			path: { opportunityId },
			query: { pageNumber: page, pageSize: FEEDBACK_PAGE_SIZE },
		});
		setFeedbackStats({
			averageRating: result.averageRating ?? null,
			feedbackCount: result.feedbackCount,
		});
		return {
			items: result.items.items,
			pageCount: result.items.pageCount,
		};
	});

	function loadOpportunity() {
		if (!opportunityId) return Promise.resolve();
		return api
			.getVolunteerOpportunityDetails({ path: { opportunityId } })
			.then((data) => {
				setOpportunity(data);
				setOpportunityError(null);
			})
			.catch((err) => {
				setOpportunityError(
					getApiErrorMessage(
						err,
						t("engagementManagement.opportunityLoadError"),
					),
				);
			});
	}

	useEffect(() => {
		loadOpportunity();
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [opportunityId]);

	function retryLoadOpportunity() {
		setRetryingOpportunity(true);
		loadOpportunity().finally(() => setRetryingOpportunity(false));
	}

	function loadCheckInPin() {
		if (
			!opportunityId ||
			!isOrganizer ||
			opportunity?.checkInMethod !== "PINCode"
		)
			return Promise.resolve();
		return api
			.getOpportunityCheckInPin({ path: { opportunityId } })
			.then((pin) => {
				setCheckInPin(pin);
				setCheckInPinError(null);
			})
			.catch((err) => {
				setCheckInPinError(
					getApiErrorMessage(
						err,
						t("engagementManagement.checkInPinLoadError"),
					),
				);
			});
	}

	useEffect(() => {
		loadCheckInPin();
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [opportunityId, isOrganizer, opportunity?.checkInMethod]);

	function retryLoadCheckInPin() {
		setRetryingCheckInPin(true);
		loadCheckInPin().finally(() => setRetryingCheckInPin(false));
	}

	function focusEngagementRowControl(testId: string) {
		requestAnimationFrame(() => {
			document.querySelector<HTMLElement>(`[data-testid="${testId}"]`)?.focus();
		});
	}

	function volunteerDisplayName(e: EngagementSummary): string {
		if (e.volunteerName) return e.volunteerName;
		if (e.volunteerId)
			return t("engagementManagement.volunteer", {
				id: e.volunteerId.slice(0, 8) + "...",
			});
		return t("engagementManagement.anonymizedVolunteer");
	}

	async function handleConfirm(engagementId: string) {
		setConfirming(engagementId);
		try {
			const updated = await api.confirmEngagement({
				path: { engagementId },
			});
			setEngagements((prev) =>
				prev.map((e) =>
					e.id === engagementId ? { ...e, status: updated.status } : e,
				),
			);
			dispatchToast("success", t("engagementManagement.confirmSuccess"));
			focusEngagementRowControl(`engagement-revoke-${engagementId}`);
		} catch (err) {
			dispatchToast(
				"error",
				err instanceof Error
					? err.message
					: t("engagementManagement.confirmError"),
			);
		} finally {
			setConfirming(null);
		}
	}

	async function handleCheckIn(engagementId: string) {
		if (!opportunityId) return;
		setCheckingIn(engagementId);
		try {
			await api.checkInEngagement({
				path: { opportunityId, engagementId },
			});
			setEngagements((prev) =>
				prev.map((e) =>
					e.id === engagementId ? { ...e, isCheckedIn: true } : e,
				),
			);
			dispatchToast("success", t("checkIn.markCheckedInSuccess"));
			focusEngagementRowControl(`engagement-undo-checkin-${engagementId}`);
		} catch (err) {
			dispatchToast(
				"error",
				getApiErrorMessage(err, t("checkIn.manualCheckInError")),
			);
		} finally {
			setCheckingIn(null);
		}
	}

	async function handleUndoCheckIn(engagementId: string) {
		setUndoingCheckIn(engagementId);
		try {
			await api.undoCheckInEngagement({ path: { engagementId } });
			setEngagements((prev) =>
				prev.map((e) =>
					e.id === engagementId ? { ...e, isCheckedIn: false } : e,
				),
			);
		} catch (err) {
			dispatchToast(
				"error",
				getApiErrorMessage(err, t("checkIn.undoCheckInError")),
			);
		} finally {
			setUndoingCheckIn(null);
		}
	}

	async function handleCancelConfirm() {
		if (!confirmCancelId) return;
		setCancelling(true);
		setCancelError(null);
		try {
			const trimmedReason = cancelReason.trim();
			const updated = await api.cancelEngagement({
				path: { engagementId: confirmCancelId },
				body: { reason: trimmedReason.length > 0 ? trimmedReason : null },
			});
			setEngagements((prev) =>
				prev.map((e) =>
					e.id === confirmCancelId ? { ...e, status: updated.status } : e,
				),
			);
			setConfirmCancelId(null);
			setCancelReason("");
		} catch (err) {
			setCancelError(
				err instanceof Error
					? err.message
					: t("engagementManagement.cancelError"),
			);
		} finally {
			setCancelling(false);
		}
	}

	function handleCancelClose() {
		setConfirmCancelId(null);
		setCancelReason("");
		setCancelError(null);
	}

	const cancelTarget = engagements.find((e) => e.id === confirmCancelId);

	if (notFound) {
		return <NotFoundPage />;
	}

	const checkInMethod = opportunity?.checkInMethod;
	const showManualCheckIn = isOrganizer && checkInMethod === "Manual";
	const showQrScanner = isOrganizer && checkInMethod === "QRCode";

	return (
		<>
			{opportunityError && (
				<LoadMoreError
					message={opportunityError}
					retrying={retryingOpportunity}
					onRetry={retryLoadOpportunity}
				/>
			)}

			{checkInMethod === "PINCode" && checkInPin && (
				<div className="mb-6 rounded-card border border-brand-200 bg-brand-50 p-4">
					<p className="text-sm font-medium text-brand-900">
						{t("checkIn.organizerPin")}
					</p>
					<p className="mt-1 font-mono text-2xl font-bold tracking-widest text-brand-800">
						{checkInPin}
					</p>
					<p className="mt-1 text-xs text-brand-700">
						{t("checkIn.organizerPinHint")}
					</p>
				</div>
			)}

			{checkInMethod === "PINCode" && !checkInPin && checkInPinError && (
				<LoadMoreError
					message={checkInPinError}
					retrying={retryingCheckInPin}
					onRetry={retryLoadCheckInPin}
				/>
			)}

			{showQrScanner && (
				<div className="mb-6">
					<Button type="button" onClick={() => setQrScannerOpen(true)}>
						<QrCodeIcon className="h-4 w-4" />
						{t("checkIn.qrScanButton")}
					</Button>
				</div>
			)}

			{isOrganizer && opportunity?.status === "Published" && (
				<div className="mb-6">
					<Button
						type="button"
						onClick={() => setAssignOpen(true)}
						data-testid="assign-member-button"
					>
						{t("assignMember.button")}
					</Button>
				</div>
			)}

			{assignOpen && opportunityId && (
				<AssignMemberDialog
					opportunityId={opportunityId}
					members={org.members}
					timeSlots={opportunity?.timeSlots ?? []}
					onClose={() => setAssignOpen(false)}
					onAssigned={() => {
						setAssignOpen(false);
						resetEngagements();
						dispatchToast("success", t("assignMember.success"));
					}}
				/>
			)}

			<div className={`mb-6 ${cardClass} sm:p-5`}>
				<div className="flex flex-wrap items-end gap-3">
					<div>
						<label htmlFor="engagement-status-filter" className={labelClass}>
							{t("engagementManagement.filterLabelStatus")}
						</label>
						<Select
							id="engagement-status-filter"
							value={statusFilter}
							onChange={(e) => setStatusFilter(e.target.value)}
						>
							<option value="">{t("engagementManagement.allStatuses")}</option>
							{Object.entries(STATUS_LABELS).map(([value, label]) => (
								<option key={value} value={value}>
									{label}
								</option>
							))}
						</Select>
					</div>
					{opportunity && opportunity.timeSlots.length > 1 && (
						<div>
							<label
								htmlFor="engagement-timeslot-filter"
								className={labelClass}
							>
								{t("engagementManagement.filterLabelTimeSlot")}
							</label>
							<Select
								id="engagement-timeslot-filter"
								value={timeSlotFilter}
								onChange={(e) => setTimeSlotFilter(e.target.value)}
							>
								<option value="">
									{t("engagementManagement.allTimeSlots")}
								</option>
								{opportunity.timeSlots.map((slot) => (
									<option key={slot.id} value={slot.id}>
										{formatDateTimeRange(
											slot.startDateTime as unknown as string,
											slot.endDateTime as unknown as string,
											i18n.language,
										)}
									</option>
								))}
							</Select>
						</div>
					)}
					<form
						onSubmit={handleSearchSubmit}
						className="flex flex-1 items-end gap-2"
					>
						<div className="min-w-0 flex-1">
							<label htmlFor="engagement-search" className={labelClass}>
								{t("engagementManagement.searchLabel")}
							</label>
							<input
								id="engagement-search"
								type="search"
								value={search}
								onChange={(e) => setSearch(e.target.value)}
								placeholder={t("engagementManagement.searchPlaceholder")}
								className={inputClass}
							/>
						</div>
						<Button type="submit">
							{t("engagementManagement.searchButton")}
						</Button>
					</form>
				</div>
			</div>

			{loading && (
				<div role="status" className="space-y-3">
					<span className="sr-only">{t("engagementManagement.loading")}</span>
					{Array.from({ length: 3 }).map((_, i) => (
						<div
							key={i}
							aria-hidden="true"
							className={`space-y-2 ${cardClass}`}
						>
							<Skeleton className="h-4 w-1/3" />
							<Skeleton className="h-3 w-1/2" />
						</div>
					))}
				</div>
			)}
			{error && (
				<LoadMoreError
					message={t("engagementManagement.error", { message: error })}
					retrying={loading}
					onRetry={retryLoadMoreEngagements}
				/>
			)}

			{!loading &&
				!error &&
				engagements.length === 0 &&
				(hasActiveFilters ? (
					<EmptyState
						title={t("engagementManagement.noResults")}
						message={t("engagementManagement.noResultsHint")}
					/>
				) : (
					<EmptyState
						title={t("engagementManagement.noApplications")}
						message={t("engagementManagement.noApplicationsHint")}
					/>
				))}

			{isOrganizer && !loading && !error && actionableCount > 0 && (
				<div className="mb-3 flex flex-wrap items-center gap-3 rounded-card border border-gray-100 bg-white p-3 text-sm shadow-resting">
					<label
						htmlFor="select-all-pending"
						className="flex items-center gap-2"
					>
						<input
							id="select-all-pending"
							type="checkbox"
							checked={allPendingSelected}
							onChange={toggleSelectAllPending}
							disabled={pendingIds.length === 0}
							className={`h-4 w-4 ${checkboxClass}`}
						/>
						{t("engagementManagement.selectAllPending")}
					</label>
					{selectedIds.size > 0 && (
						<>
							<span className="text-gray-700">
								{t("engagementManagement.selectedCount", {
									count: selectedIds.size,
								})}
							</span>
							{selectedPendingCount > 0 && (
								<Button
									type="button"
									size="sm"
									onClick={handleBulkConfirm}
									disabled={bulkConfirming}
								>
									{bulkConfirming
										? t("engagementManagement.processing")
										: t("engagementManagement.confirmSelected")}
								</Button>
							)}
							<Button
								type="button"
								variant="dangerOutline"
								size="sm"
								onClick={() => setBulkCancelOpen(true)}
							>
								{t("engagementManagement.cancelSelected")}
							</Button>
							<button
								type="button"
								onClick={() => setSelectedIds(new Set())}
								className="text-xs text-gray-500 hover:underline"
							>
								{t("engagementManagement.clearSelection")}
							</button>
						</>
					)}
				</div>
			)}

			{!loading && !error && engagements.length > 0 && (
				<ul className="space-y-3">
					{engagements.map((e) => (
						<li key={e.id} className={cardClass}>
							<div className="flex items-start gap-3">
								{isOrganizer &&
									(e.status === "Pending" || e.status === "Confirmed") && (
										<input
											type="checkbox"
											checked={selectedIds.has(e.id)}
											onChange={() => toggleSelected(e.id)}
											aria-label={t("engagementManagement.selectRow", {
												name: volunteerDisplayName(e),
											})}
											className={`mt-1 h-4 w-4 ${checkboxClass}`}
										/>
									)}
								<div className="flex flex-1 items-start justify-between gap-2">
									<div className="min-w-0">
										<p className="text-sm font-medium text-gray-800">
											{e.volunteerId ? (
												<Link
													to={`/users/${e.volunteerId}`}
													aria-label={t("engagementManagement.viewProfile", {
														name: volunteerDisplayName(e),
													})}
													className="inline-flex items-center gap-1 hover:text-brand-800 hover:underline"
												>
													{e.volunteerName ?? (
														<span className="font-mono text-xs text-gray-500">
															{t("engagementManagement.volunteer", {
																id: e.volunteerId.slice(0, 8) + "...",
															})}
														</span>
													)}
													<ArrowTopRightOnSquareIcon className="h-3 w-3 shrink-0" />
												</Link>
											) : (
												<span className="text-xs text-gray-500 italic">
													{t("engagementManagement.anonymizedVolunteer")}
												</span>
											)}
										</p>
										{(e.volunteerEmail || e.volunteerPhone) && (
											<p className="mt-1 flex flex-wrap gap-x-3 text-xs text-gray-500">
												{e.volunteerEmail && (
													<a
														href={`mailto:${e.volunteerEmail}`}
														className="hover:underline"
													>
														{e.volunteerEmail}
													</a>
												)}
												{e.volunteerPhone && (
													<a
														href={`tel:${e.volunteerPhone}`}
														className="hover:underline"
													>
														{e.volunteerPhone}
													</a>
												)}
											</p>
										)}
										{e.message && (
											<p className="mt-1 text-sm text-gray-700 italic">
												{quotes.open}
												{e.message}
												{quotes.close}
											</p>
										)}
										{e.timeSlotId &&
											(() => {
												const slot = timeSlotsById.get(e.timeSlotId);
												return (
													<p className="mt-1 text-xs text-gray-500">
														{slot
															? formatDateTimeRange(
																	slot.startDateTime as unknown as string,
																	slot.endDateTime as unknown as string,
																	i18n.language,
																)
															: e.timeSlotId.slice(0, 8) + "..."}
													</p>
												);
											})()}
										<p className="mt-1 text-xs text-gray-500">
											{t("engagementManagement.receivedOn", {
												date: formatDate(
													e.createdOn as unknown as string,
													i18n.language,
												),
											})}
										</p>
										{e.isCheckedIn && (
											<Chip tone="success" size="sm" className="mt-2">
												<CheckIconSolid className="h-3 w-3" />
												{t("checkIn.checkedInLabel")}
											</Chip>
										)}
									</div>
									<div className="flex shrink-0 flex-wrap items-center justify-end gap-3">
										<span
											className={`rounded-full border px-2.5 py-0.5 text-xs font-medium ${STATUS_COLORS[e.status] ?? "border-gray-200 bg-gray-100 text-gray-600"}`}
										>
											{STATUS_LABELS[e.status] ?? e.status}
										</span>
										{isOrganizer && e.status === "Pending" && (
											<div className="flex gap-2">
												<Button
													type="button"
													variant="success"
													size="sm"
													onClick={() => handleConfirm(e.id)}
													disabled={confirming === e.id}
													aria-label={t("engagementManagement.confirmNamed", {
														name: volunteerDisplayName(e),
													})}
												>
													{confirming === e.id
														? t("engagementManagement.processing")
														: t("engagementManagement.confirm")}
												</Button>
												<Button
													type="button"
													variant="dangerOutline"
													size="sm"
													onClick={() => setConfirmCancelId(e.id)}
													aria-label={t("engagementManagement.cancelNamed", {
														name: volunteerDisplayName(e),
													})}
												>
													<TrashIcon className="h-3.5 w-3.5" />
													{t("engagementManagement.cancel")}
												</Button>
											</div>
										)}
										{isOrganizer && e.status === "Confirmed" && (
											<div className="flex gap-2">
												{showManualCheckIn && !e.isCheckedIn && (
													<Button
														onClick={() => handleCheckIn(e.id)}
														disabled={checkingIn === e.id}
														size="sm"
													>
														{checkingIn === e.id
															? t("checkIn.markingCheckedIn")
															: t("checkIn.markCheckedIn")}
													</Button>
												)}
												{e.isCheckedIn && (
													<Button
														type="button"
														variant="tertiary"
														size="sm"
														data-testid={`engagement-undo-checkin-${e.id}`}
														onClick={() => handleUndoCheckIn(e.id)}
														disabled={undoingCheckIn === e.id}
													>
														{undoingCheckIn === e.id
															? t("checkIn.undoingCheckIn")
															: t("checkIn.undoCheckIn")}
													</Button>
												)}
												<Button
													type="button"
													variant="dangerOutline"
													size="sm"
													data-testid={`engagement-revoke-${e.id}`}
													onClick={() => setConfirmCancelId(e.id)}
													aria-label={t("engagementManagement.cancelNamed", {
														name: volunteerDisplayName(e),
													})}
												>
													<TrashIcon className="h-3.5 w-3.5" />
													{t("engagementManagement.cancel")}
												</Button>
											</div>
										)}
									</div>
								</div>
							</div>
						</li>
					))}
				</ul>
			)}

			{!loading &&
				!error &&
				engagements.length > 0 &&
				hasMoreEngagements &&
				(engagementsLoadMoreError ? (
					<LoadMoreError
						message={t("engagementManagement.error", {
							message: engagementsLoadMoreError,
						})}
						retrying={engagementsLoadingMore}
						onRetry={retryLoadMoreEngagements}
					/>
				) : (
					<LoadMoreButton
						loading={engagementsLoadingMore}
						label={t("engagementManagement.loadMore")}
						loadingLabel={t("engagementManagement.loading")}
						onClick={loadMoreEngagements}
					/>
				))}

			{qrScannerOpen && opportunityId && (
				<Suspense
					fallback={
						<ModalLoadingFallback onClose={() => setQrScannerOpen(false)} />
					}
				>
					<QRScannerModal
						opportunityId={opportunityId}
						onCheckedIn={(engagementId) => {
							setEngagements((prev) =>
								prev.map((e) =>
									e.id === engagementId ? { ...e, isCheckedIn: true } : e,
								),
							);
						}}
						onClose={() => setQrScannerOpen(false)}
					/>
				</Suspense>
			)}

			{confirmCancelId && (
				<ConfirmDialog
					title={t("confirmDialog.cancel.title")}
					message={t("confirmDialog.cancel.message", {
						name: cancelTarget
							? volunteerDisplayName(cancelTarget)
							: t("engagementManagement.anonymizedVolunteer"),
						opportunity: opportunityTitle ?? t("engagementManagement.title"),
					})}
					confirmLabel={t("confirmDialog.cancel.confirm")}
					onConfirm={handleCancelConfirm}
					onClose={handleCancelClose}
					loading={cancelling}
					error={cancelError}
				>
					<label htmlFor="cancel-reason" className={labelClass}>
						{t("confirmDialog.cancel.reasonLabel")}
					</label>
					<textarea
						id="cancel-reason"
						rows={3}
						maxLength={500}
						value={cancelReason}
						onChange={(e) => setCancelReason(e.target.value)}
						placeholder={t("confirmDialog.cancel.reasonPlaceholder")}
						disabled={cancelling}
						className={textareaClass}
					/>
					<p className="mt-1 text-right text-xs text-gray-500">
						{cancelReason.length}/500
					</p>
				</ConfirmDialog>
			)}

			{bulkCancelOpen && (
				<ConfirmDialog
					title={t("confirmDialog.bulkCancel.title", {
						count: selectedIds.size,
					})}
					message={t("confirmDialog.bulkCancel.message", {
						count: selectedIds.size,
					})}
					confirmLabel={t("confirmDialog.bulkCancel.confirm")}
					onConfirm={handleBulkCancelConfirm}
					onClose={handleBulkCancelClose}
					loading={bulkCancelling}
					error={bulkCancelError}
				>
					<label htmlFor="bulk-cancel-reason" className={labelClass}>
						{t("confirmDialog.cancel.reasonLabel")}
					</label>
					<textarea
						id="bulk-cancel-reason"
						rows={3}
						maxLength={500}
						value={bulkCancelReason}
						onChange={(e) => setBulkCancelReason(e.target.value)}
						placeholder={t("confirmDialog.cancel.reasonPlaceholder")}
						disabled={bulkCancelling}
						className={textareaClass}
					/>
					<p className="mt-1 text-right text-xs text-gray-500">
						{bulkCancelReason.length}/500
					</p>
				</ConfirmDialog>
			)}

			{feedbackStats !== null && feedbackStats.feedbackCount > 0 ? (
				<section className="mt-8">
					<h2 className="mb-4 text-lg font-semibold text-gray-900">
						{t("feedback.organizerTab")}
					</h2>
					<p className="mb-4 text-sm text-gray-700">
						{t("feedback.averageRating", {
							rating:
								feedbackStats.averageRating?.toLocaleString(locale, {
									minimumFractionDigits: 1,
									maximumFractionDigits: 1,
								}) ?? "-",
							count: feedbackStats.feedbackCount,
						})}
					</p>
					<ul className="space-y-3">
						{feedbackItems.map((item, idx) => (
							<li key={idx} className={cardClass}>
								<div className="flex items-center gap-1">
									<StarRating rating={item.rating as number} />
									<span className="ml-1 text-xs text-gray-500">
										{formatDate(
											item.submittedAt as unknown as string,
											i18n.language,
										)}
									</span>
								</div>
								{item.comment && (
									<p className="mt-1 text-sm text-gray-700">{item.comment}</p>
								)}
							</li>
						))}
					</ul>
					{!feedbackLoading &&
						!feedbackError &&
						feedbackItems.length > 0 &&
						hasMoreFeedback && (
							<LoadMoreButton
								loading={feedbackLoadingMore}
								label={t("feedback.loadMore")}
								loadingLabel={t("engagementManagement.loading")}
								onClick={loadMoreFeedback}
							/>
						)}
				</section>
			) : feedbackError ? (
				<section className="mt-8">
					<h2 className="mb-4 text-lg font-semibold text-gray-900">
						{t("feedback.organizerTab")}
					</h2>
					<LoadMoreError
						message={t("feedback.error", { message: feedbackError })}
						retrying={feedbackLoading}
						onRetry={retryLoadMoreFeedback}
					/>
				</section>
			) : null}
		</>
	);
}

import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useOutletContext, useSearchParams } from "react-router";
import { useTranslation } from "react-i18next";
import type {
	VolunteerOpportunityDetails,
	VolunteerOpportunitySummary,
} from "../../client";
import { useApiClient } from "../../hooks/useApiClient";
import { useLoadMore } from "../../hooks/useLoadMore";
import { usePageTitle } from "../../hooks/usePageTitle";
import { dispatchToast } from "../../lib/toastBus";
import { getApiErrorMessage } from "../../lib/apiError";
import {
	checkboxClass,
	labelClass,
	textareaClass,
} from "../../lib/formClasses";
import { cardClass } from "../../lib/surfaceClasses";
import { getOpportunityCapacity } from "../../lib/opportunityCapacity";
import { formatSignUpCount, pickLocalizedText } from "../../lib/format";
import Chip, { type ChipTone } from "../../components/Chip";
import CreateVolunteerOpportunityModal from "../../components/CreateVolunteerOpportunityModal";
import ConfirmDialog from "../../components/ConfirmDialog";
import EmptyState from "../../components/EmptyState";
import Skeleton from "../../components/Skeleton";
import Button from "../../components/Button";
import RowActionsMenu from "../../components/RowActionsMenu";
import LoadMoreError from "../../components/LoadMoreError";
import LoadMoreButton from "../../components/LoadMoreButton";
import PageSectionHeading from "../../components/PageSectionHeading";
import { PlusIcon } from "../../components/QuickActionIcons";
import { ArrowRightIcon } from "../../components/icons";
import { useQuickActions } from "../../contexts/QuickActionsContext";
import type { OrgAppContext } from "../../layouts/OrgAppLayout";

const OPPORTUNITIES_PAGE_SIZE = 10;

/**
 * What a destructive confirmation is about to act on. The title travels with
 * the id so the dialog can name the opportunity rather than leaning on a
 * pronoun with nothing to refer back to (#2325).
 */
interface ConfirmTarget {
	id: string;
	title: string;
}

const STATUS_BADGE_TONE: Record<string, ChipTone> = {
	Draft: "warning",
	Published: "success",
	Unpublished: "neutral",
	Cancelled: "danger",
};

export default function OrgOpportunitiesPage() {
	const { org, isOrganizer } = useOutletContext<OrgAppContext>();
	const { t, i18n } = useTranslation();
	const api = useApiClient();
	const organizationId = org.id;
	usePageTitle(`${t("orgOverview.tabOpportunities")} - ${org.name}`);

	// The generated client types every OpenAPI integer as `number | string`,
	// so the paging counters are narrowed back to numbers here - `useLoadMore`
	// compares `pageCount` against the page it just asked for.
	const [internalOnly, setInternalOnly] = useState(false);

	async function loadOpportunities(status: string, page: number) {
		const result = await api.getOrganizationOpportunities({
			path: { organizationId },
			query: {
				status,
				pageNumber: page,
				pageSize: OPPORTUNITIES_PAGE_SIZE,
				membersOnly: internalOnly ? true : undefined,
			},
		});
		return {
			items: result.items,
			pageCount:
				result.pageCount === undefined ? undefined : Number(result.pageCount),
			totalItems:
				result.totalItems === undefined ? undefined : Number(result.totalItems),
		};
	}

	const {
		items: drafts,
		loading: draftsLoading,
		loadingMore: draftsLoadingMore,
		error: draftsError,
		loadMoreError: draftsLoadMoreError,
		hasMore: hasMoreDrafts,
		loadMore: loadMoreDrafts,
		retryLoadMore: retryLoadMoreDrafts,
		reset: resetDrafts,
	} = useLoadMore<VolunteerOpportunitySummary>(
		(page) => loadOpportunities("Draft", page),
		{
			deps: [organizationId, internalOnly],
			getErrorMessage: (e) => getApiErrorMessage(e, t("error.serverError")),
		},
	);

	const {
		items: published,
		loading: publishedLoading,
		loadingMore: publishedLoadingMore,
		error: publishedError,
		loadMoreError: publishedLoadMoreError,
		hasMore: hasMorePublished,
		loadMore: loadMorePublished,
		retryLoadMore: retryLoadMorePublished,
		reset: resetPublished,
	} = useLoadMore<VolunteerOpportunitySummary>(
		(page) => loadOpportunities("Published", page),
		{
			deps: [organizationId, internalOnly],
			getErrorMessage: (e) => getApiErrorMessage(e, t("error.serverError")),
		},
	);

	const {
		items: unpublished,
		loading: unpublishedLoading,
		loadingMore: unpublishedLoadingMore,
		error: unpublishedError,
		loadMoreError: unpublishedLoadMoreError,
		hasMore: hasMoreUnpublished,
		loadMore: loadMoreUnpublished,
		retryLoadMore: retryLoadMoreUnpublished,
		reset: resetUnpublished,
	} = useLoadMore<VolunteerOpportunitySummary>(
		(page) => loadOpportunities("Unpublished", page),
		{
			deps: [organizationId, internalOnly],
			getErrorMessage: (e) => getApiErrorMessage(e, t("error.serverError")),
		},
	);

	const {
		items: cancelled,
		loading: cancelledLoading,
		loadingMore: cancelledLoadingMore,
		error: cancelledError,
		loadMoreError: cancelledLoadMoreError,
		hasMore: hasMoreCancelled,
		loadMore: loadMoreCancelled,
		retryLoadMore: retryLoadMoreCancelled,
		reset: resetCancelled,
	} = useLoadMore<VolunteerOpportunitySummary>(
		(page) => loadOpportunities("Cancelled", page),
		{
			deps: [organizationId, internalOnly],
			getErrorMessage: (e) => getApiErrorMessage(e, t("error.serverError")),
		},
	);

	function reloadAll() {
		resetDrafts();
		resetPublished();
		resetUnpublished();
		resetCancelled();
	}

	const [showCreate, setShowCreate] = useState(false);
	const [editDetails, setEditDetails] =
		useState<VolunteerOpportunityDetails | null>(null);
	const [editLoadingId, setEditLoadingId] = useState<string | null>(null);
	const [publishingId, setPublishingId] = useState<string | null>(null);
	const [deleteTarget, setDeleteTarget] = useState<ConfirmTarget | null>(null);
	const [deleting, setDeleting] = useState(false);
	const [deleteError, setDeleteError] = useState<string | null>(null);
	const [unpublishTarget, setUnpublishTarget] = useState<ConfirmTarget | null>(
		null,
	);
	const [unpublishing, setUnpublishing] = useState(false);
	const [unpublishError, setUnpublishError] = useState<string | null>(null);
	const [cancelTarget, setCancelTarget] = useState<ConfirmTarget | null>(null);
	const [cancelReason, setCancelReason] = useState("");
	const [cancelling, setCancelling] = useState(false);
	const [cancelError, setCancelError] = useState<string | null>(null);

	const [searchParams, setSearchParams] = useSearchParams();

	const [highlightedId, setHighlightedId] = useState<string | null>(null);
	const highlightRef = useRef<HTMLLIElement | null>(null);

	const quickActions = useMemo(
		() =>
			isOrganizer
				? [
						{
							key: "create-opportunity",
							label: t("orgOverview.createOpportunity"),
							icon: <PlusIcon />,
							onClick: () => setShowCreate(true),
							variant: "primary" as const,
						},
					]
				: [],
		[t, isOrganizer],
	);
	useQuickActions(quickActions);

	useEffect(() => {
		const h = searchParams.get("highlight");
		if (!h) return;
		setHighlightedId(h);
		const next = new URLSearchParams(searchParams);
		next.delete("highlight");
		setSearchParams(next, { replace: true });
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, []);

	useEffect(() => {
		if (!highlightedId || !highlightRef.current) return;
		const reduceMotion = window.matchMedia(
			"(prefers-reduced-motion: reduce)",
		).matches;
		highlightRef.current.scrollIntoView({
			behavior: reduceMotion ? "auto" : "smooth",
			block: "center",
		});
		const timer = setTimeout(() => setHighlightedId(null), 2500);
		return () => clearTimeout(timer);
	}, [highlightedId, drafts, published, unpublished, cancelled]);

	async function openEdit(id: string) {
		setEditLoadingId(id);
		try {
			const details = await api.getVolunteerOpportunityDetails({
				path: { opportunityId: id },
			});
			setEditDetails(details);
		} catch (e) {
			dispatchToast("error", getApiErrorMessage(e, t("error.serverError")));
		} finally {
			setEditLoadingId(null);
		}
	}

	async function publish(id: string) {
		setPublishingId(id);
		try {
			await api.publishVolunteerOpportunity({ path: { opportunityId: id } });
			dispatchToast("success", t("opportunities.publishSuccess"));
			reloadAll();
		} catch (e) {
			dispatchToast("error", getApiErrorMessage(e, t("error.serverError")));
		} finally {
			setPublishingId(null);
		}
	}

	function handleCreated(createdDraftId?: string) {
		reloadAll();
		if (createdDraftId) setHighlightedId(createdDraftId);
	}

	function handleEdited() {
		setEditDetails(null);
		reloadAll();
	}

	async function handleDeleteConfirm() {
		if (!deleteTarget) return;
		setDeleting(true);
		setDeleteError(null);
		try {
			await api.deleteVolunteerOpportunity({
				path: { opportunityId: deleteTarget.id },
			});
			setDeleteTarget(null);
			reloadAll();
		} catch (err) {
			setDeleteError(getApiErrorMessage(err, t("opportunities.deleteError")));
		} finally {
			setDeleting(false);
		}
	}

	async function handleUnpublishConfirm() {
		if (!unpublishTarget) return;
		setUnpublishing(true);
		setUnpublishError(null);
		try {
			await api.unpublishVolunteerOpportunity({
				path: { opportunityId: unpublishTarget.id },
			});
			setUnpublishTarget(null);
			dispatchToast("success", t("opportunities.unpublishSuccess"));
			reloadAll();
		} catch (err) {
			setUnpublishError(getApiErrorMessage(err, t("error.serverError")));
		} finally {
			setUnpublishing(false);
		}
	}

	function handleCancelClose() {
		setCancelTarget(null);
		setCancelReason("");
		setCancelError(null);
	}

	async function handleCancelConfirm() {
		if (!cancelTarget) return;
		setCancelling(true);
		setCancelError(null);
		try {
			const trimmedReason = cancelReason.trim();
			await api.cancelVolunteerOpportunity({
				path: { opportunityId: cancelTarget.id },
				body: {
					reason: trimmedReason.length > 0 ? trimmedReason : null,
				},
			});
			setCancelTarget(null);
			setCancelReason("");
			dispatchToast("success", t("opportunities.cancelSuccess"));
			reloadAll();
		} catch (err) {
			setCancelError(getApiErrorMessage(err, t("error.serverError")));
		} finally {
			setCancelling(false);
		}
	}

	function renderRow(
		item: VolunteerOpportunitySummary,
		showStatusBadge = true,
	) {
		const status = item.status;
		const isHighlighted = item.id === highlightedId;
		const title = pickLocalizedText(item.titleDe, item.titleEn, i18n.language);
		const confirmTarget: ConfirmTarget = {
			id: item.id,
			title: title.text || t("orgDashboard.unnamedDraft"),
		};
		const description = pickLocalizedText(
			item.descriptionDe,
			item.descriptionEn,
			i18n.language,
		);
		const badgeLabel =
			status === "Draft"
				? t("opportunities.draftBadge")
				: status === "Published"
					? t("orgOpportunities.publishedBadge")
					: status === "Unpublished"
						? t("orgOpportunities.unpublishedBadge")
						: status === "Cancelled"
							? t("orgOpportunities.cancelledBadge")
							: status;
		return (
			<li
				key={item.id}
				ref={isHighlighted ? highlightRef : null}
				data-highlighted={isHighlighted ? "true" : undefined}
				data-testid="opportunity-row"
				className={`flex h-full scroll-mt-24 flex-col gap-3 rounded-card border bg-white p-4 shadow-resting transition-shadow hover:shadow-raised ${
					isHighlighted
						? "border-brand-400 ring-2 ring-brand-500 ring-offset-2"
						: "border-gray-100"
				}`}
			>
				<div className="min-w-0">
					<div className="flex items-center gap-2">
						<Link
							to={`/volunteer-opportunities/${item.id}`}
							lang={title.text ? title.lang : undefined}
							className="truncate text-sm font-semibold text-gray-900 hover:text-brand-700 hover:underline"
						>
							{title.text || t("orgDashboard.unnamedDraft")}
						</Link>
						{showStatusBadge && (
							<Chip
								data-testid="opportunity-status-badge"
								tone={STATUS_BADGE_TONE[status] ?? "neutral"}
								size="sm"
								className="shrink-0"
							>
								{badgeLabel}
							</Chip>
						)}
						{item.membersOnly && (
							<Chip
								data-testid="opportunity-members-only-badge"
								tone="warning"
								size="sm"
								className="shrink-0"
							>
								{t("opportunities.membersOnlyBadge")}
							</Chip>
						)}
					</div>
					{description && (
						<p
							lang={description.lang}
							className="mt-0.5 line-clamp-1 text-xs text-gray-500"
						>
							{description.text}
						</p>
					)}

					<p
						data-testid="opportunity-signup-count"
						className="mt-1 text-xs text-gray-500"
					>
						{formatSignUpCount(
							getOpportunityCapacity({
								totalMaxParticipants:
									item.totalMaxParticipants === null
										? null
										: Number(item.totalMaxParticipants),
								currentParticipantCount: Number(item.currentParticipantCount),
								participationType: item.participationType,
							}),
							t,
						)}
					</p>
				</div>

				<div className="mt-auto flex flex-wrap items-center gap-2">
					{isOrganizer && (status === "Draft" || status === "Unpublished") && (
						<Button
							type="button"
							onClick={() => void publish(item.id)}
							disabled={publishingId === item.id}
							data-testid="opportunity-publish"
							size="sm"
						>
							{publishingId === item.id
								? t("opportunities.publishing")
								: t("opportunities.publish")}
						</Button>
					)}
					{status !== "Draft" && (
						<Link
							to={`/app/${organizationId}/dashboard/opportunities/${item.id}/engagements`}
							className="inline-flex items-center gap-1 rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-brand-700 transition hover:bg-brand-50"
						>
							{t("orgOpportunities.manageApplications")}
							<ArrowRightIcon className="h-3.5 w-3.5" />
						</Link>
					)}
					{isOrganizer && (
						<div className="ml-auto">
							<RowActionsMenu
								label={t("orgOpportunities.moreActionsFor", {
									title: title.text || t("orgDashboard.unnamedDraft"),
								})}
								actions={[
									...(status !== "Cancelled"
										? [
												{
													key: "edit",
													label:
														editLoadingId === item.id
															? t("orgOpportunities.editLoading")
															: t("opportunities.edit"),
													disabled: editLoadingId === item.id,
													testId: "opportunity-edit",
													onClick: () => void openEdit(item.id),
												},
											]
										: []),
									...(status === "Published"
										? [
												{
													key: "unpublish",
													label: t("opportunities.unpublish"),
													testId: "opportunity-unpublish",
													onClick: () => {
														setUnpublishTarget(confirmTarget);
														setUnpublishError(null);
													},
												},
											]
										: []),
									...(status === "Published" || status === "Unpublished"
										? [
												{
													key: "cancel",
													label: t("opportunities.cancel"),
													destructive: true,
													testId: "opportunity-cancel",
													onClick: () => {
														setCancelTarget(confirmTarget);
														setCancelReason("");
														setCancelError(null);
													},
												},
											]
										: []),
									{
										key: "delete",
										label: t("opportunities.delete"),
										destructive: true,
										testId: "opportunity-delete",
										onClick: () => {
											setDeleteTarget(confirmTarget);
											setDeleteError(null);
										},
									},
								]}
							/>
						</div>
					)}
				</div>
			</li>
		);
	}

	function renderSection(
		testId: string,
		heading: string,
		description: string,
		items: VolunteerOpportunitySummary[],
		loading: boolean,
		error: string | null,
		hasMore: boolean,
		loadMoreError: string | null,
		loadingMore: boolean,
		onLoadMore: () => void,
		onRetryLoadMore: () => void,
		showStatusBadge = true,
	) {
		if (loading || error || items.length === 0) return null;
		return (
			<section data-testid={testId}>
				<PageSectionHeading description={description}>
					{heading}
				</PageSectionHeading>
				<ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
					{items.map((item) => renderRow(item, showStatusBadge))}
				</ul>
				{hasMore &&
					(loadMoreError ? (
						<LoadMoreError
							message={t("orgOpportunities.error", { message: loadMoreError })}
							retrying={loadingMore}
							onRetry={onRetryLoadMore}
						/>
					) : (
						<LoadMoreButton
							loading={loadingMore}
							label={t("orgOpportunities.loadMore")}
							loadingLabel={t("orgOpportunities.loading")}
							onClick={onLoadMore}
						/>
					))}
			</section>
		);
	}

	const initialLoading =
		draftsLoading || publishedLoading || unpublishedLoading || cancelledLoading;
	const anyError =
		draftsError || publishedError || unpublishedError || cancelledError;
	const totalCount =
		drafts.length + published.length + unpublished.length + cancelled.length;

	return (
		<div>
			<label className="mb-4 flex w-fit cursor-pointer items-center gap-2 text-sm text-gray-700">
				<input
					type="checkbox"
					className={`h-4 w-4 ${checkboxClass}`}
					checked={internalOnly}
					onChange={(e) => setInternalOnly(e.target.checked)}
					data-testid="internal-only-filter"
				/>
				{t("orgOpportunities.internalOnlyFilter")}
			</label>

			{initialLoading && !anyError && (
				<div
					role="status"
					className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3"
				>
					<span className="sr-only">{t("orgOpportunities.loading")}</span>
					{Array.from({ length: 3 }).map((_, i) => (
						<div
							key={i}
							aria-hidden="true"
							className={`space-y-2 ${cardClass}`}
						>
							<Skeleton className="h-4 w-2/3" />
							<Skeleton className="h-3 w-1/2" />
							<Skeleton className="h-3 w-1/3" />
						</div>
					))}
				</div>
			)}

			{draftsError && (
				<LoadMoreError
					message={t("orgOpportunities.error", { message: draftsError })}
					retrying={draftsLoading}
					onRetry={retryLoadMoreDrafts}
				/>
			)}
			{publishedError && (
				<LoadMoreError
					message={t("orgOpportunities.error", { message: publishedError })}
					retrying={publishedLoading}
					onRetry={retryLoadMorePublished}
				/>
			)}
			{unpublishedError && (
				<LoadMoreError
					message={t("orgOpportunities.error", { message: unpublishedError })}
					retrying={unpublishedLoading}
					onRetry={retryLoadMoreUnpublished}
				/>
			)}
			{cancelledError && (
				<LoadMoreError
					message={t("orgOpportunities.error", { message: cancelledError })}
					retrying={cancelledLoading}
					onRetry={retryLoadMoreCancelled}
				/>
			)}

			{!initialLoading && !anyError && totalCount === 0 && (
				<EmptyState
					title={t("orgOpportunities.emptyTitle")}
					message={t("orgOpportunities.emptyDesc")}
					action={
						isOrganizer
							? {
									label: t("orgOverview.createOpportunity"),
									onClick: () => setShowCreate(true),
								}
							: undefined
					}
				/>
			)}

			{totalCount > 0 && (
				<div className="space-y-8">
					{renderSection(
						"drafts-section",
						t("orgOpportunities.draftsHeading"),
						t("orgOpportunities.draftsDesc"),
						drafts,
						draftsLoading,
						draftsError,
						hasMoreDrafts,
						draftsLoadMoreError,
						draftsLoadingMore,
						loadMoreDrafts,
						retryLoadMoreDrafts,
					)}
					{renderSection(
						"published-section",
						t("orgOpportunities.publishedHeading"),
						t("orgOpportunities.publishedDesc"),
						published,
						publishedLoading,
						publishedError,
						hasMorePublished,
						publishedLoadMoreError,
						publishedLoadingMore,
						loadMorePublished,
						retryLoadMorePublished,
						false,
					)}
					{renderSection(
						"unpublished-section",
						t("orgOpportunities.unpublishedHeading"),
						t("orgOpportunities.unpublishedDesc"),
						unpublished,
						unpublishedLoading,
						unpublishedError,
						hasMoreUnpublished,
						unpublishedLoadMoreError,
						unpublishedLoadingMore,
						loadMoreUnpublished,
						retryLoadMoreUnpublished,
					)}
					{renderSection(
						"cancelled-section",
						t("orgOpportunities.cancelledHeading"),
						t("orgOpportunities.cancelledDesc"),
						cancelled,
						cancelledLoading,
						cancelledError,
						hasMoreCancelled,
						cancelledLoadMoreError,
						cancelledLoadingMore,
						loadMoreCancelled,
						retryLoadMoreCancelled,
					)}
				</div>
			)}

			{showCreate && (
				<CreateVolunteerOpportunityModal
					organizationId={organizationId}
					onClose={() => setShowCreate(false)}
					onSuccess={handleCreated}
				/>
			)}

			{editDetails && (
				<CreateVolunteerOpportunityModal
					organizationId={organizationId}
					initialOpportunity={editDetails}
					onClose={() => setEditDetails(null)}
					onSuccess={handleEdited}
				/>
			)}

			{deleteTarget && (
				<ConfirmDialog
					title={t("confirmDialog.delete.title")}
					message={t("confirmDialog.delete.message", {
						title: deleteTarget.title,
					})}
					confirmLabel={t("confirmDialog.delete.confirm")}
					onConfirm={handleDeleteConfirm}
					onClose={() => {
						setDeleteTarget(null);
						setDeleteError(null);
					}}
					loading={deleting}
					error={deleteError}
				/>
			)}

			{unpublishTarget && (
				<ConfirmDialog
					title={t("confirmDialog.unpublish.title")}
					message={t("confirmDialog.unpublish.message", {
						title: unpublishTarget.title,
					})}
					confirmLabel={t("confirmDialog.unpublish.confirm")}
					onConfirm={handleUnpublishConfirm}
					onClose={() => {
						setUnpublishTarget(null);
						setUnpublishError(null);
					}}
					loading={unpublishing}
					error={unpublishError}
				/>
			)}

			{cancelTarget && (
				<ConfirmDialog
					title={t("confirmDialog.cancelOpportunity.title")}
					message={t("confirmDialog.cancelOpportunity.message", {
						title: cancelTarget.title,
					})}
					confirmLabel={t("confirmDialog.cancelOpportunity.confirm")}
					onConfirm={handleCancelConfirm}
					onClose={handleCancelClose}
					loading={cancelling}
					error={cancelError}
				>
					<label htmlFor="cancel-opportunity-reason" className={labelClass}>
						{t("confirmDialog.cancelOpportunity.reasonLabel")}
					</label>
					<textarea
						id="cancel-opportunity-reason"
						rows={3}
						maxLength={500}
						value={cancelReason}
						onChange={(e) => setCancelReason(e.target.value)}
						placeholder={t("confirmDialog.cancelOpportunity.reasonPlaceholder")}
						disabled={cancelling}
						className={textareaClass}
					/>
					<p className="mt-1 text-right text-xs text-gray-500">
						{cancelReason.length}/500
					</p>
				</ConfirmDialog>
			)}
		</div>
	);
}

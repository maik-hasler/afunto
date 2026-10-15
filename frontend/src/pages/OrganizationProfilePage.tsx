import { useEffect, useState } from "react";
import { useParams, useLocation } from "react-router";
import { useAuth } from "react-oidc-context";
import { useTranslation } from "react-i18next";
import type { PublicOrganizationProfileResponse } from "../client";
import OrganizationProfileView from "../components/OrganizationProfileView";
import SectionHeading from "../components/SectionHeading";
import ReportContentModal, {
	type ReportReason,
} from "../components/ReportContentModal";
import Skeleton from "../components/Skeleton";
import DetailLoadFailure from "../components/DetailLoadFailure";
import EmptyState from "../components/EmptyState";
import Button from "../components/Button";
import { useApiClient } from "../hooks/useApiClient";
import { useMyEngagementStates } from "../hooks/useMyEngagementStates";
import { usePageDescription } from "../hooks/usePageDescription";
import { usePageTitle } from "../hooks/usePageTitle";
import { reportIntentSigninArgs } from "../lib/reportIntent";
import { usePendingReportIntent } from "../hooks/usePendingReportIntent";
import { useOnlineStatus } from "../hooks/useOnlineStatus";
import {
	classifyLoadFailure,
	getApiErrorMessage,
	type LoadFailureKind,
} from "../lib/apiError";
import { dispatchToast } from "../lib/toastBus";
import { FlagIcon, GlobeIcon } from "../components/icons";
import PageHeaderBand from "../components/PageHeaderBand";
import OpportunityCard from "../components/OpportunityCard";
import OrgAvatar from "../components/OrgAvatar";

export default function OrganizationProfilePage() {
	const { organizationId } = useParams<{ organizationId: string }>();
	const api = useApiClient();
	const auth = useAuth();
	const location = useLocation();
	const { t, i18n } = useTranslation();
	const engagementStates = useMyEngagementStates();

	const [profile, setProfile] =
		useState<PublicOrganizationProfileResponse | null>(null);
	const [loading, setLoading] = useState(true);
	const [error, setError] = useState<string | null>(null);
	const [failure, setFailure] = useState<LoadFailureKind | null>(null);
	const [showReport, setShowReport] = useState(false);
	const pendingReportTargetId = usePendingReportIntent();

	// Resumes a Report click an anonymous visitor made before being sent through Keycloak: the
	// intent used to be dropped at the redirect, so they came back to an unchanged page (#2326).
	useEffect(() => {
		if (
			auth.isAuthenticated &&
			organizationId &&
			pendingReportTargetId === organizationId
		) {
			setShowReport(true);
		}
	}, [auth.isAuthenticated, organizationId, pendingReportTargetId]);
	const online = useOnlineStatus();

	// A resolved-but-absent profile is the same dead end as a 404.
	const failureKind = loading
		? null
		: (failure ?? (profile ? null : "notFound"));

	// `null` while a failure is on screen: DetailLoadFailure sets the title
	// itself, and letting the loading placeholder win would leave the tab
	// claiming the page is still loading (#2320).
	usePageTitle(failureKind ? null : (profile?.name ?? t("orgProfile.loading")));
	usePageDescription(profile?.description || null);

	function load() {
		setLoading(true);
		if (!organizationId) {
			setFailure("notFound");
			setLoading(false);
			return;
		}
		setError(null);
		setFailure(null);
		return api
			.getPublicOrganizationProfile({ path: { organizationId } })
			.then((data) => {
				setProfile(data);
				setError(null);
				setFailure(null);
			})
			.catch((err) => {
				setError(getApiErrorMessage(err, t("error.serverError")));
				setFailure(classifyLoadFailure(err, online));
			})
			.finally(() => setLoading(false));
	}

	useEffect(() => {
		load();
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [organizationId]);

	if (loading)
		return (
			<div role="status">
				<span className="sr-only">{t("orgProfile.loading")}</span>
				<div className="mb-6 flex items-center gap-4">
					<Skeleton className="h-16 w-16 shrink-0 rounded-full" />
					<Skeleton className="h-6 w-48" />
				</div>
				<div className="max-w-2xl">
					<Skeleton className="mb-2 h-4 w-full" />
					<Skeleton className="mb-6 h-4 w-2/3" />
					<Skeleton className="mb-3 h-3 w-32" />
					<div className="space-y-3">
						{Array.from({ length: 2 }).map((_, i) => (
							<Skeleton key={i} className="h-20 w-full" />
						))}
					</div>
				</div>
			</div>
		);
	if (failureKind || !profile)
		return (
			<DetailLoadFailure
				kind={failureKind ?? "notFound"}
				notFoundTitle={t("orgProfile.notFoundTitle")}
				notFoundMessage={t("orgProfile.notFoundMessage")}
				errorMessage={error ?? t("error.serverError")}
				onRetry={load}
				action={{ label: t("nav.organizations"), to: "/organizations" }}
				data-testid="organization-load-failure"
			/>
		);

	async function handleReportSubmit(reason: ReportReason, details: string) {
		if (!organizationId) return;
		await api.reportOrganization({
			path: { organizationId },
			body: {
				reason,
				details: details || null,
			},
		});
		dispatchToast("success", t("report.submitSuccess"));
	}

	return (
		<>
			<PageHeaderBand
				eyebrow={t("orgProfile.eyebrow")}
				title={profile.name}
				avatar={
					<OrgAvatar name={profile.name} logoUrl={profile.logoUrl} size="3xl" />
				}
				lead={profile.description ?? undefined}
				leadLang="de"
			>
				{/* Organizations carry no English description to fall back
				from, so on an English page this lead is always the German
				original - the same disclosure a German-only opportunity
				already gets on its own header band (#2328). */}
				{profile.description && i18n.language !== "de" && (
					<p className="mb-4 text-sm text-brand-200">
						{t("opportunities.germanOnlyNotice")}
					</p>
				)}
				{profile.website && (
					<Button
						variant="onDark"
						size="sm"
						href={profile.website}
						target="_blank"
						rel="noopener noreferrer"
						data-testid="organization-website"
					>
						<GlobeIcon className="h-4 w-4" />
						{t("orgProfile.visitWebsite")}
					</Button>
				)}
			</PageHeaderBand>

			<OrganizationProfileView
				showHeader={false}
				centered
				name={profile.name}
				contactEmail={profile.contactEmail}
				contactPhone={profile.contactPhone}
				website={profile.website}
				address={profile.address}
			>
				<SectionHeading>{t("orgProfile.currentNeeds")}</SectionHeading>

				{profile.openOpportunities.length === 0 ? (
					<EmptyState title={t("orgProfile.noOpportunities")} />
				) : (
					<ul className="grid gap-4 sm:grid-cols-2">
						{profile.openOpportunities.map((opp) => (
							<OpportunityCard
								key={opp.id}
								item={opp}
								headingLevel={3}
								viewerEngagementStatus={engagementStates.get(opp.id)}
							/>
						))}
					</ul>
				)}

				<div className="mt-10 border-t border-gray-100 pt-6">
					<button
						type="button"
						onClick={() => {
							if (auth.isAuthenticated || !organizationId) {
								setShowReport(true);
								return;
							}
							void auth.signinRedirect(
								reportIntentSigninArgs(
									location.pathname,
									location.search,
									organizationId,
								),
							);
						}}
						data-testid="report-organization"
						aria-label={t("orgProfile.reportOrganization")}
						className="inline-flex items-center gap-1.5 text-sm text-gray-500 transition-colors hover:text-gray-700"
					>
						<FlagIcon className="h-4 w-4" />
						{t("orgProfile.report")}
					</button>
				</div>
			</OrganizationProfileView>

			{showReport && (
				<ReportContentModal
					targetLabel={profile.name}
					onSubmit={handleReportSubmit}
					onClose={() => setShowReport(false)}
				/>
			)}
		</>
	);
}

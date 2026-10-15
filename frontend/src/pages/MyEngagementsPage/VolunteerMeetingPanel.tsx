import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { EngagementSummary } from "../../client";
import Button from "../../components/Button";
import ConfirmDialog from "../../components/ConfirmDialog";
import MeetingDetails from "../../components/MeetingDetails";
import MeetingFormModal, {
	type MeetingFormValues,
} from "../../components/MeetingFormModal";
import { useApiClient } from "../../hooks/useApiClient";
import { getApiErrorMessage } from "../../lib/apiError";
import { meetingState } from "../../lib/meetings";
import { dispatchToast } from "../../lib/toastBus";

interface Props {
	engagement: EngagementSummary;
	/** Reloads the list, so the card shows what the server now holds. */
	onChanged: () => void;
}

/**
 * The volunteer's side of arranging a meeting for an expression of interest:
 * answer the organization's proposal, suggest another time, or propose one
 * unprompted. Whatever is sent replaces the current proposal.
 */
export default function VolunteerMeetingPanel({
	engagement,
	onChanged,
}: Props) {
	const api = useApiClient();
	const { t } = useTranslation();
	const [formOpen, setFormOpen] = useState(false);
	const [withdrawOpen, setWithdrawOpen] = useState(false);
	const [busy, setBusy] = useState(false);
	const [withdrawError, setWithdrawError] = useState<string | null>(null);

	const isActive =
		engagement.status === "Pending" || engagement.status === "Confirmed";
	const meeting = engagement.meeting ?? null;
	if (!meeting && !isActive) return null;

	const engagementId = engagement.id;
	const state = meeting ? meetingState(meeting, "Volunteer") : null;
	const isOwnProposal = meeting?.proposedBy === "Volunteer";
	const isGroup = (meeting?.participantCount ?? 0) > 1;

	async function handleSubmit(values: MeetingFormValues) {
		await api.proposeMeetingAsVolunteer({
			path: { engagementId },
			body: values,
		});
		dispatchToast("success", t("meetings.proposed"));
		onChanged();
	}

	async function respond(accept: boolean) {
		if (!meeting) return;
		setBusy(true);
		try {
			await api.respondToMeetingAsVolunteer({
				path: { engagementId, meetingId: meeting.id },
				body: { accept },
			});
			dispatchToast(
				"success",
				t(accept ? "meetings.accepted" : "meetings.declined"),
			);
			onChanged();
		} catch (err) {
			dispatchToast(
				"error",
				getApiErrorMessage(err, t("meetings.actionError")),
			);
		} finally {
			setBusy(false);
		}
	}

	async function handleWithdrawConfirm() {
		if (!meeting) return;
		setBusy(true);
		setWithdrawError(null);
		try {
			await api.cancelMeetingAsVolunteer({
				path: { engagementId, meetingId: meeting.id },
			});
			setWithdrawOpen(false);
			dispatchToast("success", t("meetings.cancelled"));
			onChanged();
		} catch (err) {
			setWithdrawError(getApiErrorMessage(err, t("meetings.actionError")));
		} finally {
			setBusy(false);
		}
	}

	const proposeButton = (label: string) => (
		<Button
			type="button"
			variant={meeting ? "tertiary" : "brandOutline"}
			size="sm"
			onClick={() => setFormOpen(true)}
		>
			{label}
		</Button>
	);

	return (
		<>
			{meeting && state ? (
				<MeetingDetails meeting={meeting} viewer="Volunteer">
					{state === "awaitingYou" && (
						<>
							<Button
								type="button"
								size="sm"
								onClick={() => void respond(true)}
								disabled={busy}
							>
								{t("meetings.accept")}
							</Button>
							<Button
								type="button"
								variant="dangerOutline"
								size="sm"
								onClick={() => void respond(false)}
								disabled={busy}
							>
								{t("meetings.decline")}
							</Button>
						</>
					)}
					{state === "accepted" && !isOwnProposal && (
						<Button
							type="button"
							variant="dangerOutline"
							size="sm"
							onClick={() => void respond(false)}
							disabled={busy}
						>
							{t("meetings.cantMakeIt")}
						</Button>
					)}
					{isOwnProposal && state !== "over" && state !== "declined" && (
						<Button
							type="button"
							variant="dangerOutline"
							size="sm"
							onClick={() => {
								setWithdrawError(null);
								setWithdrawOpen(true);
							}}
							disabled={busy}
						>
							{t("meetings.withdraw")}
						</Button>
					)}
					{isActive &&
						(state === "over" || state === "declined"
							? proposeButton(t("meetings.propose"))
							: proposeButton(t("meetings.counter")))}
				</MeetingDetails>
			) : (
				<div>{proposeButton(t("meetings.propose"))}</div>
			)}
			{meeting && isGroup && state !== "over" && (
				<p className="text-xs text-gray-600">{t("meetings.groupPrivacy")}</p>
			)}

			{formOpen && (
				<MeetingFormModal
					title={meeting ? t("meetings.counter") : t("meetings.propose")}
					intro={
						isGroup && state !== "over"
							? t("meetings.counterIntroGroup")
							: t("meetings.proposeIntroToOrganization")
					}
					onSubmit={handleSubmit}
					onClose={() => setFormOpen(false)}
				/>
			)}

			{withdrawOpen && (
				<ConfirmDialog
					title={t("meetings.withdrawTitle")}
					message={t("meetings.withdrawMessage")}
					confirmLabel={t("meetings.withdraw")}
					cancelLabel={t("meetings.keep")}
					onConfirm={() => void handleWithdrawConfirm()}
					onClose={() => setWithdrawOpen(false)}
					loading={busy}
					error={withdrawError}
				/>
			)}
		</>
	);
}

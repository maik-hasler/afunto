import { useState } from "react";
import { useTranslation } from "react-i18next";
import type { OrganizationMemberDto, TimeSlotDetail } from "../client";
import { useApiClient } from "../hooks/useApiClient";
import { getApiErrorMessage } from "../lib/apiError";
import { labelClass } from "../lib/formClasses";
import { formatDateTimeRange } from "../lib/format";
import ConfirmDialog from "./ConfirmDialog";
import Select from "./Select";

interface Props {
	opportunityId: string;
	members: OrganizationMemberDto[];
	/** Slots the member can be assigned to; empty for an opportunity without time slots. */
	timeSlots: TimeSlotDetail[];
	onClose: () => void;
	onAssigned: () => void;
}

function memberLabel(member: OrganizationMemberDto): string {
	const fullName = [member.firstName, member.lastName]
		.filter(Boolean)
		.join(" ");
	return fullName ? `${fullName} (${member.username})` : member.username;
}

/**
 * An organizer hands an opportunity to one of the organization's members: the
 * member is signed up and confirmed in one step (afunto#2406).
 */
export default function AssignMemberDialog({
	opportunityId,
	members,
	timeSlots,
	onClose,
	onAssigned,
}: Props) {
	const { t, i18n } = useTranslation();
	const api = useApiClient();
	const [memberId, setMemberId] = useState("");
	const [timeSlotId, setTimeSlotId] = useState("");
	const [assigning, setAssigning] = useState(false);
	const [error, setError] = useState<string | null>(null);

	const needsSlot = timeSlots.length > 0;
	const canSubmit = memberId !== "" && (!needsSlot || timeSlotId !== "");

	async function handleAssign() {
		if (!canSubmit) {
			setError(t("assignMember.incomplete"));
			return;
		}
		setAssigning(true);
		setError(null);
		try {
			await api.assignMemberToOpportunity({
				path: { opportunityId },
				body: { memberId, timeSlotId: needsSlot ? timeSlotId : null },
			});
			onAssigned();
		} catch (err) {
			setError(getApiErrorMessage(err, t("error.serverError")));
		} finally {
			setAssigning(false);
		}
	}

	return (
		<ConfirmDialog
			title={t("assignMember.title")}
			message={t("assignMember.message")}
			confirmLabel={t("assignMember.confirm")}
			cancelLabel={t("assignMember.cancel")}
			tone="constructive"
			onConfirm={handleAssign}
			onClose={onClose}
			loading={assigning}
			error={error}
		>
			<div className="mt-4 space-y-4">
				<div>
					<label htmlFor="assign-member" className={labelClass}>
						{t("assignMember.memberLabel")}
					</label>
					<Select
						id="assign-member"
						value={memberId}
						onChange={(e) => setMemberId(e.target.value)}
					>
						<option value="">{t("assignMember.memberPlaceholder")}</option>
						{members.map((member) => (
							<option key={member.userId} value={member.userId}>
								{memberLabel(member)}
							</option>
						))}
					</Select>
				</div>
				{needsSlot && (
					<div>
						<label htmlFor="assign-time-slot" className={labelClass}>
							{t("assignMember.slotLabel")}
						</label>
						<Select
							id="assign-time-slot"
							value={timeSlotId}
							onChange={(e) => setTimeSlotId(e.target.value)}
						>
							<option value="">{t("assignMember.slotPlaceholder")}</option>
							{timeSlots.map((slot) => (
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
			</div>
		</ConfirmDialog>
	);
}

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { TimeSlotDetail } from "../client";
import { useApiClient } from "../hooks/useApiClient";
import { useInvalidateMyEngagementStates } from "../hooks/useMyEngagementStates";
import {
	computeSpotsLeft,
	formatDateTimeRange,
	isSlotFull,
} from "../lib/format";
import { getApiErrorMessage, isApiErrorCode } from "../lib/apiError";
import { getTextareaClass } from "../lib/formClasses";
import CharCount from "./CharCount";
import Dropdown from "./Dropdown";
import Modal from "./Modal";
import Button from "./Button";
import ErrorBanner from "./ErrorBanner";
import Field from "./Field";
import { RequiredFieldsLegend } from "./RequiredMark";
import { inlineLinkClass } from "../lib/linkClasses";

const MESSAGE_MAX_LENGTH = 500;

interface Props {
	opportunityId: string;
	organizationId: string;
	participationType: string;
	timeSlots: TimeSlotDetail[];
	/** Slots this volunteer already holds - offering them is a guaranteed 409 (#2323). */
	engagedTimeSlotIds?: string[];

	preselectedTimeSlotId?: string;
	onClose: () => void;
	onSuccess: () => void;
}

export default function SignUpModal({
	opportunityId,
	organizationId,
	participationType,
	timeSlots,
	engagedTimeSlotIds = [],
	preselectedTimeSlotId,
	onClose,
	onSuccess,
}: Props) {
	const api = useApiClient();
	const invalidateMyEngagementStates = useInvalidateMyEngagementStates();
	const { t, i18n } = useTranslation();

	function isAlreadySignedUp(timeSlotId: string): boolean {
		return engagedTimeSlotIds.includes(timeSlotId);
	}

	const confirmedTimeSlot =
		preselectedTimeSlotId && !isAlreadySignedUp(preselectedTimeSlotId)
			? timeSlots.find((ts) => ts.id === preselectedTimeSlotId)
			: undefined;
	const [selectedTimeSlotId, setSelectedTimeSlotId] = useState<string>(() => {
		if (confirmedTimeSlot) return confirmedTimeSlot.id;
		const availableSlots = timeSlots.filter(
			(ts) =>
				!isSlotFull(ts.maxParticipants, ts.bookedCount) &&
				!isAlreadySignedUp(ts.id),
		);
		return availableSlots.length === 1 ? availableSlots[0].id : "";
	});
	const [message, setMessage] = useState("");
	const [submitting, setSubmitting] = useState(false);
	const [error, setError] = useState<string | null>(null);

	const [showContactOrganizationLink, setShowContactOrganizationLink] =
		useState(false);
	const [messageError, setMessageError] = useState<string | null>(null);
	const messageFieldRef = useRef<HTMLTextAreaElement>(null);

	const isScheduledSlots = participationType === "ScheduledSlots";

	useEffect(() => {
		if (!messageError) return;
		messageFieldRef.current?.focus();
	}, [messageError]);

	async function handleSubmit(e: React.FormEvent) {
		e.preventDefault();
		setError(null);
		setShowContactOrganizationLink(false);
		if (isScheduledSlots && timeSlots.length > 0 && !selectedTimeSlotId) {
			setError(t("signUp.selectTimeSlotRequired"));
			return;
		}
		if (!isScheduledSlots && !message.trim()) {
			setMessageError(t("signUp.messageRequired"));
			return;
		}
		setMessageError(null);
		setSubmitting(true);

		try {
			await api.createEngagement({
				path: { opportunityId },
				body: {
					type: isScheduledSlots ? "ScheduledSlots" : "IndividualContact",
					timeSlotId:
						isScheduledSlots && selectedTimeSlotId ? selectedTimeSlotId : null,
					message: !isScheduledSlots ? message : null,
				},
			});
			void invalidateMyEngagementStates();
			onSuccess();
			onClose();
		} catch (err) {
			setError(getApiErrorMessage(err, t("signUp.unknownError")));
			setShowContactOrganizationLink(
				isApiErrorCode(err, "Engagement.ReactivationLimitReached"),
			);
		} finally {
			setSubmitting(false);
		}
	}

	return (
		<Modal
			onClose={onClose}
			labelledBy="sign-up-dialog-title"
			maxWidth="max-w-md"
		>
			<h2 id="sign-up-dialog-title" className="mb-4 text-lg font-semibold">
				{isScheduledSlots
					? confirmedTimeSlot
						? t("signUp.titleConfirm")
						: t("signUp.titleWaitlist")
					: t("signUp.titleInterest")}
			</h2>

			<form onSubmit={handleSubmit} className="space-y-4">
				{isScheduledSlots && (
					<div>
						{timeSlots.length === 0 ? (
							<p className="text-sm text-gray-500">{t("signUp.noTimeSlots")}</p>
						) : confirmedTimeSlot ? (
							<p
								className="text-sm text-gray-700"
								data-testid="sign-up-confirmed-slot"
							>
								{t("signUp.confirmTimeSlot", {
									range: formatDateTimeRange(
										confirmedTimeSlot.startDateTime as unknown as string,
										confirmedTimeSlot.endDateTime as unknown as string,
										i18n.language,
									),
								})}
							</p>
						) : (
							<>
								<label htmlFor="sign-up-time-slot" className="sr-only">
									{t("signUp.selectTimeSlot")}
								</label>
								<Dropdown
									id="sign-up-time-slot"
									value={selectedTimeSlotId}
									onChange={setSelectedTimeSlotId}
									placeholder={t("signUp.selectPlaceholder")}
									className="mt-1 w-full rounded-xl border border-gray-200 bg-white px-3 py-2 text-sm shadow-sm transition focus:border-brand-400"
									options={timeSlots.map((ts) => {
										const spotsLeft = computeSpotsLeft(
											ts.maxParticipants,
											ts.bookedCount,
										);
										const slotFull = isSlotFull(
											ts.maxParticipants,
											ts.bookedCount,
										);
										const alreadySignedUp = isAlreadySignedUp(ts.id);
										return {
											value: ts.id,
											disabled: slotFull || alreadySignedUp,
											label: `${formatDateTimeRange(
												ts.startDateTime as unknown as string,
												ts.endDateTime as unknown as string,
												i18n.language,
											)} · ${
												alreadySignedUp
													? t("signUp.alreadySignedUpSlot")
													: spotsLeft === null
														? t("opportunities.unlimitedSpots")
														: slotFull
															? t("opportunities.full")
															: t("opportunities.spotsLeft", {
																	count: spotsLeft,
																})
											}`,
										};
									})}
								/>
							</>
						)}
					</div>
				)}

				{!isScheduledSlots && (
					<div>
						<RequiredFieldsLegend className="mb-2" />
						<Field
							label={t("signUp.message")}
							id="sign-up-message"
							required
							error={messageError ?? undefined}
						>
							<textarea
								id="sign-up-message"
								ref={messageFieldRef}
								value={message}
								onChange={(e) => {
									setMessage(e.target.value);
									if (messageError) setMessageError(null);
								}}
								aria-required="true"
								aria-invalid={messageError ? true : undefined}
								aria-describedby={
									messageError ? "sign-up-message-error" : undefined
								}
								rows={4}
								maxLength={MESSAGE_MAX_LENGTH}
								placeholder={t("signUp.messagePlaceholder")}
								className={getTextareaClass(Boolean(messageError))}
							/>
						</Field>
						{!messageError && (
							<CharCount current={message.length} max={MESSAGE_MAX_LENGTH} />
						)}
					</div>
				)}

				{error && (
					<>
						<ErrorBanner message={error} />
						{showContactOrganizationLink && (
							<Link
								to={`/organizations/${organizationId}`}
								className={`mt-1 inline-block text-sm ${inlineLinkClass}`}
							>
								{t("common.contactOrganization")}
							</Link>
						)}
					</>
				)}

				<div className="flex justify-end gap-2">
					<Button type="button" variant="secondary" onClick={onClose}>
						{t("signUp.cancel")}
					</Button>
					<Button
						type="submit"
						disabled={
							submitting || (isScheduledSlots && timeSlots.length === 0)
						}
					>
						{submitting
							? t("signUp.submitting")
							: isScheduledSlots
								? t("signUp.submitWaitlist")
								: t("signUp.submitInterest")}
					</Button>
				</div>
			</form>
		</Modal>
	);
}

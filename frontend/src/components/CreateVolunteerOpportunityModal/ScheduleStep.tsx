import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { Controller } from "react-hook-form";
import type { Control, UseFormRegister, UseFormWatch } from "react-hook-form";
import DatePicker from "../DatePicker";
import Chip from "../Chip";
import Button from "../Button";
import { PlusIcon } from "../icons";
import { formatDateTimeRange } from "../../lib/format";
import { labelClass } from "../../lib/formClasses";
import { ChoiceGroup, FloatingField } from "./shared";
import type { OpportunityFormValues } from "./schema";
import { findOverlappingSlotIds } from "./timeSlots";

/** Where focus lands when the slot editor closes after adding a slot. */
export const ADD_SLOT_BUTTON_ID = "add-time-slot";

/** Where focus lands when the slot editor closes after editing this slot. */
export function editSlotButtonId(slotId: string): string {
	return `edit-slot-${slotId}`;
}

export type SeriesEditScope = "Only" | "ThisAndFollowing" | "EntireSeries";

export interface TimeSlotRow {
	id: string;
	startDateTime: string;
	endDateTime: string;
	maxParticipants: number | null;
	bookedCount: number;
	persisted: boolean;

	seriesId?: string;
	recurrenceFrequency?: string;
	recurrenceCount?: number;

	seriesPosition?: number;
}

interface Props {
	register: UseFormRegister<OpportunityFormValues>;
	watch: UseFormWatch<OpportunityFormValues>;
	control: Control<OpportunityFormValues>;
	setCheckInPin: (pin: string) => void;
	checkInPinError?: string;
	allTimeSlots: TimeSlotRow[];
	removingSlotId: string | null;
	onRemoveExistingSlot: (id: string, bookedCount: number) => void;
	onRemovePendingSlot: (id: string) => void;
	onRequestRemoveSeriesSlot: (id: string, bookedCount: number) => void;
	onStartEditSlot: (slot: TimeSlotRow) => void;
	onOpenAddSlot: () => void;
	slotError: string | null;

	/**
	 * True while editing an opportunity that already exists, where adding,
	 * editing or removing a slot writes straight to the server rather than
	 * being staged for the wizard's own Save button (#2315).
	 */
	slotChangesAreImmediate: boolean;
}

const PIN_RANGE = 900000;
// The largest multiple of PIN_RANGE a Uint32 can hold. A draw at or above it
// is thrown away rather than folded back with `%`, which would make the low
// PINs slightly more likely than the high ones.
const PIN_DRAW_LIMIT = Math.floor(2 ** 32 / PIN_RANGE) * PIN_RANGE;

function generateRandomPin(): string {
	const draw = new Uint32Array(1);
	do {
		crypto.getRandomValues(draw);
	} while (draw[0] >= PIN_DRAW_LIMIT);
	return String(100000 + (draw[0] % PIN_RANGE));
}

/**
 * How volunteers take part and when: one step, because the participation
 * type decides what the rest of it asks for - time slots or an interest
 * deadline - and the two used to sit a step apart (#2387).
 */
export default function ScheduleStep({
	register,
	watch,
	control,
	setCheckInPin,
	checkInPinError,
	allTimeSlots,
	removingSlotId,
	onRemoveExistingSlot,
	onRemovePendingSlot,
	onRequestRemoveSeriesSlot,
	onStartEditSlot,
	onOpenAddSlot,
	slotError,
	slotChangesAreImmediate,
}: Props) {
	const { t, i18n } = useTranslation();
	const occurrence = watch("occurrence");
	const participationType = watch("participationType");
	const checkInMethod = watch("checkInMethod");
	const checkInPin = watch("checkInPin");
	const isScheduledSlots = participationType === "ScheduledSlots";

	const today = new Date();
	const todayDateInputValue = new Date(
		today.getTime() - today.getTimezoneOffset() * 60000,
	)
		.toISOString()
		.slice(0, 10);

	// Two slots covering the same hour let one volunteer sign up for both and
	// be double-booked, so the list flags them; the slot editor warns before
	// one more is added (#2325).
	const overlappingSlotIds = useMemo(
		() => findOverlappingSlotIds(allTimeSlots),
		[allTimeSlots],
	);

	function capacityLabel(maxParticipants: number | null): string {
		return maxParticipants === null
			? t("timeSlots.capacityUnlimited")
			: t("timeSlots.capacity", { count: maxParticipants });
	}

	return (
		<div className="space-y-6" data-testid="wizard-step-3">
			<ChoiceGroup
				legend={t("createOpportunity.fieldParticipationType")}
				name="participationType"
				value={participationType}
				registration={register("participationType")}
				options={[
					{ value: "ScheduledSlots", label: t("opportunities.waitlist") },
					{
						value: "IndividualContact",
						label: t("opportunities.individualContact"),
					},
				]}
			/>

			<ChoiceGroup
				legend={t("createOpportunity.fieldFrequency")}
				name="occurrence"
				value={occurrence}
				registration={register("occurrence")}
				options={[
					{ value: "OneTime", label: t("opportunities.oneTime") },
					{ value: "Recurring", label: t("opportunities.recurring") },
				]}
			/>

			{isScheduledSlots ? (
				<section aria-labelledby="time-slots-heading">
					<h4
						id="time-slots-heading"
						className="text-sm font-semibold text-gray-800"
					>
						{t("timeSlots.sectionTitle")}
					</h4>
					<p className="mt-0.5 mb-3 text-xs text-gray-500">
						{slotChangesAreImmediate
							? t("timeSlots.immediateHint")
							: t("timeSlots.requiredHint")}
					</p>

					{allTimeSlots.length === 0 ? (
						<p className="mb-3 text-sm text-gray-500">
							{t("timeSlots.noSlots")}
						</p>
					) : (
						<ul
							className="mb-3 divide-y divide-gray-200 rounded-xl border border-gray-200"
							data-testid="time-slot-list"
						>
							{allTimeSlots.map((slot) => (
								<li
									key={slot.id}
									className="flex flex-col gap-1 px-3 py-2 text-sm sm:flex-row sm:items-center sm:justify-between"
								>
									<span className="text-gray-700">
										{formatDateTimeRange(
											slot.startDateTime,
											slot.endDateTime,
											i18n.language,
										)}
										<span className="text-gray-500">
											{" · "}
											{capacityLabel(slot.maxParticipants)}
										</span>
										{overlappingSlotIds.has(slot.id) && (
											<Chip tone="warning" size="sm" className="ml-2">
												{t("timeSlots.overlapWarning")}
											</Chip>
										)}
										{slot.seriesId && slot.recurrenceCount && (
											<Chip tone="brand" size="sm" className="ml-2">
												{t("timeSlots.seriesBadge", {
													frequency: t(
														`timeSlots.recurrence${slot.recurrenceFrequency}`,
													),
													position: slot.seriesPosition,
													count: slot.recurrenceCount,
												})}
											</Chip>
										)}
										{slot.bookedCount > 0 && (
											<span className="ml-2 text-xs text-gray-500">
												{t("timeSlots.bookedCount", {
													count: slot.bookedCount,
												})}
											</span>
										)}
									</span>
									<span className="flex shrink-0 items-center gap-1 sm:ml-2">
										{slot.persisted && (
											<button
												type="button"
												id={editSlotButtonId(slot.id)}
												onClick={() => onStartEditSlot(slot)}
												className="min-h-6 rounded px-2 text-xs text-brand-700 hover:underline"
											>
												{t("timeSlots.editButton")}
											</button>
										)}
										<button
											type="button"
											disabled={slot.persisted && removingSlotId === slot.id}
											onClick={() => {
												if (slot.persisted && slot.seriesId) {
													onRequestRemoveSeriesSlot(slot.id, slot.bookedCount);
												} else if (slot.persisted) {
													onRemoveExistingSlot(slot.id, slot.bookedCount);
												} else {
													onRemovePendingSlot(slot.id);
												}
											}}
											className="min-h-6 rounded px-2 text-xs text-red-600 hover:underline disabled:opacity-50"
										>
											{slot.persisted && removingSlotId === slot.id
												? t("timeSlots.removing")
												: t("timeSlots.removeButton")}
										</button>
									</span>
								</li>
							))}
						</ul>
					)}

					{slotError && (
						<p className="mb-3 text-xs text-red-600" role="alert">
							{slotError}
						</p>
					)}

					<Button
						type="button"
						id={ADD_SLOT_BUTTON_ID}
						variant="brandOutline"
						fullWidth
						onClick={onOpenAddSlot}
					>
						<PlusIcon className="h-4 w-4" />
						{t("timeSlots.addTitle")}
					</Button>
				</section>
			) : (
				<div>
					<label
						htmlFor="create-valid-until"
						className={`mb-1.5 ${labelClass}`}
					>
						{t("createOpportunity.fieldValidUntil")}
					</label>
					<Controller
						name="validUntil"
						control={control}
						render={({ field }) => (
							<DatePicker
								id="create-valid-until"
								value={field.value}
								min={todayDateInputValue}
								onChange={field.onChange}
							/>
						)}
					/>
					<p className="mt-1.5 text-xs text-gray-500">
						{t("createOpportunity.validUntilHint")}
					</p>
				</div>
			)}

			<ChoiceGroup
				legend={t("createOpportunity.fieldCheckInMethod")}
				name="checkInMethod"
				value={checkInMethod}
				registration={register("checkInMethod")}
				options={[
					{ value: "None", label: t("checkInMethod.none") },
					{ value: "QRCode", label: t("checkInMethod.qrCode") },
					{ value: "PINCode", label: t("checkInMethod.pinCode") },
					{ value: "Manual", label: t("checkInMethod.manual") },
				]}
			>
				{checkInMethod === "None" && isScheduledSlots && (
					<p className="mt-2 text-xs text-gray-500">
						{t("createOpportunity.checkInMethodNoneHint")}
					</p>
				)}
				{checkInMethod === "None" && !isScheduledSlots && (
					<p className="mt-2 text-xs text-amber-700">
						{t("createOpportunity.checkInMethodNoneIndividualWarning")}
					</p>
				)}
			</ChoiceGroup>

			{checkInMethod === "PINCode" && (
				<div>
					<div className="flex items-start gap-2">
						<FloatingField
							id="create-check-in-pin"
							label={t("createOpportunity.fieldCheckInPin")}
							registration={register("checkInPin", {
								onChange: (e) => {
									const sanitized = e.target.value
										.replace(/\D/g, "")
										.slice(0, 6);
									if (sanitized !== e.target.value) setCheckInPin(sanitized);
								},
							})}
							error={checkInPinError}
							maxLength={6}
							inputMode="numeric"
							pattern="[0-9]*"
							displayValue={checkInPin}
							wrapperClassName="flex-1"
						/>
						<button
							type="button"
							onClick={() => setCheckInPin(generateRandomPin())}
							className="mt-0.5 shrink-0 rounded-xl border border-gray-500 px-4 py-3.5 text-sm font-medium text-gray-700 transition hover:bg-gray-50"
						>
							{t("createOpportunity.generateRandomPin")}
						</button>
					</div>
					{!checkInPinError && (
						<p className="mt-1 text-xs text-gray-500">
							{t("createOpportunity.checkInPinHint")}
						</p>
					)}
				</div>
			)}
		</div>
	);
}

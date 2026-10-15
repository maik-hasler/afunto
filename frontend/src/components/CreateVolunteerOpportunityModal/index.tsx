import { useEffect, useMemo, useRef, useState } from "react";
import type { ChangeEvent } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useTranslation } from "react-i18next";
import type {
	AddressDto,
	ApiClient,
	TimeSlotDetail,
	VolunteerOpportunityDetails,
} from "../../client";
import { useApiClient } from "../../hooks/useApiClient";
import { dispatchToast } from "../../lib/toastBus";
import { getApiErrorMessage, isApiErrorCode } from "../../lib/apiError";
import { validateImageUpload } from "../../lib/imageUpload";
import {
	CANONICAL_TIME_ZONE,
	toZonedDatetimeLocalValue,
	zonedDatetimeLocalToUtc,
} from "../../lib/timezone";
import ConfirmDialog from "../ConfirmDialog";
import Modal from "../Modal";
import Button from "../Button";
import ErrorBanner from "../ErrorBanner";
import ImageCropModal from "../ImageCropModal";
import { RequiredFieldsLegend } from "../RequiredMark";
import { Stepper } from "./shared";
import BasicsStep from "./BasicsStep";
import LocationStep from "./LocationStep";
import FormatStep from "./FormatStep";
import DetailsStep from "./DetailsStep";
import type { SeriesEditScope } from "./DetailsStep";
import DeleteSeriesSlotDialog from "./DeleteSeriesSlotDialog";
import { CloseIcon } from "../icons";
import {
	buildOpportunityFormSchema,
	errorStepsFromFieldErrors,
	STEP_FIELDS,
	TOTAL_STEPS,
} from "./schema";
import type { OpportunityFormValues } from "./schema";
import {
	byStartDateTime,
	capacityToInput,
	hasUnaddedSlotInput,
	isCompleteDateTime,
	MAX_PARTICIPANTS_LIMIT,
	resolveCapacity,
} from "./timeSlots";
import type { CapacityInput, NewSlotField } from "./timeSlots";

const BLOCKED_JUMP_MESSAGE_ID = "create-opportunity-step-blocked";

// Steps `origin` by whole weeks/months in Berlin wall-clock terms (not the
// browser's own timezone), so an organizer outside Europe/Berlin previewing
// a recurring series before it's saved sees the same occurrences the backend
// would authoritatively generate (CreateTimeSlotCommandHandler.Advance, #2203).
function advanceDate(
	origin: Date,
	frequency: string | undefined,
	steps: number,
): Date {
	if (!frequency || steps === 0) return new Date(origin);

	const local = toZonedDatetimeLocalValue(origin, CANONICAL_TIME_ZONE);
	const [datePart, timePart] = local.split("T");
	const [year, month, day] = datePart.split("-").map(Number);

	const steppedDate = new Date(Date.UTC(year, month - 1, day));
	if (frequency === "Weekly") {
		steppedDate.setUTCDate(steppedDate.getUTCDate() + 7 * steps);
	} else if (frequency === "Monthly") {
		steppedDate.setUTCMonth(steppedDate.getUTCMonth() + steps);
	}

	const steppedDatePart = steppedDate.toISOString().slice(0, 10);
	return zonedDatetimeLocalToUtc(
		`${steppedDatePart}T${timePart}`,
		CANONICAL_TIME_ZONE,
	);
}

function resolveCheckInPin(
	values: Pick<OpportunityFormValues, "checkInMethod" | "checkInPin">,
): string | undefined {
	return values.checkInMethod === "PINCode"
		? values.checkInPin || undefined
		: undefined;
}

async function uploadBanner(
	api: ApiClient,
	opportunityId: string,
	bannerFile: File,
	onError: () => void,
): Promise<void> {
	try {
		await api.uploadOpportunityBanner({
			path: { opportunityId },
			body: { file: bannerFile },
		});
	} catch {
		onError();
	}
}

interface Props {
	organizationId: string;
	onClose: () => void;

	onSuccess: (createdDraftId?: string) => void;

	initialOpportunity?: VolunteerOpportunityDetails;
}

interface PendingTimeSlot {
	id: string;

	batchId: string;
	batchFrequency: string | undefined;
	batchCount: number;
	startDateTime: string;
	endDateTime: string;
	maxParticipants: number | null;
}

interface EditingSlot {
	id: string;
	startDateTime: string;
	endDateTime: string;
	maxParticipants: CapacityInput;
	scope: SeriesEditScope;
}

/**
 * An `EditingSlot` whose capacity box has been resolved to a value the API
 * takes, so nothing past this point has to second-guess what was typed.
 */
type ValidatedSlotEdit = Omit<EditingSlot, "maxParticipants"> & {
	maxParticipants: number | null;
};

function toDatetimeLocalValue(value: Date | string): string {
	const date = value instanceof Date ? value : new Date(value);
	return toZonedDatetimeLocalValue(date, CANONICAL_TIME_ZONE);
}

function toDateInputValue(value: Date | string): string {
	const date = value instanceof Date ? value : new Date(value);
	return new Date(date.getTime() - date.getTimezoneOffset() * 60000)
		.toISOString()
		.slice(0, 10);
}

function endOfDayFromDateInput(value: string): Date {
	const [year, month, day] = value.split("-").map(Number);
	return new Date(year, month - 1, day, 23, 59, 59, 999);
}

// The four fields the backend blames when it cannot resolve an address. They
// live on step 2, so a rejection has to send the organizer back there rather
// than leave a banner on whichever step they submitted from (#2320).
const ADDRESS_FIELDS = [
	"street",
	"houseNumber",
	"zipCode",
	"city",
] as const satisfies readonly (keyof OpportunityFormValues)[];

const DEFAULT_VALUES: OpportunityFormValues = {
	titleDe: "",
	titleEn: "",
	descriptionDe: "",
	descriptionEn: "",
	isRemote: false,
	street: "",
	houseNumber: "",
	zipCode: "",
	city: "",
	occurrence: "OneTime",
	participationType: "ScheduledSlots",
	checkInMethod: "None",
	checkInPin: "",
	category: undefined,
	tags: [],
	validUntil: "",
};

function formFromOpportunity(
	opp: VolunteerOpportunityDetails,
): OpportunityFormValues {
	return {
		titleDe: opp.titleDe ?? "",
		titleEn: opp.titleEn ?? "",
		descriptionDe: opp.descriptionDe ?? "",
		descriptionEn: opp.descriptionEn ?? "",
		isRemote: opp.isRemote,
		street: opp.street ?? "",
		houseNumber: opp.houseNumber ?? "",
		zipCode: opp.zipCode ?? "",
		city: opp.city ?? "",
		occurrence: opp.occurrence as OpportunityFormValues["occurrence"],
		participationType:
			opp.participationType as OpportunityFormValues["participationType"],
		checkInMethod: opp.checkInMethod as OpportunityFormValues["checkInMethod"],
		checkInPin: "",
		category: opp.category ?? undefined,
		tags: opp.tags ?? [],
		validUntil: opp.validUntil ? toDateInputValue(opp.validUntil) : "",
	};
}

export default function CreateVolunteerOpportunityModal({
	organizationId,
	onClose,
	onSuccess,
	initialOpportunity,
}: Props) {
	const api = useApiClient();
	const { t, i18n } = useTranslation();
	const isEditMode = initialOpportunity !== undefined;

	const capacityErrorMessage = t("timeSlots.capacityInvalid", {
		max: MAX_PARTICIPANTS_LIMIT.toLocaleString(i18n.language),
	});

	const canSaveDraft =
		!isEditMode ||
		initialOpportunity.status === "Draft" ||
		initialOpportunity.status === "Unpublished";

	const schema = useMemo(() => buildOpportunityFormSchema(t), [t]);
	const {
		register,
		control,
		watch,
		setValue,
		getValues,
		trigger,
		clearErrors,
		setError: setFieldError,
		setFocus,
		getFieldState,
		formState: { errors, isDirty },
	} = useForm<OpportunityFormValues>({
		resolver: zodResolver(schema),
		mode: "onBlur",
		defaultValues: initialOpportunity
			? formFromOpportunity(initialOpportunity)
			: DEFAULT_VALUES,
	});

	const [step, setStep] = useState(1);
	const [submitting, setSubmitting] = useState<"draft" | "publish" | null>(
		null,
	);
	const [error, setError] = useState<string | null>(null);

	const [errorToken, setErrorToken] = useState(0);

	const [blockedJump, setBlockedJump] = useState<{
		target: number;
		blocking: number;
		attempt: number;
	} | null>(null);
	const [orgAddress, setOrgAddress] = useState<AddressDto | null>(null);
	const [showDiscardConfirm, setShowDiscardConfirm] = useState(false);

	const [bannerFile, setBannerFile] = useState<File | null>(null);
	const [bannerPreview, setBannerPreview] = useState<string | null>(
		initialOpportunity?.bannerImageUrl ?? null,
	);
	const [bannerError, setBannerError] = useState<string | null>(null);
	const [bannerRemoved, setBannerRemoved] = useState(false);
	const [croppingBannerFile, setCroppingBannerFile] = useState<File | null>(
		null,
	);

	const [pendingSlots, setPendingSlots] = useState<PendingTimeSlot[]>([]);
	const [existingSlots, setExistingSlots] = useState<TimeSlotDetail[]>(
		initialOpportunity?.timeSlots ?? [],
	);
	const [newSlot, setNewSlot] = useState<{
		startDateTime: string;
		endDateTime: string;
		maxParticipants: CapacityInput;
	}>({
		startDateTime: "",
		endDateTime: "",
		maxParticipants: "1",
	});
	const [slotError, setSlotError] = useState<string | null>(null);
	// Which of the add form's inputs the current message is about, so it points
	// at the ones that produced it rather than floating under the row (#2320).
	// A past start is the start box's alone, a bad capacity the spot box's;
	// only "ends before it starts" implicates the pair (#2325). Scoped to that
	// form on purpose: an open edit row has its own inputs, and the shared
	// message renders down here either way.
	const [invalidNewSlotFields, setInvalidNewSlotFields] = useState<
		ReadonlySet<NewSlotField>
	>(new Set());

	function clearInvalidNewSlotFields() {
		setInvalidNewSlotFields(new Set());
	}
	const [removingSlotId, setRemovingSlotId] = useState<string | null>(null);
	const [pendingSlotDelete, setPendingSlotDelete] = useState<{
		id: string;
		bookedCount: number;
	} | null>(null);
	const [slotDeleteError, setSlotDeleteError] = useState<string | null>(null);

	// In edit mode every slot action hits the server the moment it is clicked,
	// which is not what a dialog with an unpressed "Save" implies. The step now
	// says so up front and confirms the destructive one; this tracks whether it
	// has happened, so closing can repeat the point (#2315).
	const [slotChangesApplied, setSlotChangesApplied] = useState(false);
	const [addingSlot, setAddingSlot] = useState(false);
	const [editingSlot, setEditingSlot] = useState<EditingSlot | null>(null);
	const [updatingSlotId, setUpdatingSlotId] = useState<string | null>(null);
	const [pendingSlotEdit, setPendingSlotEdit] = useState<
		(ValidatedSlotEdit & { bookedCount: number }) | null
	>(null);
	const [pendingSeriesDelete, setPendingSeriesDelete] = useState<{
		id: string;
		bookedCount: number;
	} | null>(null);
	const [deletingSeriesSlot, setDeletingSeriesSlot] = useState(false);
	const [seriesDeleteError, setSeriesDeleteError] = useState<string | null>(
		null,
	);
	const [recurrenceFrequency, setRecurrenceFrequency] = useState("Weekly");
	const [recurrenceCount, setRecurrenceCount] = useState(1);

	const createdOpportunityIdRef = useRef<string | null>(null);
	const createdSlotIdsRef = useRef<Set<string>>(new Set());
	const createdBatchIdsRef = useRef<Set<string>>(new Set());

	const bodyRef = useRef<HTMLDivElement>(null);

	const focusFieldsRef = useRef<(keyof OpportunityFormValues)[] | null>(null);
	const [focusAttempt, setFocusAttempt] = useState(0);

	const occurrence = watch("occurrence");
	const participationType = watch("participationType");
	const isRemote = watch("isRemote");
	const titleDe = watch("titleDe");
	const isScheduledSlots = participationType === "ScheduledSlots";

	const draftTitleMissing = !titleDe.trim();

	useEffect(() => {
		if (isEditMode) return;
		let cancelled = false;
		api
			.getOrganizationDetails({ path: { organizationId } })
			.then((org) => {
				if (cancelled || !org.address) return;
				setOrgAddress(org.address);
				const current = getValues();
				if (
					!current.street &&
					!current.houseNumber &&
					!current.zipCode &&
					!current.city
				) {
					setValue("street", org.address.street ?? "");
					setValue("houseNumber", org.address.houseNumber ?? "");
					setValue("zipCode", org.address.zipCode ?? "");
					setValue("city", org.address.city ?? "");
				}
			})
			.catch(() => {});
		return () => {
			cancelled = true;
		};
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [organizationId, isEditMode]);

	useEffect(() => {
		if (!initialOpportunity || initialOpportunity.checkInMethod !== "PINCode")
			return;
		let cancelled = false;
		api
			.getOpportunityCheckInPin({
				path: { opportunityId: initialOpportunity.id },
			})
			.then((pin) => {
				if (cancelled || !pin) return;
				setValue("checkInPin", pin);
			})
			.catch(() => {});
		return () => {
			cancelled = true;
		};
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [initialOpportunity]);

	useEffect(() => {
		return () => {
			if (bannerPreview) URL.revokeObjectURL(bannerPreview);
		};
	}, [bannerPreview]);

	useEffect(() => {
		setBlockedJump(null);
	}, [step]);

	useEffect(() => {
		if (focusAttempt === 0 || !focusFieldsRef.current) return;
		for (const field of focusFieldsRef.current) {
			if (getFieldState(field).invalid) {
				setFocus(field);
				break;
			}
		}
		focusFieldsRef.current = null;
		// eslint-disable-next-line react-hooks/exhaustive-deps
	}, [focusAttempt]);

	function requestClose() {
		const hasUnsavedChanges =
			isDirty ||
			pendingSlots.length > 0 ||
			hasUnaddedSlotInput(newSlot) ||
			bannerFile !== null ||
			bannerRemoved;
		if (hasUnsavedChanges) setShowDiscardConfirm(true);
		else onClose();
	}

	// react-hook-form only re-validates an already-errored field on every
	// keystroke once the form has been through a handleSubmit() call
	// (isSubmitted flips reValidateMode's default of "onChange" into effect) -
	// this wizard never calls handleSubmit, each step and the final submit
	// call trigger() directly instead, so a field marked invalid by "Next"
	// kept showing its error, aria-invalid included, until "Next" was clicked
	// again even after the user had already fixed it (#1928). Re-running that
	// one field's own validation on change once it has an error closes the
	// gap without waiting for another step advance.
	const registerWithRevalidate: typeof register = (name, options) =>
		register(name, {
			...options,
			onChange: async (event) => {
				await options?.onChange?.(event);
				if (errors[name as keyof OpportunityFormValues]) await trigger(name);
			},
		});

	function requestFocusFirstInvalid(fields: (keyof OpportunityFormValues)[]) {
		focusFieldsRef.current = fields;
		setFocusAttempt((n) => n + 1);
	}

	function applyOrgAddress() {
		if (!orgAddress) return;
		setValue("street", orgAddress.street);
		setValue("houseNumber", orgAddress.houseNumber);
		setValue("zipCode", orgAddress.zipCode);
		setValue("city", orgAddress.city);
		clearErrors(["street", "houseNumber", "zipCode", "city"]);
	}

	function handleRemoteToggle(checked: boolean) {
		if (checked) clearErrors(["street", "houseNumber", "zipCode", "city"]);
	}

	function setCheckInPin(pin: string) {
		setValue("checkInPin", pin, { shouldDirty: true });
	}

	async function handleNext() {
		const fields = STEP_FIELDS[step];
		const valid = fields.length === 0 || (await trigger(fields));
		if (!valid) {
			requestFocusFirstInvalid(fields);
			return;
		}
		setStep((s) => Math.min(TOTAL_STEPS, s + 1));
	}

	async function handleStepClick(n: number) {
		if (n <= step) {
			setStep(n);
			return;
		}

		let blocking: number | null = null;
		for (let s = step; s < n; s++) {
			const fields = STEP_FIELDS[s];
			if (fields.length === 0) continue;
			if (!(await trigger(fields)) && blocking === null) blocking = s;
		}
		if (blocking !== null) {
			const blockingStep = blocking;
			setBlockedJump((prev) => ({
				target: n,
				blocking: blockingStep,
				attempt: (prev?.attempt ?? 0) + 1,
			}));

			if (blockingStep === step) requestFocusFirstInvalid(STEP_FIELDS[step]);
			return;
		}
		setStep(n);
	}

	function handleBannerChange(e: ChangeEvent<HTMLInputElement>) {
		const file = e.target.files?.[0];
		e.target.value = "";
		if (!file) return;
		const rejection = validateImageUpload(file, t, i18n.language);
		if (rejection) {
			setBannerError(rejection);
			return;
		}
		setBannerError(null);
		setCroppingBannerFile(file);
	}

	function handleBannerCropped(croppedFile: File) {
		setCroppingBannerFile(null);
		setBannerFile(croppedFile);
		setBannerPreview(URL.createObjectURL(croppedFile));
	}

	function removeBanner() {
		if (isEditMode && bannerFile === null) {
			setBannerRemoved(true);
		}
		setBannerFile(null);
		setBannerPreview(null);
		setBannerError(null);
	}

	async function handleAddSlot() {
		if (
			!isCompleteDateTime(newSlot.startDateTime) ||
			!isCompleteDateTime(newSlot.endDateTime)
		)
			return;
		setSlotError(null);
		clearInvalidNewSlotFields();
		const start = zonedDatetimeLocalToUtc(
			newSlot.startDateTime,
			CANONICAL_TIME_ZONE,
		);
		const end = zonedDatetimeLocalToUtc(
			newSlot.endDateTime,
			CANONICAL_TIME_ZONE,
		);
		// The API rejects each of these, and while creating it does so only
		// after the opportunity itself has been POSTed - so the slot never
		// leaves the browser until it is one the API would take (#2325). The
		// app already ships the precise message the server would send for each
		// of the first two; a generic "could not add" said nothing (#2320).
		if (start <= new Date()) {
			setSlotError(t("apiError.TimeSlot.StartMustBeFuture"));
			setInvalidNewSlotFields(new Set(["start"]));
			return;
		}
		if (end <= start) {
			setSlotError(t("apiError.TimeSlot.EndMustBeAfterStart"));
			setInvalidNewSlotFields(new Set(["start", "end"]));
			return;
		}
		const maxParticipants = resolveCapacity(newSlot.maxParticipants);
		if (maxParticipants === undefined) {
			setSlotError(capacityErrorMessage);
			setInvalidNewSlotFields(new Set(["max"]));
			return;
		}

		const isRecurring = occurrence === "Recurring";
		if (isEditMode && initialOpportunity) {
			setAddingSlot(true);
			try {
				const responses = await api.createTimeSlot({
					path: { opportunityId: initialOpportunity.id },
					body: {
						startDateTime: start,
						endDateTime: end,
						maxParticipants: maxParticipants ?? null,
						recurrenceFrequency: isRecurring ? recurrenceFrequency : null,
						recurrenceCount: isRecurring ? recurrenceCount : 1,
					},
				});
				setExistingSlots((prev) => [
					...prev,
					...responses.map((r) => ({
						id: r.id,
						startDateTime: r.startDateTime,
						endDateTime: r.endDateTime,
						maxParticipants: r.maxParticipants,
						bookedCount: 0,
						seriesId: r.seriesId,
						recurrenceFrequency: r.recurrenceFrequency,
						recurrenceCount: r.recurrenceCount,
					})),
				]);

				setSlotChangesApplied(true);
				setNewSlot({
					startDateTime: "",
					endDateTime: "",
					maxParticipants: "1",
				});
				setError(null);
			} catch {
				setSlotError(t("timeSlots.addError"));
			} finally {
				setAddingSlot(false);
			}
		} else {
			const duration = end.getTime() - start.getTime();
			const count = isRecurring
				? Math.max(1, Math.min(52, recurrenceCount))
				: 1;
			const freq = isRecurring ? recurrenceFrequency : undefined;
			const batchId = crypto.randomUUID();
			const newSlots: PendingTimeSlot[] = Array.from(
				{ length: count },
				(_, i) => {
					const slotStart = advanceDate(start, freq, i);
					const slotEnd = new Date(slotStart.getTime() + duration);
					return {
						id: crypto.randomUUID(),
						batchId,
						batchFrequency: freq,
						batchCount: count,
						startDateTime: slotStart.toISOString(),
						endDateTime: slotEnd.toISOString(),
						maxParticipants,
					};
				},
			);
			setPendingSlots((prev) => [...prev, ...newSlots]);
			setNewSlot({ startDateTime: "", endDateTime: "", maxParticipants: "1" });
			setError(null);
		}
	}

	function handleDiscardNewSlot() {
		setNewSlot({ startDateTime: "", endDateTime: "", maxParticipants: "1" });
		setSlotError(null);
		clearInvalidNewSlotFields();
		setError(null);
	}

	function handleRemovePendingSlot(id: string) {
		setPendingSlots((prev) => prev.filter((s) => s.id !== id));
	}

	function handleRequestRemoveExistingSlot(
		timeSlotId: string,
		bookedCount: number,
	) {
		setSlotError(null);
		setSlotDeleteError(null);
		setPendingSlotDelete({ id: timeSlotId, bookedCount });
	}

	async function performExistingSlotDelete(timeSlotId: string) {
		if (!initialOpportunity) return;
		setRemovingSlotId(timeSlotId);
		setSlotError(null);
		clearInvalidNewSlotFields();
		setSlotDeleteError(null);
		try {
			await api.deleteTimeSlot({
				path: { opportunityId: initialOpportunity.id, timeSlotId },
				query: { scope: "Only" },
			});
			setExistingSlots((prev) => prev.filter((s) => s.id !== timeSlotId));
			setSlotChangesApplied(true);
			setPendingSlotDelete(null);
		} catch {
			setSlotDeleteError(t("timeSlots.removeError"));
		} finally {
			setRemovingSlotId(null);
		}
	}

	function handleRequestRemoveSeriesSlot(
		timeSlotId: string,
		bookedCount: number,
	) {
		setSeriesDeleteError(null);
		setPendingSeriesDelete({ id: timeSlotId, bookedCount });
	}

	async function performSeriesDelete(scope: SeriesEditScope) {
		if (!initialOpportunity || !pendingSeriesDelete) return;
		setDeletingSeriesSlot(true);
		setSeriesDeleteError(null);
		try {
			const result = await api.deleteTimeSlot({
				path: {
					opportunityId: initialOpportunity.id,
					timeSlotId: pendingSeriesDelete.id,
				},
				query: { scope },
			});
			setExistingSlots((prev) =>
				prev.filter((s) => !result.deletedTimeSlotIds.includes(s.id)),
			);
			setSlotChangesApplied(true);
			setPendingSeriesDelete(null);
		} catch {
			setSeriesDeleteError(t("timeSlots.removeError"));
		} finally {
			setDeletingSeriesSlot(false);
		}
	}

	function handleStartEditSlot(slot: {
		id: string;
		startDateTime: string;
		endDateTime: string;
		maxParticipants: number | null;
	}) {
		setSlotError(null);
		clearInvalidNewSlotFields();
		setEditingSlot({
			id: slot.id,
			startDateTime: toDatetimeLocalValue(slot.startDateTime),
			endDateTime: toDatetimeLocalValue(slot.endDateTime),
			maxParticipants: capacityToInput(slot.maxParticipants),
			scope: "Only",
		});
	}

	async function applySlotEdit(edit: ValidatedSlotEdit) {
		if (!initialOpportunity) return;
		setUpdatingSlotId(edit.id);
		setSlotError(null);
		clearInvalidNewSlotFields();
		try {
			const result = await api.updateTimeSlot({
				path: { opportunityId: initialOpportunity.id, timeSlotId: edit.id },
				body: {
					startDateTime:
						edit.scope === "Only"
							? zonedDatetimeLocalToUtc(edit.startDateTime, CANONICAL_TIME_ZONE)
							: null,
					endDateTime:
						edit.scope === "Only"
							? zonedDatetimeLocalToUtc(edit.endDateTime, CANONICAL_TIME_ZONE)
							: null,
					maxParticipants: edit.maxParticipants ?? null,
					scope: edit.scope,
				},
			});
			if (edit.scope === "Only") {
				setExistingSlots((prev) =>
					prev.map((s) =>
						s.id === edit.id
							? {
									...s,
									startDateTime: zonedDatetimeLocalToUtc(
										edit.startDateTime,
										CANONICAL_TIME_ZONE,
									),
									endDateTime: zonedDatetimeLocalToUtc(
										edit.endDateTime,
										CANONICAL_TIME_ZONE,
									),
									maxParticipants: edit.maxParticipants,
								}
							: s,
					),
				);
			} else {
				const fresh = await api.getVolunteerOpportunityDetails({
					path: { opportunityId: initialOpportunity.id },
				});
				setExistingSlots(fresh.timeSlots);
				if (result.skippedTimeSlotIds.length > 0) {
					setSlotError(
						t("timeSlots.editPartialSkip", {
							count: result.skippedTimeSlotIds.length,
						}),
					);
				}
			}
			setSlotChangesApplied(true);
			setEditingSlot(null);
		} catch {
			setSlotError(t("timeSlots.editError"));
		} finally {
			setUpdatingSlotId(null);
			setPendingSlotEdit(null);
		}
	}

	function handleRequestSaveEditSlot(bookedCount: number) {
		if (!editingSlot) return;
		const maxParticipants = resolveCapacity(editingSlot.maxParticipants);
		if (maxParticipants === undefined) {
			setSlotError(capacityErrorMessage);
			return;
		}
		const edit: ValidatedSlotEdit = { ...editingSlot, maxParticipants };
		if (edit.scope !== "Only") {
			setSlotError(null);
			clearInvalidNewSlotFields();
			void applySlotEdit(edit);
			return;
		}
		if (
			!isCompleteDateTime(edit.startDateTime) ||
			!isCompleteDateTime(edit.endDateTime)
		)
			return;
		const start = zonedDatetimeLocalToUtc(
			edit.startDateTime,
			CANONICAL_TIME_ZONE,
		);
		const end = zonedDatetimeLocalToUtc(edit.endDateTime, CANONICAL_TIME_ZONE);
		if (end <= start) {
			setSlotError(t("apiError.TimeSlot.EndMustBeAfterStart"));
			return;
		}
		setSlotError(null);
		clearInvalidNewSlotFields();
		if (bookedCount > 0) {
			setPendingSlotEdit({ ...edit, bookedCount });
		} else {
			void applySlotEdit(edit);
		}
	}

	const submit = async (asDraft: boolean) => {
		if (!asDraft) {
			for (let s = 1; s <= TOTAL_STEPS; s++) {
				const fields = STEP_FIELDS[s];
				if (fields.length === 0) continue;
				const stepValid = await trigger(fields);
				if (!stepValid) {
					setStep(s);
					requestFocusFirstInvalid(fields);
					return;
				}
			}
		}

		const values = getValues();
		if (values.participationType === "ScheduledSlots") {
			// A slot typed into the add form but never added is not on the
			// list, so saving past it would drop it silently (#2388).
			if (hasUnaddedSlotInput(newSlot)) {
				setError(t("timeSlots.unaddedBeforeSave"));
				setErrorToken((tk) => tk + 1);
				setStep(4);
				return;
			}
			if (!asDraft && pendingSlots.length + existingSlots.length === 0) {
				setError(t("timeSlots.requiredForPublish"));
				setErrorToken((tk) => tk + 1);
				setStep(4);
				return;
			}
			// A slot that was in the future when it was added can have slipped
			// into the past while the wizard was open. The opportunity is
			// created before its slots are POSTed, so learning that from the
			// API is what left an orphan draft behind (#2325).
			const now = Date.now();
			if (pendingSlots.some((s) => Date.parse(s.startDateTime) <= now)) {
				setError(t("timeSlots.pastSlotsBeforePublish"));
				setErrorToken((tk) => tk + 1);
				setStep(4);
				return;
			}
		}
		if (
			!asDraft &&
			values.participationType === "IndividualContact" &&
			!values.validUntil
		) {
			setError(t("createOpportunity.validUntilRequiredForPublish"));
			setErrorToken((tk) => tk + 1);
			setStep(4);
			return;
		}
		setSubmitting(asDraft ? "draft" : "publish");
		setError(null);

		let createdDraftId: string | undefined;
		try {
			if (isEditMode && initialOpportunity) {
				await api.updateVolunteerOpportunity({
					path: { opportunityId: initialOpportunity.id },
					body: {
						titleDe: values.titleDe,
						titleEn: values.titleEn || null,
						descriptionDe: values.descriptionDe,
						descriptionEn: values.descriptionEn || null,
						isRemote: values.isRemote,
						street: values.isRemote ? null : values.street,
						houseNumber: values.isRemote ? null : values.houseNumber,
						zipCode: values.isRemote ? null : values.zipCode,
						city: values.isRemote ? null : values.city,
						occurrence: values.occurrence,
						participationType: values.participationType,
						checkInMethod: values.checkInMethod,
						checkInPin: resolveCheckInPin(values) ?? null,
						category: values.category || null,
						tags: values.tags,
						validUntil: values.validUntil
							? endOfDayFromDateInput(values.validUntil)
							: null,
					},
				});
				if (bannerFile) {
					await uploadBanner(api, initialOpportunity.id, bannerFile, () =>
						dispatchToast("error", t("editOpportunity.bannerUploadFailed")),
					);
				} else if (bannerRemoved) {
					try {
						await api.deleteOpportunityBanner({
							path: { opportunityId: initialOpportunity.id },
						});
					} catch {
						dispatchToast("error", t("editOpportunity.bannerRemoveFailed"));
					}
				}
			} else {
				const publishScheduledSlotsAfterCreate = !asDraft && isScheduledSlots;

				let opportunityId = createdOpportunityIdRef.current;
				if (opportunityId) {
					await api.updateVolunteerOpportunity({
						path: { opportunityId },
						body: {
							titleDe: values.titleDe,
							titleEn: values.titleEn || null,
							descriptionDe: values.descriptionDe,
							descriptionEn: values.descriptionEn || null,
							isRemote: values.isRemote,
							street: values.isRemote ? null : values.street,
							houseNumber: values.isRemote ? null : values.houseNumber,
							zipCode: values.isRemote ? null : values.zipCode,
							city: values.isRemote ? null : values.city,
							occurrence: values.occurrence,
							participationType: values.participationType,
							checkInMethod: values.checkInMethod,
							checkInPin: resolveCheckInPin(values) ?? null,
							category: values.category || null,
							tags: values.tags,
							validUntil: values.validUntil
								? endOfDayFromDateInput(values.validUntil)
								: null,
						},
					});
				} else {
					const opportunity = await api.createVolunteerOpportunity({
						body: {
							titleDe: values.titleDe,
							titleEn: values.titleEn || null,
							descriptionDe: values.descriptionDe,
							descriptionEn: values.descriptionEn || null,
							organizationId,
							isRemote: values.isRemote,
							street: values.isRemote ? null : values.street,
							houseNumber: values.isRemote ? null : values.houseNumber,
							zipCode: values.isRemote ? null : values.zipCode,
							city: values.isRemote ? null : values.city,
							occurrence: values.occurrence,
							participationType: values.participationType,
							checkInMethod: values.checkInMethod,
							checkInPin: resolveCheckInPin(values) ?? null,
							category: values.category ?? null,
							tags: values.tags,
							validUntil: values.validUntil
								? endOfDayFromDateInput(values.validUntil)
								: null,
							isDraft: asDraft || publishScheduledSlotsAfterCreate,
						},
					});
					opportunityId = opportunity.id;
					createdOpportunityIdRef.current = opportunityId;
				}
				if (bannerFile) {
					await uploadBanner(api, opportunityId, bannerFile, () =>
						dispatchToast("error", t("createOpportunity.bannerUploadFailed")),
					);
				}

				const batches = new Map<string, PendingTimeSlot[]>();
				for (const slot of pendingSlots) {
					const list = batches.get(slot.batchId) ?? [];
					list.push(slot);
					batches.set(slot.batchId, list);
				}
				for (const [batchId, batchSlots] of batches) {
					const sorted = [...batchSlots].sort((a, b) =>
						a.startDateTime.localeCompare(b.startDateTime),
					);
					const first = sorted[0];
					const isIntactRecurringBatch =
						first.batchCount > 1 && sorted.length === first.batchCount;
					if (isIntactRecurringBatch) {
						if (createdBatchIdsRef.current.has(batchId)) continue;
						await api.createTimeSlot({
							path: { opportunityId },
							body: {
								startDateTime: new Date(first.startDateTime),
								endDateTime: new Date(first.endDateTime),
								maxParticipants: first.maxParticipants ?? null,
								recurrenceFrequency: first.batchFrequency ?? null,
								recurrenceCount: first.batchCount,
							},
						});
						createdBatchIdsRef.current.add(batchId);
					} else {
						for (const slot of sorted) {
							if (createdSlotIdsRef.current.has(slot.id)) continue;
							await api.createTimeSlot({
								path: { opportunityId },
								body: {
									startDateTime: new Date(slot.startDateTime),
									endDateTime: new Date(slot.endDateTime),
									maxParticipants: slot.maxParticipants ?? null,
									recurrenceFrequency: null,
									recurrenceCount: 1,
								},
							});
							createdSlotIdsRef.current.add(slot.id);
						}
					}
				}
				if (publishScheduledSlotsAfterCreate) {
					try {
						await api.publishVolunteerOpportunity({ path: { opportunityId } });
					} catch (publishErr) {
						if (
							!isApiErrorCode(
								publishErr,
								"VolunteerOpportunity.AlreadyPublished",
							)
						) {
							throw publishErr;
						}
					}
				}
				if (asDraft) {
					createdDraftId = opportunityId;
					dispatchToast("success", t("createOpportunity.draftSavedToast"));
				}
			}
			onSuccess(createdDraftId);
			onClose();
		} catch (err: unknown) {
			if (isApiErrorCode(err, "Address.NotGeocodable")) {
				for (const field of ADDRESS_FIELDS)
					setFieldError(field, {
						type: "server",
						message: t("createOpportunity.addressUnresolvedField"),
					});
				setStep(2);
				requestFocusFirstInvalid([...ADDRESS_FIELDS]);
			}
			setError(
				getApiErrorMessage(
					err,
					isEditMode
						? t("editOpportunity.unknownError")
						: t("createOpportunity.unknownError"),
				),
			);
			setErrorToken((tk) => tk + 1);
		} finally {
			setSubmitting(null);
		}
	};

	const erroredFields = new Set(
		Object.keys(errors) as (keyof OpportunityFormValues)[],
	);
	const errorSteps = errorStepsFromFieldErrors(erroredFields);

	const stepTitles = [
		t("createOpportunity.step1Title"),
		t("createOpportunity.step2Title"),
		t("createOpportunity.step3Title"),
		t("createOpportunity.step4Title"),
	];

	const blockedJumpMessage =
		blockedJump && errorSteps.has(blockedJump.blocking)
			? t("createOpportunity.stepBlocked", {
					target: blockedJump.target,
					blocking: blockedJump.blocking,
					blockingTitle: stepTitles[blockedJump.blocking - 1],
				})
			: null;

	const stepSubtitles = [
		t("createOpportunity.step1Subtitle"),
		t("createOpportunity.step2Subtitle"),
		t("createOpportunity.step3Subtitle"),
		isScheduledSlots
			? t("createOpportunity.step4SubtitleWaitlist")
			: t("createOpportunity.step4SubtitleIndividualContact"),
	];

	const seriesPositionById = new Map<string, number>();
	if (isEditMode) {
		const bySeriesId = new Map<string, typeof existingSlots>();
		for (const s of existingSlots) {
			if (!s.seriesId) continue;
			const list = bySeriesId.get(s.seriesId) ?? [];
			list.push(s);
			bySeriesId.set(s.seriesId, list);
		}
		for (const slots of bySeriesId.values()) {
			slots
				.slice()
				.sort(
					(a, b) =>
						new Date(a.startDateTime).getTime() -
						new Date(b.startDateTime).getTime(),
				)
				.forEach((s, i) => seriesPositionById.set(s.id, i + 1));
		}
	}

	const allTimeSlots = (
		isEditMode
			? existingSlots.map((s) => ({
					id: s.id,
					startDateTime:
						s.startDateTime instanceof Date
							? s.startDateTime.toISOString()
							: String(s.startDateTime),
					endDateTime:
						s.endDateTime instanceof Date
							? s.endDateTime.toISOString()
							: String(s.endDateTime),
					maxParticipants:
						s.maxParticipants === null ? null : Number(s.maxParticipants),
					bookedCount: Number(s.bookedCount),
					persisted: true as const,
					seriesId: s.seriesId ?? undefined,
					recurrenceFrequency: s.recurrenceFrequency ?? undefined,
					recurrenceCount:
						s.recurrenceCount === null ? undefined : Number(s.recurrenceCount),
					seriesPosition: s.seriesId ? seriesPositionById.get(s.id) : undefined,
				}))
			: pendingSlots.map((s) => ({
					...s,
					bookedCount: 0,
					persisted: false as const,
				}))
	).sort(byStartDateTime);

	return (
		<>
			<Modal
				onClose={requestClose}
				labelledBy="create-opportunity-dialog-title"
				maxWidth="max-w-xl"
				className="flex min-w-0 flex-col overflow-hidden rounded-card bg-white shadow-modal"
				suspended={
					showDiscardConfirm ||
					pendingSlotEdit !== null ||
					pendingSeriesDelete !== null ||
					croppingBannerFile !== null
				}
				initialFocusRef={bodyRef}
			>
				<div className="flex items-center justify-between gap-4 border-b border-gray-100 px-6 py-4">
					<h2
						id="create-opportunity-dialog-title"
						className="text-lg font-semibold text-gray-900"
					>
						{isEditMode
							? t("createOpportunity.editTitle")
							: t("createOpportunity.title")}
					</h2>
					<button
						type="button"
						onClick={requestClose}
						aria-label={t("createOpportunity.close")}
						className="shrink-0 rounded-lg p-1.5 text-gray-600 transition-colors hover:bg-gray-100 hover:text-gray-800"
					>
						<CloseIcon className="h-5 w-5" />
					</button>
				</div>

				<div className="border-b border-gray-100 px-6 pt-3 pb-3">
					<Stepper
						current={step}
						errorSteps={errorSteps}
						onStepClick={(n) => void handleStepClick(n)}
						steps={stepTitles}
						stepLabel={(n, label) =>
							`${t("createOpportunity.stepOf", { current: n, total: TOTAL_STEPS })}: ${label}`
						}
						blocked={
							blockedJump && blockedJumpMessage
								? {
										step: blockedJump.target,
										messageId: BLOCKED_JUMP_MESSAGE_ID,
									}
								: undefined
						}
					/>
					{blockedJump && blockedJumpMessage && (
						<ErrorBanner
							key={blockedJump.attempt}
							id={BLOCKED_JUMP_MESSAGE_ID}
							message={blockedJumpMessage}
							className="mt-3"
						/>
					)}
				</div>

				<div aria-live="polite" className="sr-only">
					{t("createOpportunity.stepOf", { current: step, total: TOTAL_STEPS })}
					: {stepTitles[step - 1]}
				</div>

				<div
					ref={bodyRef}
					className="max-h-[min(70vh,640px)] overflow-y-auto px-6 py-5"
				>
					<h3 className="sr-only">{stepTitles[step - 1]}</h3>
					<p className="mb-4 text-sm leading-relaxed text-gray-500">
						{stepSubtitles[step - 1]}
					</p>

					{(step === 1 || (step === 2 && !isRemote)) && (
						<RequiredFieldsLegend className="-mt-2 mb-4" />
					)}

					{step === 1 && (
						<BasicsStep
							register={registerWithRevalidate}
							watch={watch}
							titleDeError={errors.titleDe?.message}
							titleEnError={errors.titleEn?.message}
							descriptionDeError={errors.descriptionDe?.message}
							descriptionEnError={errors.descriptionEn?.message}
							revalidationAttempt={focusAttempt}
							bannerPreview={bannerPreview}
							bannerError={bannerError}
							onBannerChange={handleBannerChange}
							onBannerRemove={removeBanner}
						/>
					)}

					{step === 2 && (
						<LocationStep
							register={registerWithRevalidate}
							watch={watch}
							onRemoteToggle={handleRemoteToggle}
							errors={{
								street: errors.street?.message,
								houseNumber: errors.houseNumber?.message,
								zipCode: errors.zipCode?.message,
								city: errors.city?.message,
							}}
							organizationId={organizationId}
							orgAddress={orgAddress}
							isEditMode={isEditMode}
							onApplyOrgAddress={applyOrgAddress}
						/>
					)}

					{step === 3 && (
						<FormatStep
							register={registerWithRevalidate}
							watch={watch}
							setCheckInPin={setCheckInPin}
							checkInPinError={errors.checkInPin?.message}
						/>
					)}

					{step === 4 && (
						<DetailsStep
							control={control}
							isScheduledSlots={isScheduledSlots}
							occurrence={occurrence}
							allTimeSlots={allTimeSlots}
							removingSlotId={removingSlotId}
							onRemoveExistingSlot={handleRequestRemoveExistingSlot}
							onRequestRemoveSeriesSlot={handleRequestRemoveSeriesSlot}
							onRemovePendingSlot={handleRemovePendingSlot}
							editingSlot={editingSlot}
							onStartEditSlot={handleStartEditSlot}
							onEditingSlotChange={setEditingSlot}
							onCancelEditSlot={() => setEditingSlot(null)}
							onSaveEditSlot={handleRequestSaveEditSlot}
							updatingSlotId={updatingSlotId}
							newSlot={newSlot}
							onNewSlotChange={setNewSlot}
							slotError={slotError}
							invalidNewSlotFields={invalidNewSlotFields}
							slotChangesAreImmediate={isEditMode}
							addingSlot={addingSlot}
							onAddSlot={() => void handleAddSlot()}
							onDiscardNewSlot={handleDiscardNewSlot}
							recurrenceFrequency={recurrenceFrequency}
							onRecurrenceFrequencyChange={setRecurrenceFrequency}
							recurrenceCount={recurrenceCount}
							onRecurrenceCountChange={setRecurrenceCount}
							error={error}
							errorToken={errorToken}
						/>
					)}
				</div>

				<div className="border-t border-gray-100 bg-gray-50">
					{canSaveDraft && draftTitleMissing && (
						<p
							id="save-draft-hint"
							className="px-4 pt-3 text-xs text-gray-500 sm:px-6"
						>
							{t(
								step === 1
									? "createOpportunity.saveDraftRequiresTitleHere"
									: "createOpportunity.saveDraftRequiresTitle",
							)}
						</p>
					)}
					<div className="flex flex-col-reverse gap-3 px-4 py-4 sm:flex-row sm:items-center sm:justify-between sm:px-6">
						<Button
							type="button"
							variant="secondary"
							data-testid="modal-cancel"
							onClick={() =>
								step > 1 ? setStep((s) => s - 1) : requestClose()
							}
						>
							{step === 1
								? t("createOpportunity.cancel")
								: t("createOpportunity.back")}
						</Button>

						<div className="flex flex-col-reverse gap-2 sm:flex-row sm:items-center">
							{canSaveDraft && (
								<Button
									type="button"
									variant="outline"
									data-testid="modal-save-draft"
									disabled={submitting !== null || draftTitleMissing}
									aria-describedby={
										draftTitleMissing ? "save-draft-hint" : undefined
									}
									title={
										draftTitleMissing
											? t(
													step === 1
														? "createOpportunity.saveDraftRequiresTitleHere"
														: "createOpportunity.saveDraftRequiresTitle",
												)
											: undefined
									}
									onClick={() => void submit(true)}
								>
									{submitting === "draft"
										? t("createOpportunity.savingDraft")
										: t("createOpportunity.saveDraft")}
								</Button>
							)}
							{step < TOTAL_STEPS ? (
								<Button
									type="button"
									data-testid="modal-next"
									onClick={() => void handleNext()}
								>
									{t("createOpportunity.next")}
								</Button>
							) : (
								<Button
									type="button"
									disabled={submitting !== null}
									data-testid="modal-submit"
									onClick={() => void submit(false)}
								>
									{submitting === "publish"
										? isEditMode
											? t("createOpportunity.saving")
											: t("createOpportunity.creating")
										: isEditMode
											? t("createOpportunity.save")
											: t("createOpportunity.publish")}
								</Button>
							)}
						</div>
					</div>
				</div>
			</Modal>

			{showDiscardConfirm && (
				<ConfirmDialog
					title={t("createOpportunity.unsavedChangesTitle")}
					message={t("createOpportunity.unsavedChangesMessage")}
					confirmLabel={t("createOpportunity.discardChanges")}
					onConfirm={() => {
						setShowDiscardConfirm(false);
						onClose();
					}}
					onClose={() => setShowDiscardConfirm(false)}
				>
					{slotChangesApplied && (
						<p className="text-sm text-amber-700">
							{t("createOpportunity.slotChangesKeptOnDiscard")}
						</p>
					)}
				</ConfirmDialog>
			)}

			{pendingSlotDelete && (
				<ConfirmDialog
					title={t("confirmDialog.removeTimeSlot.title")}
					message={t("confirmDialog.removeTimeSlot.message")}
					confirmLabel={t("confirmDialog.removeTimeSlot.confirm")}
					loading={removingSlotId === pendingSlotDelete.id}
					error={slotDeleteError}
					onConfirm={() => void performExistingSlotDelete(pendingSlotDelete.id)}
					onClose={() => {
						setPendingSlotDelete(null);
						setSlotDeleteError(null);
					}}
				>
					{pendingSlotDelete.bookedCount > 0 && (
						<p className="text-sm text-amber-700">
							{t("timeSlots.deleteSeries.cancelsEngagementsWarning")}
						</p>
					)}
				</ConfirmDialog>
			)}

			{pendingSlotEdit && (
				<ConfirmDialog
					title={t("confirmDialog.editTimeSlot.title")}
					message={t("confirmDialog.editTimeSlot.message", {
						count: pendingSlotEdit.bookedCount,
					})}
					confirmLabel={t("confirmDialog.editTimeSlot.confirm")}
					loading={updatingSlotId === pendingSlotEdit.id}
					onConfirm={() => void applySlotEdit(pendingSlotEdit)}
					onClose={() => setPendingSlotEdit(null)}
				/>
			)}

			{pendingSeriesDelete && (
				<DeleteSeriesSlotDialog
					bookedCount={pendingSeriesDelete.bookedCount}
					loading={deletingSeriesSlot}
					error={seriesDeleteError}
					onConfirm={(scope) => void performSeriesDelete(scope)}
					onClose={() => setPendingSeriesDelete(null)}
				/>
			)}

			{croppingBannerFile && (
				<ImageCropModal
					file={croppingBannerFile}
					aspectRatio={2.5}
					shape="rect"
					outputWidth={1200}
					outputHeight={480}
					title={t("createOpportunity.fieldBanner")}
					onCancel={() => setCroppingBannerFile(null)}
					onCropped={handleBannerCropped}
				/>
			)}
		</>
	);
}

import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import type { UseFormRegister } from "react-hook-form";
import type { AddressDto } from "../../client";
import { ChoiceGroup, FloatingField } from "./shared";
import type { OpportunityFormValues } from "./schema";
import { inlineLinkClass } from "../../lib/linkClasses";

interface Props {
	register: UseFormRegister<OpportunityFormValues>;
	isRemote: boolean;
	onRemoteChange: (isRemote: boolean) => void;
	errors: {
		street?: string;
		houseNumber?: string;
		zipCode?: string;
		city?: string;
	};
	organizationId: string;
	orgAddress: AddressDto | null;
	isEditMode: boolean;
	onApplyOrgAddress: () => void;
}

type LocationMode = "onSite" | "remote";

export default function LocationStep({
	register,
	isRemote,
	onRemoteChange,
	errors,
	organizationId,
	orgAddress,
	isEditMode,
	onApplyOrgAddress,
}: Props) {
	const { t } = useTranslation();

	return (
		<div className="space-y-5" data-testid="wizard-step-2">
			<ChoiceGroup<LocationMode>
				legend={t("createOpportunity.locationModeLegend")}
				name="opportunity-location-mode"
				value={isRemote ? "remote" : "onSite"}
				onChange={(mode) => onRemoteChange(mode === "remote")}
				options={[
					{
						value: "onSite",
						label: t("createOpportunity.locationOnSite"),
						id: "opportunity-on-site",
					},
					{
						value: "remote",
						label: t("createOpportunity.locationRemote"),
						id: "opportunity-remote",
					},
				]}
			/>

			{isRemote ? (
				<p className="text-xs text-gray-500">
					{t("createOpportunity.locationRemoteHint")}
				</p>
			) : (
				<div className="space-y-3">
					{orgAddress && !isEditMode && (
						<button
							type="button"
							onClick={onApplyOrgAddress}
							className="min-h-6 text-sm font-medium text-brand-700 hover:underline"
						>
							{t("createOpportunity.useOrgAddress")}
						</button>
					)}
					<div className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_9rem]">
						<FloatingField
							id="opportunity-street"
							label={t("createOpportunity.fieldStreet")}
							registration={register("street")}
							required
							error={errors.street}
							maxLength={100}
						/>
						<FloatingField
							id="opportunity-house"
							label={t("createOpportunity.fieldNumber")}
							registration={register("houseNumber")}
							required
							error={errors.houseNumber}
							maxLength={10}
						/>
					</div>
					<div className="grid grid-cols-1 gap-3 sm:grid-cols-[7rem_1fr]">
						<FloatingField
							id="opportunity-zip"
							label={t("createOpportunity.fieldZip")}
							registration={register("zipCode")}
							required
							error={errors.zipCode}
							maxLength={5}
							inputMode="numeric"
							pattern="\d{5}"
						/>
						<FloatingField
							id="opportunity-city"
							label={t("createOpportunity.fieldCity")}
							registration={register("city")}
							required
							error={errors.city}
							maxLength={100}
						/>
					</div>
					<p className="text-xs text-gray-500" data-testid="location-hint">
						{t("createOpportunity.locationHint")}
						{!orgAddress && !isEditMode && (
							<>
								{" "}
								{t("createOpportunity.orgAddressTip")}{" "}
								<Link
									to={`/app/${organizationId}/dashboard/settings`}
									className={inlineLinkClass}
								>
									{t("createOpportunity.orgSettingsLink")}
								</Link>
							</>
						)}
					</p>
				</div>
			)}
		</div>
	);
}

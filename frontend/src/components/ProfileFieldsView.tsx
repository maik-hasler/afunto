import { useTranslation } from "react-i18next";
import Chip from "./Chip";

interface ProfileFieldsViewProps {
	bio?: string | null;
	skills: string[];
	languages: string[];
	preferredContact?: string | null;
	phone?: string | null;
}

export default function ProfileFieldsView({
	bio,
	skills,
	languages,
	preferredContact,
	phone,
}: ProfileFieldsViewProps) {
	const { t } = useTranslation();

	return (
		<div className="space-y-5">
			{bio && (
				<div>
					<p className="mb-1 text-sm font-medium text-gray-700">
						{t("profile.fieldBio")}
					</p>
					<p className="text-sm whitespace-pre-wrap text-gray-600">{bio}</p>
				</div>
			)}

			{skills.length > 0 && (
				<div>
					<p className="mb-2 text-sm font-medium text-gray-700">
						{t("profile.fieldSkills")}
					</p>
					<div className="flex flex-wrap gap-2">
						{skills.map((s) => (
							<Chip key={s} tone="brand">
								{s}
							</Chip>
						))}
					</div>
				</div>
			)}

			{languages.length > 0 && (
				<div>
					<p className="mb-2 text-sm font-medium text-gray-700">
						{t("profile.fieldLanguages")}
					</p>
					<div className="flex flex-wrap gap-2">
						{languages.map((l) => (
							<Chip key={l} tone="neutral">
								{l}
							</Chip>
						))}
					</div>
				</div>
			)}

			{preferredContact && (
				<div>
					<p className="mb-1 text-sm font-medium text-gray-700">
						{t("profile.fieldPreferredContact")}
					</p>
					<p className="text-sm text-gray-600">
						{preferredContact === "Email"
							? t("profile.preferredContactEmail")
							: t("profile.preferredContactPhone")}
					</p>
				</div>
			)}

			{phone && (
				<div>
					<p className="mb-1 text-sm font-medium text-gray-700">
						{t("profile.fieldPhone")}
					</p>
					<p className="text-sm text-gray-600">{phone}</p>
				</div>
			)}
		</div>
	);
}

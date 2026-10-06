import { useState } from "react";
import { useNavigate } from "react-router";
import { useAuth } from "react-oidc-context";
import { useTranslation } from "react-i18next";
import { useApiClient } from "../../hooks/useApiClient";
import { getApiErrorMessage } from "../../lib/apiError";
import { clearActiveOrgId } from "../../lib/activeOrg";
import { clearSeenAchievements } from "../../hooks/useAchievementNotifier";
import ConfirmDialog from "../../components/ConfirmDialog";
import DangerZonePanel from "../../components/DangerZonePanel";

export default function DangerZoneCard() {
	const auth = useAuth();
	const api = useApiClient();
	const { t } = useTranslation();
	const navigate = useNavigate();

	const [showDeleteDialog, setShowDeleteDialog] = useState(false);
	const [deleting, setDeleting] = useState(false);
	const [deleteError, setDeleteError] = useState<string | null>(null);

	async function handleDeleteAccount() {
		setDeleting(true);
		setDeleteError(null);
		try {
			await api.deleteMyAccount({});

			clearActiveOrgId();
			clearSeenAchievements(auth.user?.profile?.sub);
			localStorage.removeItem("i18nextLng");
			localStorage.removeItem("afunto:language-explicit");
			await auth.removeUser();
			navigate("/");
		} catch (err) {
			setDeleteError(getApiErrorMessage(err, t("account.deleteError")));
			setDeleting(false);
		}
	}

	return (
		<>
			<DangerZonePanel
				className="max-w-3xl"
				title={t("account.dangerZoneTitle")}
				description={t("account.dangerZoneDescription")}
				actionLabel={t("account.deleteAccountButton")}
				onAction={() => setShowDeleteDialog(true)}
			/>

			{showDeleteDialog && (
				<ConfirmDialog
					title={t("account.deleteConfirmTitle")}
					message={t("account.deleteConfirmMessage")}
					confirmLabel={t("account.deleteConfirmButton")}
					onConfirm={handleDeleteAccount}
					onClose={() => {
						setShowDeleteDialog(false);
						setDeleteError(null);
					}}
					loading={deleting}
					error={deleteError}
				/>
			)}
		</>
	);
}

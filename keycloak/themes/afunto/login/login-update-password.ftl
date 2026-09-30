<#import "template.ftl" as layout>

<@layout.registrationLayout
	displayMessage=!messagesPerField.existsError('password','password-confirm')
	pageTitle="updatePasswordTitle"
	eyebrow="stepSecurity"
	lead="updatePasswordLead"; section>

	<#if section = "header">
		${msg("updatePasswordTitle")}

	<#elseif section = "form">
		<form id="kc-passwd-update-form" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">

			<div class="form-group">
				<div class="form-field form-field--with-toggle">
					<input
						type="password"
						id="password-new"
						name="password-new"
						class="${properties.kcInputClass!}"
						aria-invalid="<#if messagesPerField.existsError('password','password-confirm')>true</#if>"
						autocomplete="new-password"
						autofocus
						required
						placeholder=" "
					/>
					<label for="password-new" class="${properties.kcLabelClass!}">${msg("passwordNew")}</label>
					<#if properties.kcFormPasswordVisibilityButtonClass?has_content>
						<button
							class="${properties.kcFormPasswordVisibilityButtonClass!}"
							type="button"
							aria-label="${msg('showPassword')}"
							aria-controls="password-new"
							data-password-toggle
							data-icon-show="${properties.kcFormPasswordVisibilityIconShow!}"
							data-icon-hide="${properties.kcFormPasswordVisibilityIconHide!}"
							data-label-show="${msg('showPassword')}"
							data-label-hide="${msg('hidePassword')}"
						>
							<i class="${properties.kcFormPasswordVisibilityIconShow!}" aria-hidden="true"></i>
						</button>
					</#if>
				</div>
				<#if messagesPerField.existsError('password')>
					<span id="input-error-password" class="${properties.kcInputErrorMessageClass!}" aria-live="polite">
						${kcSanitize(messagesPerField.get('password'))?no_esc}
					</span>
				</#if>
			</div>

			<div class="form-group">
				<div class="form-field form-field--with-toggle">
					<input
						type="password"
						id="password-confirm"
						name="password-confirm"
						class="${properties.kcInputClass!}"
						aria-invalid="<#if messagesPerField.existsError('password-confirm')>true</#if>"
						autocomplete="new-password"
						required
						placeholder=" "
					/>
					<label for="password-confirm" class="${properties.kcLabelClass!}">${msg("passwordConfirm")}</label>
					<#if properties.kcFormPasswordVisibilityButtonClass?has_content>
						<button
							class="${properties.kcFormPasswordVisibilityButtonClass!}"
							type="button"
							aria-label="${msg('showPassword')}"
							aria-controls="password-confirm"
							data-password-toggle
							data-icon-show="${properties.kcFormPasswordVisibilityIconShow!}"
							data-icon-hide="${properties.kcFormPasswordVisibilityIconHide!}"
							data-label-show="${msg('showPassword')}"
							data-label-hide="${msg('hidePassword')}"
						>
							<i class="${properties.kcFormPasswordVisibilityIconShow!}" aria-hidden="true"></i>
						</button>
					</#if>
				</div>
				<#if messagesPerField.existsError('password-confirm')>
					<span id="input-error-password-confirm" class="${properties.kcInputErrorMessageClass!}" aria-live="polite">
						${kcSanitize(messagesPerField.get('password-confirm'))?no_esc}
					</span>
				</#if>
			</div>

			<div class="form-field form-field-checkbox">
				<input type="checkbox" id="logout-sessions" name="logout-sessions" value="on" class="checkbox-control" checked>
				<label for="logout-sessions">${msg("logoutOtherSessions")}</label>
			</div>

			<div id="kc-form-buttons" class="${properties.kcFormButtonsClass!}">

				<input
					name="login"
					class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
					type="submit"
					value="${msg('doSetPassword')}"
				/>
				<#if isAppInitiatedAction??>
					<button
						class="${properties.kcButtonClass!} ${properties.kcButtonDefaultClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
						type="submit"
						name="cancel-aia"
						value="true"
						formnovalidate
					>${msg("doCancel")}</button>
				</#if>
			</div>

		</form>
	</#if>

</@layout.registrationLayout>

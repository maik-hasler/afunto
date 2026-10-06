<#import "template.ftl" as layout>
<@layout.registrationLayout
	displayMessage=!messagesPerField.existsError("username")
	displayInfo=true
	pageTitle="emailForgotTitle"
	eyebrow="stepRecovery"
	lead="emailForgotIntro"; section>

	<#if section = "header">
		${msg("emailForgotTitle")}

	<#elseif section = "form">
		<form id="kc-reset-password-form" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">

			<div class="form-group">
				<div class="form-field">
					<input
						id="username"
						class="${properties.kcInputClass!}"
						name="username"
						value="${(auth.attemptedUsername!'')}"
						type="text"
						aria-invalid="<#if messagesPerField.existsError('username')>true</#if>"
						autocomplete="username"
						autofocus
						required
						placeholder=" "
					/>
					<label for="username" class="${properties.kcLabelClass!}">
						<#if !realm.loginWithEmailAllowed>
							${msg("username")}
						<#elseif !realm.registrationEmailAsUsername>
							${msg("usernameOrEmail")}
						<#else>
							${msg("email")}
						</#if>
					</label>
				</div>
				<#if messagesPerField.existsError('username')>
					<span id="input-error-username" class="${properties.kcInputErrorMessageClass!}" aria-live="polite">
						${kcSanitize(messagesPerField.get('username'))?no_esc}
					</span>
				</#if>
			</div>

			<div class="${properties.kcFormButtonsClass!}">
				<input
					class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
					type="submit"
					value="${msg('doSubmitResetPassword')}"
				/>
			</div>

		</form>

	<#elseif section = "info">

		<a href="${url.loginUrl}" class="back-link">
			<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
				stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
				<path d="m15 18-6-6 6-6"/>
			</svg>
			${msg("backToLogin")}
		</a>

	</#if>

</@layout.registrationLayout>

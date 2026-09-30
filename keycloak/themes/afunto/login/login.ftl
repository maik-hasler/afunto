<#import "template.ftl" as layout>
<@layout.registrationLayout
	displayMessage=!messagesPerField.existsError("username","password")
	displayInfo=realm.password && realm.registrationAllowed && !registrationDisabled??
	pageTitle="loginTitle"
	eyebrow="stepSignIn"; section>

	<#if section = "header">
		${msg("loginTitle")}

	<#elseif section = "form">
		<#if realm.password>

			<form id="kc-form-login" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">

				<#if messagesPerField.existsError('username','password')>
					<div class="form-error-banner" role="alert">
						<span id="input-error">${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}</span>
					</div>
				</#if>

				<div class="form-group">
					<div class="form-field">
						<input
							id="username"
							class="${properties.kcInputClass!}"
							name="username"
							value="${(login.username!'')}"
							type="text"
							aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"
							<#if messagesPerField.existsError('username','password')>aria-describedby="input-error"</#if>
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
				</div>

				<div class="form-group">
					<div class="form-field form-field--with-toggle">
						<input
							id="password"
							class="${properties.kcInputClass!}"
							name="password"
							type="password"
							aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"
							<#if messagesPerField.existsError('username','password')>aria-describedby="input-error"</#if>
							autocomplete="current-password"
							required
							placeholder=" "
						/>
						<label for="password" class="${properties.kcLabelClass!}">${msg("password")}</label>
						<#if properties.kcFormPasswordVisibilityButtonClass?has_content>
							<button
								class="${properties.kcFormPasswordVisibilityButtonClass!}"
								type="button"
								aria-label="${msg('showPassword')}"
								aria-controls="password"
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
				</div>

				<div id="kc-form-options" class="form-options">
					<div class="form-options-wrapper">
						<#if realm.rememberMe>
							<label>
								<input id="rememberMe" name="rememberMe" type="checkbox" <#if login.rememberMe??>checked</#if>>
								<span>${msg("rememberMe")}</span>
							</label>
						</#if>
					</div>
					<#if realm.resetPasswordAllowed>
						<a href="${url.loginResetCredentialsUrl}" class="link-accent">${msg("doForgotPassword")}</a>
					</#if>
				</div>

				<div id="kc-form-buttons" class="${properties.kcFormButtonsClass!}">
					<input
						class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
						name="login"
						id="kc-login"
						type="submit"
						value="${msg('doLogIn')}"
					/>
				</div>

			</form>
		</#if>

	<#elseif section = "info">
		<#if realm.password && realm.registrationAllowed && !registrationDisabled??>
			${msg("noAccount")} <a href="${url.registrationUrl}">${msg("doRegister")}</a>
		</#if>
	</#if>

</@layout.registrationLayout>

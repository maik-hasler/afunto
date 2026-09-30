<#import "template.ftl" as layout>

<@layout.registrationLayout
	displayInfo=!isAppInitiatedAction??
	pageTitle="emailVerifyTitle"
	eyebrow="stepVerify"; section>

	<#if section = "header">
		${msg("emailVerifyTitle")}

	<#elseif section = "form">
		<p class="instruction">
			<#if verifyEmail??>
				${msg("emailVerifyInstruction1", verifyEmail)}
			<#else>
				${msg("emailVerifyInstruction4", user.email)}
			</#if>
		</p>

		<#if isAppInitiatedAction??>
			<form id="kc-verify-email-form" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">
				<div id="kc-form-buttons" class="${properties.kcFormButtonsClass!}">
					<#if verifyEmail??>
						<input
							class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
							type="submit"
							value="${msg('emailVerifyResend')}"
						/>
					<#else>
						<input
							class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
							type="submit"
							value="${msg('emailVerifySend')}"
						/>
					</#if>
					<button
						class="${properties.kcButtonClass!} ${properties.kcButtonDefaultClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
						type="submit"
						name="cancel-aia"
						value="true"
						formnovalidate
					>${msg("doCancel")}</button>
				</div>
			</form>
		</#if>

	<#elseif section = "info">
		<span>${msg("emailVerifyInstruction2")} <a href="${url.loginAction}">${msg("emailVerifyInstruction3")}</a></span>

	</#if>

</@layout.registrationLayout>

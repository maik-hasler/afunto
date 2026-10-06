<#import "template.ftl" as layout>
<#import "user-profile-commons.ftl" as userProfileCommons>

<@layout.registrationLayout
	displayMessage=messagesPerField.exists('global')
	displayRequiredFields=true
	pageTitle="loginProfileTitle"
	eyebrow="stepProfile"
	lead="loginProfileLead"; section>

	<#if section = "header">
		${msg("loginProfileTitle")}

	<#elseif section = "form">
		<form id="kc-update-profile-form" class="${properties.kcFormClass!}" action="${url.loginAction}" method="post">

			<@userProfileCommons.userProfileFormFields/>

			<div id="kc-form-buttons" class="${properties.kcFormButtonsClass!}">
				<input
					class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
					type="submit"
					value="${msg('doSaveProfile')}"
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

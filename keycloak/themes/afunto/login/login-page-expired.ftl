<#import "template.ftl" as layout>

<@layout.registrationLayout
	pageTitle="pageExpiredTitle"
	eyebrow="stepSession"
	lead="pageExpiredLead"; section>

	<#if section = "header">
		${msg("pageExpiredTitle")}

	<#elseif section = "form">
		<div id="kc-page-expired" class="${properties.kcFormButtonsClass!}">
			<a
				id="loginContinueLink"
				class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
				href="${url.loginAction}"
			>${msg("pageExpiredContinue")}</a>
			<a
				id="loginRestartLink"
				class="${properties.kcButtonClass!} ${properties.kcButtonDefaultClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
				href="${url.loginRestartFlowUrl}"
			>${msg("pageExpiredRestart")}</a>
		</div>
	</#if>

</@layout.registrationLayout>

<#import "template.ftl" as layout>

<@layout.registrationLayout
	displayMessage=false
	pageTitle="errorTitle"; section>

	<#if section = "header">
		${kcSanitize(msg("errorTitle"))?no_esc}

	<#elseif section = "form">
		<div id="kc-error-message">
			<p class="instruction">${kcSanitize(message.summary)?no_esc}</p>

			<#if traceId??>
				<p class="instruction" id="traceId">${msg("traceIdSupportMessage", traceId)}</p>
			</#if>

			<#if !skipLink??>
				<#if client?? && (client.baseUrl)?has_content>
					<#assign backUrl = client.baseUrl>
				<#else>
					<#assign backUrl = properties.siteUrl>
				</#if>
				<div class="${properties.kcFormButtonsClass!}">
					<a id="backToApplication" class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}" href="${backUrl}">${msg("backToApplication")}</a>
				</div>
			</#if>
		</div>
	</#if>

</@layout.registrationLayout>

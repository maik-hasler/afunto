<#import "template.ftl" as layout>

<@layout.registrationLayout
	displayMessage=false
	pageTitle="infoTitle"
	eyebrow="stepStatus"; section>

	<#if section = "header">
		<#if messageHeader??>
			${kcSanitize(msg("${messageHeader}"))?no_esc}
		<#else>
			${kcSanitize(message.summary)?no_esc}
		</#if>

	<#elseif section = "form">
		<div id="kc-info-message">

			<#if messageHeader?? || (requiredActions??)>
				<p class="instruction">
					${kcSanitize(message.summary)?no_esc}<#if requiredActions??><#list requiredActions>: <b><#items as reqActionItem>${kcSanitize(msg("requiredAction.${reqActionItem}"))?no_esc}<#sep>, </#items></b></#list></#if>
				</p>
			</#if>

			<#if !skipLink??>
				<div class="${properties.kcFormButtonsClass!}">
					<#if pageRedirectUri?has_content>
						<a class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}" href="${pageRedirectUri}">${msg("backToApplication")}</a>
					<#elseif actionUri?has_content>
						<a class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}" href="${actionUri}">${msg("proceedWithAction")}</a>
					<#else>

						<#if (client.baseUrl)?has_content>
							<#assign backUrl = client.baseUrl>
						<#else>
							<#assign backUrl = properties.siteUrl>
						</#if>
						<a class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}" href="${backUrl}">${msg("backToApplication")}</a>
					</#if>
				</div>
			</#if>
		</div>
	</#if>

</@layout.registrationLayout>

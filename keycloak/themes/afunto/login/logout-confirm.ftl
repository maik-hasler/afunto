<#import "template.ftl" as layout>

<#if logoutConfirm.skipLink>
	<#assign cancelUrl = "">
<#elseif (client.baseUrl)?has_content>
	<#assign cancelUrl = client.baseUrl>
<#else>
	<#assign cancelUrl = properties.siteUrl>
</#if>

<@layout.registrationLayout
	pageTitle="logoutConfirmTitle"
	eyebrow="stepSession"
	showBackLink=!cancelUrl?has_content; section>

	<#if section = "header">
		${msg("logoutConfirmTitle")}

	<#elseif section = "form">
		<div id="kc-logout-confirm" class="content-area">
			<p class="instruction">${msg("logoutConfirmHeader")}</p>

			<form class="form-actions" action="${url.logoutConfirmAction}" method="POST">
				<input type="hidden" name="session_code" value="${logoutConfirm.code}">
				<div id="kc-form-buttons" class="${properties.kcFormButtonsClass!}">
					<input
						class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
						name="confirmLogout"
						id="kc-logout"
						type="submit"
						value="${msg('doLogout')}"
					/>
					<#if cancelUrl?has_content>
						<a
							id="kc-logout-cancel"
							class="${properties.kcButtonClass!} ${properties.kcButtonDefaultClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
							href="${cancelUrl}"
						>${msg("doCancel")}</a>
					</#if>
				</div>
			</form>
		</div>
	</#if>

</@layout.registrationLayout>

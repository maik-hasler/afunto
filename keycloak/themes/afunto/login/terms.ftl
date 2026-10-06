<#import "template.ftl" as layout>

<@layout.registrationLayout
	displayMessage=false
	pageTitle="termsTitle"
	eyebrow="stepTerms"
	lead="termsIntro"; section>

	<#if section = "header">
		${msg("termsTitle")}

	<#elseif section = "form">
		<div id="kc-terms-text">
			${kcSanitize(msg("termsDetails", properties.siteUrl))?no_esc}
		</div>

		<form class="${properties.kcFormClass!}" action="${url.loginAction}" method="POST">
			<div id="kc-form-buttons" class="${properties.kcFormButtonsClass!}">
				<input
					class="${properties.kcButtonClass!} ${properties.kcButtonPrimaryClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
					name="accept"
					id="kc-accept"
					type="submit"
					value="${msg('doAccept')}"
				/>
				<input
					class="${properties.kcButtonClass!} ${properties.kcButtonDefaultClass!} ${properties.kcButtonBlockClass!} ${properties.kcButtonLargeClass!}"
					name="cancel"
					id="kc-decline"
					type="submit"
					value="${msg('doDecline')}"
				/>
			</div>
		</form>
	</#if>

</@layout.registrationLayout>

<#import "template.ftl" as layout>
<@layout.emailLayout
	intro=msg("emailUpdateConfirmationIntro", newEmail)
	action=msg("emailUpdateConfirmationAction")
	link=link
	expiry=msg("emailLinkExpiry", linkExpirationFormatter(linkExpiration))
	note=msg("emailUpdateConfirmationIgnore")/>

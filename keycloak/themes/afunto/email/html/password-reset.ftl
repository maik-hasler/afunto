<#import "template.ftl" as layout>
<@layout.emailLayout
	intro=msg("passwordResetIntro")
	action=msg("passwordResetAction")
	link=link
	expiry=msg("emailLinkExpiry", linkExpirationFormatter(linkExpiration))
	note=msg("passwordResetIgnore")/>

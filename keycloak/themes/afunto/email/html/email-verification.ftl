<#import "template.ftl" as layout>
<@layout.emailLayout
	intro=msg("emailVerificationIntro")
	action=msg("emailVerificationAction")
	link=link
	expiry=msg("emailLinkExpiry", linkExpirationFormatter(linkExpiration))
	note=msg("emailVerificationIgnore")/>

<#ftl output_format="plainText">
<#assign requiredActionsText><#if requiredActions??><#list requiredActions><#items as reqActionItem>${msg("requiredAction.${reqActionItem}")}<#sep>, </#sep></#items></#list></#if></#assign>
<#import "template.ftl" as layout>
<@layout.emailLayout
	intro=msg("executeActionsIntro", requiredActionsText)
	action=msg("executeActionsAction")
	link=link
	expiry=msg("emailLinkExpiry", linkExpirationFormatter(linkExpiration))
	note=msg("executeActionsIgnore")/>

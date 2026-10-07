<#ftl output_format="plainText">
<#-- The plain-text twin of html/template.ftl, in the same order. -->
<#function greeting>
	<#if user?? && user.firstName?has_content>
		<#return msg("emailGreeting", user.firstName)>
	<#elseif user?? && user.username?has_content>
		<#return msg("emailGreeting", user.username)>
	</#if>
	<#return msg("emailGreetingWithoutName")>
</#function>

<#macro emailLayout intro action link note expiry="">
${greeting()}

${intro}

${action}: ${link}

<#if expiry?has_content>${expiry} </#if>${note}

${msg("emailClosing")}
${msg("emailSignature")}

<#-- "-- " (trailing space) is the conventional signature separator; interpolated so the source line carries no trailing whitespace. -->
--${" "}
${msg("emailFooter")}
</#macro>

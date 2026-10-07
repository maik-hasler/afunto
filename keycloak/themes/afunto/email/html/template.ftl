<#--
	The same card the backend's own emails use (backend/src/Infrastructure/Email/HtmlEmailWriter.cs):
	brand-50 page, white card, the wordmark, one brand-700 button, a muted footer. Keep the two
	in step - a new user gets this theme's verification email first and the backend's emails
	after, and both should read as one product (#2402). Every value is auto-escaped by the
	HTML output format, newEmail included.
-->
<#function greeting>
	<#if user?? && user.firstName?has_content>
		<#return msg("emailGreeting", user.firstName)>
	<#elseif user?? && user.username?has_content>
		<#return msg("emailGreeting", user.username)>
	</#if>
	<#return msg("emailGreetingWithoutName")>
</#function>

<#macro emailLayout intro action link note expiry="">
<#assign fontStack = "'Source Sans 3', 'Segoe UI', Arial, Helvetica, sans-serif">
<!DOCTYPE html>
<html lang="${locale.language}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${realmName}</title>
</head>
<body style="margin:0;padding:0;background-color:#f0faf5;">
<div style="display:none;max-height:0;overflow:hidden;">${intro}</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f0faf5;">
<tr>
<td align="center" style="padding:24px 16px;">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background-color:#ffffff;border-radius:8px;">
<tr>
<td style="padding:24px 32px 0;font-family:${fontStack};font-size:22px;font-weight:700;color:#1a3c2b;">Afunto</td>
</tr>
<tr>
<td style="padding:16px 32px 32px;font-family:${fontStack};font-size:16px;line-height:1.5;color:#111827;">
<p style="margin:0 0 16px;">${greeting()}</p>
<p style="margin:0 0 16px;">${intro}</p>
<table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 24px;">
<tr>
<td style="border-radius:6px;background-color:#226947;"><a href="${link}" style="display:inline-block;padding:12px 20px;font-family:${fontStack};font-size:16px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:6px;">${action}</a></td>
</tr>
</table>
<p style="margin:0 0 16px;font-size:14px;color:#6b7280;"><#if expiry?has_content>${expiry} </#if>${note}</p>
<p style="margin:0;">${msg("emailClosing")}<br>
${msg("emailSignature")}</p>
</td>
</tr>
</table>
<p style="max-width:560px;margin:16px auto 0;font-family:${fontStack};font-size:13px;line-height:1.5;color:#6b7280;">${msg("emailFooter")}</p>
</td>
</tr>
</table>
</body>
</html>
</#macro>

namespace Application.Common.Email;

public interface IEmailTemplateRenderer
{
	RenderedEmail Render(EmailDraft draft);
}

using Domain.Primitives;

namespace Domain.Users;

public sealed class User
	: AggregateRoot<UserId>,
		ISoftDeletableEntity,
		IAuditableEntity
{
	private List<string> _skills = [];

	private List<string> _languages = [];

	public string? AvatarUrl { get; private set; }

	public string? Bio { get; private set; }

	public string? Phone { get; private set; }

	public IReadOnlyList<string> Skills => _skills.AsReadOnly();

	public IReadOnlyList<string> Languages => _languages.AsReadOnly();

	public PreferredContact? PreferredContact { get; private set; }

	public string? PreferredLanguage { get; private set; }

	public bool IsDeleted { get; private set; }

	public DateTimeOffset? DeletedOn { get; private set; }

	public Guid UnsubscribeToken { get; private set; }

	public bool NotifyOnNewSignUp { get; private set; } = true;

	public bool NotifyOnWithdrawal { get; private set; } = true;

	public bool NotifyOnEngagementConfirmed { get; private set; } = true;

	public bool NotifyOnEngagementReminder { get; private set; } = true;

	public DateTimeOffset CreatedOn { get; private set; }

	public DateTimeOffset? ModifiedOn { get; private set; }

#pragma warning disable CS8618
	private User() : base(default) { }
#pragma warning restore CS8618

	private User(UserId id) : base(id)
	{
		UnsubscribeToken = Guid.NewGuid();
	}

	public static User Create(UserId id) => new(id);

	public void SetAvatarUrl(string? url)
	{
		AvatarUrl = url;
	}

	public void ChangeBio(string? bio)
	{
		Bio = bio;
	}

	public void SetPhone(string? phone)
	{
		Phone = phone;
	}

	public void UpdateSkills(IReadOnlyCollection<string> skills)
	{
		_skills = new List<string>(skills);
	}

	public void UpdateLanguages(IReadOnlyCollection<string> languages)
	{
		_languages = new List<string>(languages);
	}

	public void SetPreferredContact(PreferredContact? preferredContact)
	{
		PreferredContact = preferredContact;
	}

	public void SetPreferredLanguage(string? preferredLanguage)
	{
		PreferredLanguage = preferredLanguage;
	}

	public void UpdateNotificationPreferences(
		bool notifyOnNewSignUp,
		bool notifyOnWithdrawal,
		bool notifyOnEngagementConfirmed,
		bool notifyOnEngagementReminder)
	{
		NotifyOnNewSignUp = notifyOnNewSignUp;
		NotifyOnWithdrawal = notifyOnWithdrawal;
		NotifyOnEngagementConfirmed = notifyOnEngagementConfirmed;
		NotifyOnEngagementReminder = notifyOnEngagementReminder;
	}

	public bool IsSubscribedTo(EmailNotificationType type) => type switch
	{
		EmailNotificationType.NewSignUp => NotifyOnNewSignUp,
		EmailNotificationType.Withdrawal => NotifyOnWithdrawal,
		EmailNotificationType.EngagementConfirmed => NotifyOnEngagementConfirmed,
		EmailNotificationType.EngagementReminder => NotifyOnEngagementReminder,
		_ => true,
	};

	public Result Unsubscribe(EmailNotificationType type, Guid token)
	{
		if (token != UnsubscribeToken)
			return Result.Failure(Error.Forbidden("User.InvalidUnsubscribeToken", "The unsubscribe link is invalid or has already been used with a different token."));

		switch (type)
		{
			case EmailNotificationType.NewSignUp:
				NotifyOnNewSignUp = false;
				break;
			case EmailNotificationType.Withdrawal:
				NotifyOnWithdrawal = false;
				break;
			case EmailNotificationType.EngagementConfirmed:
				NotifyOnEngagementConfirmed = false;
				break;
			case EmailNotificationType.EngagementReminder:
				NotifyOnEngagementReminder = false;
				break;
			default:

				return Result.Failure(Error.Validation(
					"User.UnknownEmailNotificationType", $"Unknown email notification type '{type}'."));
		}

		return Result.Success();
	}

	public void MarkAccountDeleted()
	{
		AddEvent(new UserAccountDeletedDomainEvent(Id));
	}

	public Result MarkDeleted(DateTimeOffset deletedOn)
	{
		if (IsDeleted)
			return Result.Failure(Error.Conflict("User.AlreadyDeleted", "User is already shadow-deleted."));

		IsDeleted = true;
		DeletedOn = deletedOn;
		return Result.Success();
	}

	public Result Restore()
	{
		if (!IsDeleted)
			return Result.Failure(Error.Conflict("User.NotDeleted", "User is not shadow-deleted."));

		IsDeleted = false;
		DeletedOn = null;
		return Result.Success();
	}
}

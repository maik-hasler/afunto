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

	public bool IsDeleted { get; private set; }

	public DateTimeOffset? DeletedOn { get; private set; }

	public DateTimeOffset CreatedOn { get; private set; }

	public DateTimeOffset? ModifiedOn { get; private set; }

	/// <summary>
	/// When someone else last changed what this user may do (a role, a block).
	/// Access tokens issued up to this moment carry the old permissions, so the
	/// API rejects them and the client fetches a fresh one (#2395, #2403, #2407).
	/// </summary>
	public DateTimeOffset? PermissionsChangedOn { get; private set; }

#pragma warning disable CS8618
	private User() : base(default) { }
#pragma warning restore CS8618

	private User(UserId id) : base(id) { }

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

	public void RecordPermissionsChanged(DateTimeOffset changedOn)
	{
		PermissionsChangedOn = changedOn;
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

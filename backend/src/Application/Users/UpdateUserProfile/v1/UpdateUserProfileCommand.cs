using Application.Common.Messaging;
using Domain.Users;

namespace Application.Users.UpdateUserProfile.v1;

public sealed record UpdateUserProfileCommand(
	UserId UserId,
	string? FirstName,
	string? LastName,
	string? Bio,
	string? Phone,
	IReadOnlyList<string> Skills,
	IReadOnlyList<string> Languages,
	PreferredContact? PreferredContactValue)
	: ICommand<bool>;

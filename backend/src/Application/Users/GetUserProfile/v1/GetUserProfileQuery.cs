using Application.Common.Messaging;
using Domain.Users;

namespace Application.Users.GetUserProfile.v1;

public sealed record GetUserProfileQuery(UserId UserId)
	: IQuery<MyProfileResponse>;

public sealed record MyProfileResponse(
	Guid Id,
	string Username,
	string? FirstName,
	string? LastName,
	string Email,
	string? AvatarUrl,
	string? Bio,
	string? Phone,
	IReadOnlyList<string> Skills,
	IReadOnlyList<string> Languages,
	string? PreferredContact);

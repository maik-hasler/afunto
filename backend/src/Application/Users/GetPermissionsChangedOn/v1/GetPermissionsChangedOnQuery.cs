using Application.Common.Messaging;
using Domain.Users;

namespace Application.Users.GetPermissionsChangedOn.v1;

public sealed record GetPermissionsChangedOnQuery(UserId UserId)
	: IQuery<DateTimeOffset?>;

using Application.Common.Messaging;
using Application.Common.Persistence;

namespace Application.Users.GetPermissionsChangedOn.v1;

internal sealed class GetPermissionsChangedOnQueryHandler(
	IApplicationDbContext dbContext)
	: IQueryHandler<GetPermissionsChangedOnQuery, DateTimeOffset?>
{
	public async ValueTask<DateTimeOffset?> Handle(
		GetPermissionsChangedOnQuery request,
		CancellationToken cancellationToken = default) =>
		await dbContext.GetPermissionsChangedOnAsync(request.UserId, cancellationToken);
}

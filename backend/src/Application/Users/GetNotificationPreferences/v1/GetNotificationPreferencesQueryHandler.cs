using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Users;

namespace Application.Users.GetNotificationPreferences.v1;

internal sealed class GetNotificationPreferencesQueryHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork)
	: IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesResponse>
{
	public async ValueTask<NotificationPreferencesResponse> Handle(
		GetNotificationPreferencesQuery request,
		CancellationToken cancellationToken = default)
	{
		var user = await dbContext.Users.FindAsync(request.UserId, cancellationToken);

		if (user is null)
		{
			user = User.Create(request.UserId);
			await dbContext.Users.AddAsync(user, cancellationToken);
			await unitOfWork.SaveChangesAsync(cancellationToken);
		}

		return new NotificationPreferencesResponse(
			user.NotifyOnNewSignUp,
			user.NotifyOnWithdrawal,
			user.NotifyOnEngagementConfirmed,
			user.NotifyOnEngagementReminder);
	}
}

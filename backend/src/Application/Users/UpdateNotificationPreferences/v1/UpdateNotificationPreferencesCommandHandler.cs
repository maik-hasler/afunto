using Application.Common.Messaging;
using Application.Common.Persistence;
using Domain.Users;

namespace Application.Users.UpdateNotificationPreferences.v1;

internal sealed class UpdateNotificationPreferencesCommandHandler(
	IApplicationDbContext dbContext,
	IUnitOfWork unitOfWork)
	: ICommandHandler<UpdateNotificationPreferencesCommand, bool>
{
	public async ValueTask<bool> Handle(
		UpdateNotificationPreferencesCommand request,
		CancellationToken cancellationToken = default)
	{
		var user = await dbContext.Users.FindAsync(request.UserId, cancellationToken);

		if (user is null)
		{
			user = User.Create(request.UserId);
			await dbContext.Users.AddAsync(user, cancellationToken);
		}

		user.UpdateNotificationPreferences(
			request.NotifyOnNewSignUp,
			request.NotifyOnWithdrawal,
			request.NotifyOnEngagementConfirmed,
			request.NotifyOnEngagementReminder);

		await unitOfWork.SaveChangesAsync(cancellationToken);

		return true;
	}
}

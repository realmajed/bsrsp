using Microsoft.Extensions.DependencyInjection;

namespace BeanSceneReservationSystemProject.Services
{
    public class ReservationReminderBackgroundService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<ReservationReminderBackgroundService> _logger;

        public ReservationReminderBackgroundService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ReservationReminderBackgroundService> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(CheckInterval);

            // Run once on startup, then keep checking every interval.
            await SendDueRemindersAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SendDueRemindersAsync(stoppingToken);
            }
        }

        private async Task SendDueRemindersAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var reservationService = scope.ServiceProvider.GetRequiredService<IReservationService>();
                await reservationService.SendDueReminderEmailsAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown, no need to log it as an error.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reservation reminder emails.");
            }
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OnlineBookstore.CatalogService.Data;
using OnlineBookstore.OrderService.Data;
using System.Diagnostics;

namespace OnlineBookstore.MigrationsService;

public sealed class Worker(
    IServiceProvider serviceProvider,
    ILogger<Worker> logger,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    public const string ActivitySourceName = "Migrations";
    private static readonly ActivitySource _activitySource = new(ActivitySourceName);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = _activitySource.StartActivity("Migrating database", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();

            await MigrateAsync<CatalogDbContext>(scope.ServiceProvider, stoppingToken);
            await MigrateAsync<OrderDbContext>(scope.ServiceProvider, stoppingToken);

            logger.LogInformation("Migrated database successfully.");
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task MigrateAsync<T>(
        IServiceProvider sp,
        CancellationToken cancellationToken)
        where T : DbContext
    {
        var context = sp.GetRequiredService<T>();
        await EnsureDatabaseAsync(context, cancellationToken);
        await RunMigrationAsync(context, cancellationToken);
    }

    private static async Task EnsureDatabaseAsync<T>(T dbContext, CancellationToken cancellationToken)
        where T : DbContext
    {
        var dbCreator = dbContext.GetService<IRelationalDatabaseCreator>();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            if (!await dbCreator.ExistsAsync(cancellationToken))
            {
                await dbCreator.CreateAsync(cancellationToken);
            }
        });
    }

    private static async Task RunMigrationAsync<T>(T dbContext, CancellationToken cancellationToken)
        where T : DbContext
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }
}

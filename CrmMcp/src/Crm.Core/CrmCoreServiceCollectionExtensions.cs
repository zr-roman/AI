using Crm.Core.Data;
using Crm.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Crm.Core;

public static class CrmCoreServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует БД и бизнес-сервисы CRM. Реализацию <see cref="ICurrentUser"/> регистрирует хост:
    /// в HTTP пользователь берётся из запроса, в stdio — из конфигурации.
    /// </summary>
    public static IServiceCollection AddCrmCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Crm")
            ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Crm.");

        services.Configure<CrmOptions>(configuration.GetSection(CrmOptions.SectionName));

        // Фабрика нужна аудиту (отдельный контекст); сам CrmDbContext при этом регистрируется как scoped
        services.AddDbContextFactory<CrmDbContext>(options => options.UseNpgsql(connectionString));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CrmClock>();
        services.AddSingleton<AuditLog>();

        services.AddScoped<ContactService>();
        services.AddScoped<DealService>();
        services.AddScoped<ActivityService>();
        services.AddScoped<TaskService>();

        return services;
    }
}

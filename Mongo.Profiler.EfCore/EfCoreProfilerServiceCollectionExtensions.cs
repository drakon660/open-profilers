using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mongo.Profiler;

namespace EFCore.Profiler;

public static class EfCoreProfilerServiceCollectionExtensions
{
    public static IServiceCollection AddEfCoreProfiler(
        this IServiceCollection services,
        Action<EfCoreProfilerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<EfCoreProfilerOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        services.AddSingleton<EfProfilerCommandInterceptor>(serviceProvider =>
        {
            var sink = serviceProvider.GetRequiredService<IMongoProfilerEventSink>();
            var options = serviceProvider.GetRequiredService<IOptions<EfCoreProfilerOptions>>().Value;
            return new EfProfilerCommandInterceptor(sink, options);
        });

        return services;
    }

    public static DbContextOptionsBuilder AddEfCoreProfiler(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var interceptor = serviceProvider.GetService<EfProfilerCommandInterceptor>();
        if (interceptor is not null)
            optionsBuilder.AddInterceptors(interceptor);

        return optionsBuilder;
    }
}

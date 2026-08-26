using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using Marten;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the LupiraContactApi bounded context (Marten event store + document store + transport-neutral services) into the host's DI container.</summary>
public static class CoreServiceCollectionExtensions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=lupira_contact;Username=lupira_contact_user;Password=devpassword";

    public static IServiceCollection AddContactCore(this IServiceCollection services)
    {
        // Resolve the connection string lazily from IConfiguration so test hosts (WebApplicationFactory) can
        // override ConnectionStrings:Postgres before the store is built.
        services.AddMarten(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? DefaultConnectionString;
            var opts = new StoreOptions();
            opts.Connection(connectionString);
            opts.UseLupiraContact();
            return opts;
        }).UseLightweightSessions();

        services.AddScoped<CompletenessResolver>();
        services.AddScoped<AccessResolver>();
        services.AddScoped<PrincipalDirectory>();
        services.AddScoped<LupiraContactApi.Core.Data.Idempotency>();
        services.AddScoped<AddressBookService>();
        services.AddScoped<ContactService>();
        services.AddScoped<ContactGroupService>();
        services.AddScoped<DavChangeFeed>();
        services.AddScoped<SyncFeed>();
        return services;
    }
}

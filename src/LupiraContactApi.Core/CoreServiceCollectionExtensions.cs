using Lupira.Identity.Marten;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using Marten;
using Microsoft.Extensions.Configuration;
using Principal = LupiraContactApi.Core.Domain.Identity.Principal;

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
        services.AddLupiraPrincipalDirectory<Principal>();
        services.AddScoped<Lupira.Marten.Idempotency.Idempotency>();
        services.AddScoped<AddressBookService>();
        services.AddScoped<ContactService>();
        services.AddScoped<ContactGroupService>();
        services.AddScoped<RelationshipService>();
        services.AddScoped<RelationshipFeed>();
        services.AddScoped<DavCards>();
        services.AddScoped<DavChangeFeed>();
        services.AddScoped<SyncFeed>();
        services.AddScoped<ResidencyService>();
        services.AddScoped<ResidencyFeed>();
        services.AddScoped<PlaceEntryService>();
        services.AddScoped<PlaceEntryFeed>();
        return services;
    }
}

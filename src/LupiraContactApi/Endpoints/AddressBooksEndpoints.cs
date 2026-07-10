using LupiraContactApi.Dtos.AddressBooks;
using LupiraContactApi.Handlers;

namespace LupiraContactApi.Endpoints;

public static class AddressBooksEndpoints
{
    public static IEndpointRouteBuilder MapAddressBooks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/address-books").RequireAuthorization("ApiPolicy").WithTags("AddressBooks");

        group.MapGet("/", (AddressBooksHandler h, CancellationToken ct) => h.ListAsync(ct))
            .WithName("ListAddressBooks")
            .WithSummary("List the address books the caller can access.")
            .Produces<List<AddressBookDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", (CreateAddressBookRequest body, AddressBooksHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithName("CreateAddressBook")
            .WithSummary("Create an address book; the caller becomes its owner.")
            .Produces<AddressBookDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{addressBookId:guid}/owners", (Guid addressBookId, GrantOwnerRequest body, AddressBooksHandler h, CancellationToken ct) => h.GrantOwnerAsync(addressBookId, body, ct))
            .WithName("GrantAddressBookOwner")
            .WithSummary("Grant a member access to an address book (access = owner|read-write|read; default owner).")
            .Produces<OwnerGrantDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{addressBookId:guid}/owners", (Guid addressBookId, string email, AddressBooksHandler h, CancellationToken ct) => h.RevokeOwnerAsync(addressBookId, email, ct))
            .WithName("RevokeAddressBookOwner")
            .WithSummary("Revoke a member's access to an address book (by email). 409 if it would remove the last owner.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}

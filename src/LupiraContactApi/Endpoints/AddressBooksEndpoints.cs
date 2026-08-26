using LupiraContactApi.Core.Dtos.AddressBooks;
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
            .Produces<List<AddressBookDto>>(StatusCodes.Status200OK);

        group.MapPost("/", (CreateAddressBookRequest body, AddressBooksHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithName("CreateAddressBook")
            .WithSummary("Create an address book; the caller becomes its owner.")
            .Produces<AddressBookDto>(StatusCodes.Status200OK);

        group.MapPut("/{addressBookId:guid}", (Guid addressBookId, UpdateAddressBookRequest body, AddressBooksHandler h, CancellationToken ct) => h.UpdateAsync(addressBookId, body, ct))
            .WithName("UpdateAddressBook")
            .WithSummary("Rename an address book or change its display name (owner only; merge — omitted fields are kept).")
            .Produces<AddressBookDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{addressBookId:guid}", (Guid addressBookId, AddressBooksHandler h, CancellationToken ct) => h.DeleteAsync(addressBookId, ct))
            .WithName("DeleteAddressBook")
            .WithSummary("Delete an empty address book (owner only). 409 if it still holds contacts or groups, or is the personal book.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{addressBookId:guid}/owners", (Guid addressBookId, AddressBooksHandler h, CancellationToken ct) => h.ListOwnersAsync(addressBookId, ct))
            .WithName("ListAddressBookOwners")
            .WithSummary("List who has access to an address book and at what level (owner only).")
            .Produces<List<OwnerGrantDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

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

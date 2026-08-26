using LupiraContactApi.Core.Application;
using LupiraContactApi.Auth;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

public sealed class AddressBooksHandler(CurrentUser user, AddressBookService books)
{
    public async Task<Results<Ok<List<AddressBookDto>>, UnauthorizedHttpResult>> ListAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkOnly(await books.ListAsync(u.Id, ct));
    }

    public async Task<Results<Ok<AddressBookDto>, UnauthorizedHttpResult>> CreateAsync(CreateAddressBookRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkOnly(await books.CreateAsync(u.Id, body, ct));
    }

    public async Task<Results<Ok<AddressBookDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> UpdateAsync(Guid addressBookId, UpdateAddressBookRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await books.UpdateAsync(u.Id, addressBookId, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid addressBookId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await books.DeleteAsync(u.Id, addressBookId, ct));
    }

    public async Task<Results<Ok<List<OwnerGrantDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ListOwnersAsync(Guid addressBookId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await books.ListOwnersAsync(u.Id, addressBookId, ct));
    }

    public async Task<Results<Ok<OwnerGrantDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GrantOwnerAsync(Guid addressBookId, GrantOwnerRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await books.GrantOwnerAsync(u.Id, addressBookId, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RevokeOwnerAsync(Guid addressBookId, string email, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await books.RevokeOwnerAsync(u.Id, addressBookId, email, ct));
    }
}

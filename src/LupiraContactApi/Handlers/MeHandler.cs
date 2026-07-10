using LupiraContactApi.Application;
using LupiraContactApi.Auth;
using LupiraContactApi.Dtos.AddressBooks;
using LupiraContactApi.Dtos.Me;
using LupiraContactApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

public sealed class MeHandler(CurrentUser user, AddressBookService books)
{
    public async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> GetAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(new MeDto { Id = u.Id, Email = u.Email, DisplayName = u.DisplayName });
    }

    public async Task<Results<Ok<List<AddressBookDto>>, UnauthorizedHttpResult>> BootstrapAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkOnly(await books.BootstrapPersonalAsync(u.Id, ct));
    }
}

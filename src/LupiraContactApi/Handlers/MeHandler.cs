using LupiraContactApi.Auth;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Me;
using LupiraContactApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

public sealed class MeHandler(CurrentUser user, AddressBookService books, ContactService contacts)
{
    public async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> GetAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(new MeDto { PrincipalId = u.Id, Email = u.Email, DisplayName = u.DisplayName, ContactId = u.ContactId });
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetContactAsync(SetMyContactRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await contacts.LinkSelfContactAsync(u.Id, body.ContactId, ct));
    }

    public async Task<Results<Ok<List<AddressBookDto>>, UnauthorizedHttpResult>> BootstrapAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkOnly(await books.BootstrapPersonalAsync(u.Id, ct));
    }
}

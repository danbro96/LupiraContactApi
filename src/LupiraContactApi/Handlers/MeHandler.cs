using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Me;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

public sealed class MeHandler(CurrentUser<Principal> user, AddressBookService books, ContactService contacts)
{
    public async Task<Results<Ok<MeDto>, UnauthorizedHttpResult>> GetAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        // Unlinked only: a book shared since the last call may hold the caller's card by now.
        var contactId = u.ContactId ?? await contacts.MatchSelfContactAsync(u.Id, ct);
        return TypedResults.Ok(new MeDto { PrincipalId = u.Id, Email = u.Email, DisplayName = u.DisplayName, ContactId = contactId });
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

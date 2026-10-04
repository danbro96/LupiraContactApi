using LupiraContactApi.Auth;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Dtos.PlaceEntries;
using LupiraContactApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

public sealed class PlaceEntriesHandler(CurrentUser user, PlaceEntryService entries)
{
    public async Task<Results<Ok<PlaceEntryDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid placeId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await entries.GetAsync(u.Id, placeId, ct));
    }

    public async Task<Results<Ok<PlaceEntryDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetCodeAsync(Guid placeId, Guid codeId, SetEntryCodeRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await entries.SetCodeAsync(u.Id, placeId, codeId, body, ct));
    }

    public async Task<Results<Ok<PlaceEntryDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RemoveCodeAsync(Guid placeId, Guid codeId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await entries.RemoveCodeAsync(u.Id, placeId, codeId, ct));
    }
}

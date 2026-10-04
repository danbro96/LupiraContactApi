using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Core.Dtos.Residencies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

public sealed class ResidenciesHandler(CurrentUser<Principal> user, ResidencyService residencies, ResidencyFeed feed)
{
    public async Task<Results<Ok<List<ResidencyDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(Guid contactId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await residencies.ListAsync(u.Id, contactId, ct));
    }

    public async Task<Results<Ok<ResidencyDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AddAsync(Guid contactId, ResidencyRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await residencies.AddAsync(u.Id, contactId, body, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ResidencyDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ReviseAsync(Guid id, ResidencyRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await residencies.ReviseAsync(u.Id, id, body, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ResidencyDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> MoveOutAsync(Guid id, MoveOutRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await residencies.MoveOutAsync(u.Id, id, body.MovedOut, idempotencyKey, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RemoveAsync(Guid id, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await residencies.RemoveAsync(u.Id, id, idempotencyKey, ct));
    }

    public async Task<Results<Ok<List<ResidencyDto>>, UnauthorizedHttpResult>> ListVisibleAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok((await feed.VisibleAsync(u.Id, ct)).ToList());
    }

    public async Task<Results<Ok<List<ResidencyDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> MoveAsync(MoveRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await residencies.MoveAsync(u.Id, body, idempotencyKey, ct));
    }
}

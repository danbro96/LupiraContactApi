using LupiraContactApi.Core.Application;
using LupiraContactApi.Auth;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

/// <summary>The offline-client sync surface: the paged changes feed + the containers snapshot.</summary>
public sealed class SyncHandler(CurrentUser user, SyncFeed feed, AddressBookService books, ContactGroupService groups)
{
    public async Task<Results<Ok<SyncChangesResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ChangesAsync(string? since, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await feed.ChangesAsync(u.Id, since, limit, ct));
    }

    public async Task<Results<Ok<SyncContainersResponse>, ProblemHttpResult, UnauthorizedHttpResult>> ContainersAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var bookList = await books.ListAsync(u.Id, ct);
        if (!bookList.IsOk)
            return OpResultMap.OkProblem(new OpResult<SyncContainersResponse>(bookList.Status, null, bookList.Error));

        // Groups are listed per book (the service authorizes each) — the caller's book set is small.
        var allGroups = new List<ContactGroupDto>();
        foreach (var book in bookList.Value!)
        {
            var g = await groups.ListAsync(u.Id, book.Id, ct);
            if (g.IsOk) allGroups.AddRange(g.Value!);
        }

        return OpResultMap.OkProblem(OpResult<SyncContainersResponse>.Ok(new SyncContainersResponse
        {
            AddressBooks = bookList.Value!,
            Groups = allGroups,
        }));
    }
}

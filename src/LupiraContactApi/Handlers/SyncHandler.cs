using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using Lupira.Results;
using Lupira.Sync;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.PlaceEntries;
using LupiraContactApi.Core.Dtos.Relationships;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Dtos.Sync;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

/// <summary>The offline-client sync surface: the paged contact, relationship, residency and entry-code feeds, the
/// address-book and group snapshots, and the containers snapshot.</summary>
public sealed class SyncHandler(
    CurrentUser<Principal> user, SyncFeed feed, RelationshipFeed relationships, ResidencyFeed residencies, PlaceEntryFeed placeEntries,
    AddressBookService books, ContactGroupService groups)
{
    public async Task<Results<Ok<SyncPage<ContactSyncChange>>, ProblemHttpResult, UnauthorizedHttpResult>> ContactsAsync(string? since, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await feed.ContactsAsync(u.Id, since, limit, ct));
    }

    public async Task<Results<Ok<SyncPage<RelationshipDto>>, ProblemHttpResult, UnauthorizedHttpResult>> RelationshipsAsync(string? since, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await relationships.ChangesAsync(u.Id, since, limit, ct));
    }

    public async Task<Results<Ok<SyncPage<ResidencyDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ResidenciesAsync(string? since, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await residencies.ChangesAsync(u.Id, since, limit, ct));
    }

    public async Task<Results<Ok<SyncPage<PlaceEntryDto>>, ProblemHttpResult, UnauthorizedHttpResult>> PlaceEntriesAsync(string? since, int? limit, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await placeEntries.ChangesAsync(u.Id, since, limit, ct));
    }

    public async Task<Results<Ok<SyncPage<AddressBookDto>>, UnauthorizedHttpResult>> AddressBooksAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(SyncPages.Snapshot((await books.ListAsync(u.Id, ct)).Value!));
    }

    public async Task<Results<Ok<SyncPage<ContactGroupDto>>, UnauthorizedHttpResult>> GroupsAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok(SyncPages.Snapshot(await groups.ListReadableAsync(u.Id, ct)));
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

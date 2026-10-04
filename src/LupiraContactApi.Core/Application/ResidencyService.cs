using JasperFx.Events;
using LupiraContactApi.Core.Application.Results;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Residencies;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Mappers;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>Where contacts live, holiday and work: one residency per period at a place. Reading needs read on the
/// contact's book, changing needs write. Commands may carry an <c>Idempotency-Key</c> (see <see cref="Idempotency"/>).</summary>
public sealed class ResidencyService(IDocumentSession session, AccessResolver access, Idempotency idempotency)
{
    /// <summary>A contact's residencies, current ones first, then the most recent move-in first.</summary>
    public async Task<OpResult<List<ResidencyDto>>> ListAsync(Guid principalId, Guid contactId, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(contactId, ct);
        if (c is not { DeletedAt: null }) return OpResult<List<ResidencyDto>>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<List<ResidencyDto>>.Forbidden("No access to this contact.");
        var today = Today();
        return OpResult<List<ResidencyDto>>.Ok([.. (await session.ResidenciesOfAsync(contactId, ct))
            .OrderByDescending(r => r.IsActiveOn(today))
            .ThenByDescending(r => r.MovedIn?.EarliestDate())
            .Select(r => r.ToResponse())]);
    }

    public async Task<OpResult<ResidencyDto>> AddAsync(Guid principalId, Guid contactId, ResidencyRequest r, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is { } seen) return await ReplayedAsync(seen.AggregateId, ct);
        var c = await session.LoadAsync<Contact>(contactId, ct);
        if (c is not { DeletedAt: null }) return OpResult<ResidencyDto>.NotFound();
        if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ResidencyDto>.Forbidden("No write access to this contact.");
        if (ResidencyRules.Check(r.PlaceId, r.MovedIn, r.MovedOut, await session.ResidenciesOfAsync(contactId, ct)) is { } refusal)
            return OpResult<ResidencyDto>.Invalid(refusal);
        Stamp(principalId);

        var id = Guid.CreateVersion7();
        session.Events.StartStream<Residency>(id, Residency.Start(id, contactId, r.PlaceId, r.Type, Trimmed(r.Label), r.MovedIn, r.MovedOut));
        // Losing the dedup race means another delivery of this command committed first: answer with its residency.
        return await ReplayedAsync(await SaveGuardedAsync(commandId, id, 1, ct) ? id : (await idempotency.SeenAsync(commandId, ct))?.AggregateId, ct);
    }

    /// <summary>Corrects a residency as entered — place, type, label, period — wholesale.</summary>
    public async Task<OpResult<ResidencyDto>> ReviseAsync(Guid principalId, Guid id, ResidencyRequest r, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is { } seen) return await ReplayedAsync(seen.AggregateId, ct);
        var found = await FetchWritableAsync(principalId, id, ct);
        if (found.Value is not { } stream) return new(found.Status, null, found.Error);
        var residency = stream.Aggregate!;
        var others = (await session.ResidenciesOfAsync(residency.ContactId, ct)).Where(o => o.Id != id);
        if (ResidencyRules.Check(r.PlaceId, r.MovedIn, r.MovedOut, others) is { } refusal) return OpResult<ResidencyDto>.Invalid(refusal);
        return await ChangeAsync(principalId, stream, residency.Revise(r.PlaceId, r.Type, Trimmed(r.Label), r.MovedIn, r.MovedOut), commandId, ct);
    }

    public async Task<OpResult<ResidencyDto>> MoveOutAsync(Guid principalId, Guid id, FuzzyDate movedOut, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is { } seen) return await ReplayedAsync(seen.AggregateId, ct);
        var found = await FetchWritableAsync(principalId, id, ct);
        if (found.Value is not { } stream) return new(found.Status, null, found.Error);
        var residency = stream.Aggregate!;
        if (ResidencyRules.Check(residency.PlaceId, residency.MovedIn, movedOut, []) is { } refusal) return OpResult<ResidencyDto>.Invalid(refusal);
        return await ChangeAsync(principalId, stream, residency.MoveOut(movedOut), commandId, ct);
    }

    /// <summary>Erases a residency entered by mistake. One that ended is moved out of instead.</summary>
    public async Task<OpResult> RemoveAsync(Guid principalId, Guid id, Guid? commandId = null, CancellationToken ct = default)
    {
        if (await idempotency.SeenAsync(commandId, ct) is not null) return OpResult.Ok();
        var found = await FetchWritableAsync(principalId, id, ct);
        if (found.Value is not { } stream) return new(found.Status, found.Error);
        var changed = await ChangeAsync(principalId, stream, stream.Aggregate!.Remove(), commandId, ct);
        return new(changed.Status, changed.Error);
    }

    /// <summary>Several contacts move together: each one's current residencies at the place they leave end on the move-in
    /// date, and a residency at the new place starts then. All or nothing — one contact the caller can't write, or whose
    /// new residency would clash, refuses the whole move.</summary>
    public async Task<OpResult<List<ResidencyDto>>> MoveAsync(Guid principalId, MoveRequest r, Guid? commandId = null, CancellationToken ct = default)
    {
        var contactIds = r.ContactIds.Distinct().ToList();
        if (await idempotency.SeenAsync(commandId, ct) is not null) return OpResult<List<ResidencyDto>>.Ok(await StartedAtAsync(contactIds, r.ToPlaceId, ct));
        if (contactIds.Count == 0) return OpResult<List<ResidencyDto>>.Invalid("Name at least one contact to move.");
        if (!r.MovedIn.IsValid()) return OpResult<List<ResidencyDto>>.Invalid("Residency dates must be a valid year, year-month, or year-month-day.");

        var contacts = await session.LoadManyAsync<Contact>(ct, [.. contactIds]);
        foreach (var id in contactIds)
        {
            if (contacts.FirstOrDefault(c => c.Id == id) is not { DeletedAt: null } c) return OpResult<List<ResidencyDto>>.NotFound();
            if (!await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<List<ResidencyDto>>.Forbidden($"No write access to {c.DisplayName}.");
        }

        var today = Today();
        var byContact = (await session.ResidenciesOfAsync(contactIds, ct)).ToLookup(x => x.ContactId);
        var started = new List<Guid>();
        Stamp(principalId);
        foreach (var c in contacts)
        {
            var leaving = byContact[c.Id].Where(x => r.FromPlaceId == x.PlaceId && x.IsActiveOn(today)).ToList();
            foreach (var residency in leaving)
            {
                if (ResidencyRules.Check(residency.PlaceId, residency.MovedIn, r.MovedIn, []) is { } early)
                    return OpResult<List<ResidencyDto>>.Invalid($"{c.DisplayName}: {early}");
                var stream = await session.Events.FetchForWriting<Residency>(residency.Id, ct);
                stream.AppendMany(stream.Aggregate!.MoveOut(r.MovedIn));
            }

            // The rule sees the residencies left behind as already ended.
            var after = byContact[c.Id].Select(x => leaving.Contains(x) ? Ended(x, r.MovedIn) : x);
            if (ResidencyRules.Check(r.ToPlaceId, r.MovedIn, null, after) is { } refusal)
                return OpResult<List<ResidencyDto>>.Invalid($"{c.DisplayName}: {refusal}");

            var newId = Guid.CreateVersion7();
            session.Events.StartStream<Residency>(newId, Residency.Start(newId, c.Id, r.ToPlaceId, r.Type, Trimmed(r.Label), r.MovedIn, null));
            started.Add(newId);
        }

        await SaveGuardedAsync(commandId, started[0], 1, ct);
        return OpResult<List<ResidencyDto>>.Ok(await StartedAtAsync(contactIds, r.ToPlaceId, ct));
    }

    private static Residency Ended(Residency r, FuzzyDate movedOut) => new()
    {
        Id = r.Id, ContactId = r.ContactId, PlaceId = r.PlaceId, Type = r.Type, MovedIn = r.MovedIn, MovedOut = movedOut,
    };

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<List<ResidencyDto>> StartedAtAsync(IReadOnlyCollection<Guid> contactIds, Guid placeId, CancellationToken ct) =>
        [.. (await session.ResidenciesOfAsync(contactIds, ct)).Where(x => x.PlaceId == placeId && x.MovedOut is null).Select(x => x.ToResponse())];

    // The live residency, fetched for writing, when the caller can write its contact.
    private async Task<OpResult<IEventStream<Residency>>> FetchWritableAsync(Guid principalId, Guid id, CancellationToken ct)
    {
        var stream = await session.Events.FetchForWriting<Residency>(id, ct);
        if (stream.Aggregate is not { IsLive: true } residency) return OpResult<IEventStream<Residency>>.NotFound();
        var c = await session.LoadAsync<Contact>(residency.ContactId, ct);
        if (c is not { DeletedAt: null }) return OpResult<IEventStream<Residency>>.NotFound();
        return await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct)
            ? OpResult<IEventStream<Residency>>.Ok(stream)
            : OpResult<IEventStream<Residency>>.Forbidden("No write access to this contact.");
    }

    private async Task<OpResult<ResidencyDto>> ChangeAsync(Guid principalId, IEventStream<Residency> stream, IReadOnlyList<object> events, Guid? commandId, CancellationToken ct)
    {
        if (events.Count == 0) return OpResult<ResidencyDto>.Ok(stream.Aggregate!.ToResponse());
        Stamp(principalId);
        stream.AppendMany(events);
        await SaveGuardedAsync(commandId, stream.Id, (int) (stream.CurrentVersion ?? 0) + events.Count, ct);
        return await ReplayedAsync(stream.Id, ct);
    }

    private async Task<OpResult<ResidencyDto>> ReplayedAsync(Guid? id, CancellationToken ct) =>
        id is { } key && await session.LoadAsync<Residency>(key, ct) is { } r
            ? OpResult<ResidencyDto>.Ok(r.ToResponse())
            : OpResult<ResidencyDto>.NotFound();

    /// <summary>Commit staged events + the dedup ledger row in one transaction. False when the dedup race was lost.</summary>
    private async Task<bool> SaveGuardedAsync(Guid? commandId, Guid aggregateId, int resultVersion, CancellationToken ct)
    {
        idempotency.Record(commandId, aggregateId, resultVersion);
        try
        {
            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (Idempotency.IsDuplicate(ex))
        {
            return false;
        }

        return true;
    }

    // Stamp the acting principal + trace correlation onto every event in this unit of work (before SaveChangesAsync).
    private void Stamp(Guid principalId)
    {
        session.SetHeader(EventActor.HeaderKey, principalId.ToString());
        if (System.Diagnostics.Activity.Current?.TraceId is { } t) session.CorrelationId = t.ToString();
    }
}

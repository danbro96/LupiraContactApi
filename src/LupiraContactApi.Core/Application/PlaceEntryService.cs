using JasperFx.Events;
using Lupira.Results;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.PlaceEntries;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.PlaceEntries;
using LupiraContactApi.Core.Mappers;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>Door and gate codes per place. Their visibility follows the people living there: a caller sees a place's codes
/// when they can read a live contact with a current residency there, and may change them when they can write one.</summary>
public sealed class PlaceEntryService(IDocumentSession session, AccessResolver access)
{
    public async Task<OpResult<PlaceEntryDto>> GetAsync(Guid principalId, Guid placeId, CancellationToken ct = default)
    {
        if (!(await ResidentsAsync(principalId, placeId, ct)).Readable) return OpResult<PlaceEntryDto>.NotFound();
        var entry = await session.LoadAsync<PlaceEntry>(PlaceEntry.StreamIdOf(placeId), ct);
        return OpResult<PlaceEntryDto>.Ok(entry?.ToResponse() ?? new PlaceEntryDto { PlaceId = placeId, Codes = [] });
    }

    public async Task<OpResult<PlaceEntryDto>> SetCodeAsync(Guid principalId, Guid placeId, Guid codeId, SetEntryCodeRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Label) || string.IsNullOrWhiteSpace(r.Code)) return OpResult<PlaceEntryDto>.Invalid("A code needs a label and a value.");
        var stream = await session.Events.FetchForWriting<PlaceEntry>(PlaceEntry.StreamIdOf(placeId), ct);
        var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        return await ChangeAsync(principalId, placeId, stream, PlaceEntry.Set(stream.Aggregate, placeId, codeId, r.Label.Trim(), r.Code.Trim(), note), ct);
    }

    public async Task<OpResult<PlaceEntryDto>> RemoveCodeAsync(Guid principalId, Guid placeId, Guid codeId, CancellationToken ct = default)
    {
        var stream = await session.Events.FetchForWriting<PlaceEntry>(PlaceEntry.StreamIdOf(placeId), ct);
        if (stream.Aggregate?.Codes.Any(c => c.Id == codeId) is not true) return OpResult<PlaceEntryDto>.NotFound();
        return await ChangeAsync(principalId, placeId, stream, stream.Aggregate.Remove(codeId), ct);
    }

    private async Task<OpResult<PlaceEntryDto>> ChangeAsync(
        Guid principalId, Guid placeId, IEventStream<PlaceEntry> stream, IReadOnlyList<object> events, CancellationToken ct)
    {
        var residents = await ResidentsAsync(principalId, placeId, ct);
        if (!residents.Readable) return OpResult<PlaceEntryDto>.NotFound();
        if (!residents.Writable) return OpResult<PlaceEntryDto>.Forbidden("No write access to anyone living there.");
        if (events.Count > 0)
        {
            session.SetHeader(EventActor.HeaderKey, principalId.ToString());
            stream.AppendMany(events);
            await session.SaveChangesAsync(ct);
        }

        return OpResult<PlaceEntryDto>.Ok((await session.LoadAsync<PlaceEntry>(PlaceEntry.StreamIdOf(placeId), ct))!.ToResponse());
    }

    // Whether the caller can read, and write, a live contact currently living at the place.
    private async Task<(bool Readable, bool Writable)> ResidentsAsync(Guid principalId, Guid placeId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var contactIds = (await session.ResidenciesAtAsync([placeId], ct)).Where(r => r.IsActiveOn(today)).Select(r => r.ContactId).Distinct().ToArray();
        var books = (await session.LoadManyAsync<Contact>(ct, contactIds)).Where(c => c.DeletedAt is null).Select(c => c.AddressBookId).Distinct();
        var (readable, writable) = (false, false);
        foreach (var book in books)
        {
            readable |= await access.CanReadAddressBookAsync(principalId, book, ct);
            writable |= await access.CanWriteAddressBookAsync(principalId, book, ct);
        }

        return (readable, writable);
    }
}

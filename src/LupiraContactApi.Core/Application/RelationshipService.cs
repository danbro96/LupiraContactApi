using JasperFx.Events;
using Lupira.Results;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Data;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Inference;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Relationships.Events;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Mappers;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>Relationships between contacts, addressed from either one: "<c>toContactId</c> is this contact's kind". A
/// relationship is visible — and changeable — while both contacts are live and readable. Changing one needs write on at
/// least one contact's book; a label is the labelling contact's own word, so it needs write on that contact's book.</summary>
public sealed class RelationshipService(IDocumentSession session, AccessResolver access)
{
    /// <summary>States the relationship: re-stating revises it and revives an ended one. Parent/child adds are refused
    /// when they would make someone their own ancestor. Sibling relationships are stored as-is — shared-parentage
    /// siblinghood is derived on read (<see cref="KinshipInference"/>).</summary>
    public async Task<OpResult<ContactRelationEntryDto>> UpsertAsync(Guid principalId, Guid id, AddContactRelationRequest r, CancellationToken ct = default)
    {
        if (r.ToContactId == id) return OpResult<ContactRelationEntryDto>.Invalid("A contact cannot relate to itself.");
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is not { DeletedAt: null }) return OpResult<ContactRelationEntryDto>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<ContactRelationEntryDto>.Forbidden("No access to this contact.");
        var other = await session.LoadAsync<Contact>(r.ToContactId, ct);
        if (other is not { DeletedAt: null }) return OpResult<ContactRelationEntryDto>.Invalid("Related contact not found.");
        if (!await access.CanReadAddressBookAsync(principalId, other.AddressBookId, ct)) return OpResult<ContactRelationEntryDto>.Forbidden("No access to the related contact.");

        if (r.Kind is ContactRelationKind.Parent or ContactRelationKind.Child)
        {
            var (childId, parentId) = r.Kind == ContactRelationKind.Parent ? (id, other.Id) : (other.Id, id);
            if (KinshipInference.WouldCreateParentCycle(childId, parentId, await session.LiveRelationshipsAsync(ct)))
                return OpResult<ContactRelationEntryDto>.Invalid("Would create a parentage cycle.");
        }

        var key = RelationshipKey.Of(id, other.Id, r.Kind);
        var stream = await session.Events.FetchForWriting<Relationship>(key.StreamId, ct);
        var events = Relationship.Upsert(stream.Aggregate, key, id, Trimmed(r.Label), r.Since, Trimmed(r.Note));
        if (events.Count > 0)
        {
            var canWriteSelf = await access.CanWriteAddressBookAsync(principalId, c.AddressBookId, ct);
            if (!canWriteSelf && !await access.CanWriteAddressBookAsync(principalId, other.AddressBookId, ct))
                return OpResult<ContactRelationEntryDto>.Forbidden("No write access to either contact.");
            if (!canWriteSelf && events.OfType<RelationshipLabelled>().Any())
                return OpResult<ContactRelationEntryDto>.Forbidden("No write access to this contact.");
            Stamp(principalId);
            stream.AppendMany(events);
            await session.SaveChangesAsync(ct);
        }

        return OpResult<ContactRelationEntryDto>.Ok((await session.LoadAsync<Relationship>(key.StreamId, ct))!.ViewFrom(id).ToEntry(other));
    }

    /// <summary>Marks the relationship as having run its course (distinct from removal, which erases a mistake).</summary>
    public async Task<OpResult<ContactRelationEntryDto>> EndAsync(Guid principalId, Guid id, Guid toContactId, ContactRelationKind kind, DateOnly? until, CancellationToken ct = default)
    {
        var found = await FetchAsync(principalId, id, toContactId, kind, ct);
        if (found.Value is not { } at) return new(found.Status, null, found.Error);
        if (await ChangeAsync(principalId, at, at.Relationship.End(until), ct) is { } denied) return new(denied.Status, null, denied.Error);
        var saved = await session.LoadAsync<Relationship>(at.Relationship.Id, ct);
        return OpResult<ContactRelationEntryDto>.Ok(saved!.ViewFrom(id).ToEntry(at.Other));
    }

    /// <summary>Erases a relationship entered by mistake.</summary>
    public async Task<OpResult> RemoveAsync(Guid principalId, Guid id, Guid toContactId, ContactRelationKind kind, CancellationToken ct = default)
    {
        var found = await FetchAsync(principalId, id, toContactId, kind, ct);
        if (found.Value is not { } at) return new(found.Status, found.Error);
        return await ChangeAsync(principalId, at, at.Relationship.Remove(), ct) ?? OpResult.Ok();
    }

    /// <summary>A contact's relationships as seen from it, ordered by the other contact's name, then kind; ended ones flagged.
    /// With <paramref name="includeInferred"/>, kin derived from the parent/child graph follow, tagged Inferred.</summary>
    public async Task<OpResult<List<ContactRelationEntryDto>>> ListAsync(Guid principalId, Guid id, bool includeInferred = false, CancellationToken ct = default)
    {
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is not { DeletedAt: null }) return OpResult<List<ContactRelationEntryDto>>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<List<ContactRelationEntryDto>>.Forbidden("No access to this contact.");

        var books = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var views = (await session.RelationshipsOfAsync(id, ct)).Select(r => r.ViewFrom(id)).ToList();
        var otherIds = views.Select(v => v.OtherId).Distinct().ToArray();
        var others = (await session.Query<Contact>().Where(x => otherIds.Contains(x.Id) && x.DeletedAt == null).ToListAsync(ct))
            .Where(x => books.Contains(x.AddressBookId)).ToDictionary(x => x.Id);
        var entries = views.Where(v => others.ContainsKey(v.OtherId))
            .OrderBy(v => others[v.OtherId].SortName).ThenBy(v => v.Kind)
            .Select(v => v.ToEntry(others[v.OtherId]))
            .ToList();

        if (includeInferred)
        {
            // Kinship derives from the parent/child graph, which can span address books — over everything the caller can read.
            var readable = (await session.Query<Contact>().Where(x => x.DeletedAt == null).ToListAsync(ct))
                .Where(x => books.Contains(x.AddressBookId)).ToDictionary(x => x.Id);
            var known = readable.Keys.ToHashSet();
            var visible = (await session.LiveRelationshipsAsync(ct)).Where(r => known.Contains(r.Low) && known.Contains(r.High));
            entries.AddRange(KinshipInference.Infer(id, known, visible).Select(kin => new ContactRelationEntryDto
            {
                ContactId = kin.ContactId,
                DisplayName = readable[kin.ContactId].DisplayName,
                Kind = kin.Kind,
                Provenance = RelationProvenance.Inferred,
            }));
        }

        return OpResult<List<ContactRelationEntryDto>>.Ok(entries);
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // The live relationship "toContactId is id's kind", fetched for writing, with both contacts. NotFound unless both are
    // live and readable.
    private async Task<OpResult<Endpoints>> FetchAsync(Guid principalId, Guid id, Guid toContactId, ContactRelationKind kind, CancellationToken ct)
    {
        var c = await session.LoadAsync<Contact>(id, ct);
        if (c is not { DeletedAt: null }) return OpResult<Endpoints>.NotFound();
        if (!await access.CanReadAddressBookAsync(principalId, c.AddressBookId, ct)) return OpResult<Endpoints>.Forbidden("No access to this contact.");
        var other = await session.LoadAsync<Contact>(toContactId, ct);
        if (toContactId == id || other is not { DeletedAt: null } || !await access.CanReadAddressBookAsync(principalId, other.AddressBookId, ct))
            return OpResult<Endpoints>.NotFound();

        var stream = await session.Events.FetchForWriting<Relationship>(RelationshipKey.Of(id, toContactId, kind).StreamId, ct);
        return stream.Aggregate is { IsLive: true }
            ? OpResult<Endpoints>.Ok(new Endpoints(stream, c, other))
            : OpResult<Endpoints>.NotFound();
    }

    // Appends the events when the caller can write at least one contact's book; the refusal otherwise.
    private async Task<OpResult?> ChangeAsync(Guid principalId, Endpoints at, IReadOnlyList<object> events, CancellationToken ct)
    {
        if (events.Count == 0) return null;
        if (!await access.CanWriteAddressBookAsync(principalId, at.Self.AddressBookId, ct) && !await access.CanWriteAddressBookAsync(principalId, at.Other.AddressBookId, ct))
            return OpResult.Forbidden("No write access to either contact.");
        Stamp(principalId);
        at.Stream.AppendMany(events);
        await session.SaveChangesAsync(ct);
        return null;
    }

    // Stamp the acting principal + trace correlation onto every event in this unit of work (before SaveChangesAsync).
    private void Stamp(Guid principalId)
    {
        session.SetHeader(EventActor.HeaderKey, principalId.ToString());
        if (System.Diagnostics.Activity.Current?.TraceId is { } t) session.CorrelationId = t.ToString();
    }

    private sealed record Endpoints(IEventStream<Relationship> Stream, Contact Self, Contact Other)
    {
        public Relationship Relationship => Stream.Aggregate!;
    }
}

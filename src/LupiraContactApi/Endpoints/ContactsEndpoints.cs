using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Handlers;
using Microsoft.AspNetCore.Mvc;

namespace LupiraContactApi.Endpoints;

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContacts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contacts").RequireAuthorization("ApiPolicy").WithTags("Contacts");

        group.MapGet("/", (string? query, Guid? addressBookId, ContactsHandler h, CancellationToken ct) =>
                h.QueryAsync(query, addressBookId, ct))
            .WithName("SearchContacts")
            .WithSummary("Search contacts (full-text + fuzzy name match).")
            .Produces<List<ContactDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/thin", (Guid? addressBookId, double? maxScore, int? take, ContactsHandler h, CancellationToken ct) =>
                h.ThinAsync(addressBookId, maxScore, take, ct))
            .WithName("GetThinContacts")
            .WithSummary("Check-in worklist: contacts ranked thinnest-first by completeness score (< maxScore, default 1 = any contact with gaps). Kind-aware (person vs organisation card). Acknowledge an inapplicable gap field by merging metadata {\"completeness\":{\"na\":[\"organisation\"]}} so it stops counting.")
            .Produces<List<ContactDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", (CreateContactRequest body, ContactsHandler h, CancellationToken ct) => h.CreateAsync(body, ct))
            .WithName("CreateContact")
            .WithSummary("Create a contact.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/batch", (CreateContactsBatchRequest body, ContactsHandler h, CancellationToken ct) => h.CreateBatchAsync(body, ct))
            .WithName("CreateContactsBatch")
            .WithSummary("Create many contacts in one transaction (each carries its AddressBookId); returned index-for-index with the request.")
            .Produces<List<ContactDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/resolve-names", (ResolveContactsByNameRequest body, ContactsHandler h, CancellationToken ct) => h.ResolveByNameAsync(body, ct))
            .WithName("ResolveContactsByName")
            .WithSummary("Batch-match a list of names to contacts for imports: per name Matched (→contactId) / Ambiguous / NotFound, with candidate refs. Substring + normalized-name match, not phonetic.")
            .Produces<List<ContactNameMatch>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", (Guid id, ContactsHandler h, CancellationToken ct) => h.GetAsync(id, ct))
            .WithName("GetContact")
            .WithSummary("Get a single contact.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", (Guid id, ReviseContactRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.ReviseAsync(id, body, idempotencyKey, ct))
            .WithName("ReviseContact")
            .WithSummary("Update a contact (merge — provided fields overwrite/append, unmentioned fields are kept).")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/metadata", (Guid id, System.Text.Json.Nodes.JsonNode patch, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.AttachMetadataAsync(id, patch, occurredAt, idempotencyKey, ct))
            .WithName("MergeContactMetadata")
            .WithSummary("Merge arbitrary JSON metadata into a contact (top-level keys overwrite). Also the channel for completeness N/A acknowledgments: {\"completeness\":{\"na\":[\"organisation\"]}}.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", (Guid id, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.DeleteAsync(id, idempotencyKey, ct))
            .WithName("DeleteContact")
            .WithSummary("Delete a contact (soft delete + tombstone).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/circles", (Guid? focusId, ContactsHandler h, CancellationToken ct) => h.CirclesAsync(focusId, ct))
            .WithName("GetContactCircles")
            .WithSummary("Computed social circles (close family, extended family, friends, colleagues, household) around a focus contact — the caller's own linked contact unless focusId overrides. Degree is a closeness bucket (1 immediate, 2 two-generation kin, 3 cousin). Ended relations are excluded.")
            .Produces<ContactCirclesDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}/deceased", (Guid id, SetDeceasedRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.SetDeceasedAsync(id, body, idempotencyKey, ct))
            .WithName("MarkContactDeceased")
            .WithSummary("Mark a contact as deceased (idempotent; the date may be unknown). Deceased contacts stay in the kinship graph — death is not deletion.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}/deceased", (Guid id, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.ClearDeceasedAsync(id, occurredAt, idempotencyKey, ct))
            .WithName("ClearContactDeceased")
            .WithSummary("Undo a deceased marking recorded in error. (CardDAV can set but never clear deceased — clearing is API-only.)")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}/profiles", (Guid id, SetContactProfilesRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.SetProfilesAsync(id, body, idempotencyKey, ct))
            .WithName("SetContactProfiles")
            .WithSummary("Replace the contact's social/IM handles wholesale. Service names are canonicalized; well-known services (telegram, messenger, whatsapp…) get the profile URL derived from the handle. At most one preferred handle per service.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}/avatar", (Guid id, SetContactAvatarRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.SetAvatarAsync(id, body, idempotencyKey, ct))
            .WithName("SetContactAvatar")
            .WithSummary("Set (or clear, with an empty value) the contact's avatar — a URL/media id, never image bytes.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}/addresses", (Guid id, SetContactAddressesRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.SetAddressesAsync(id, body, idempotencyKey, ct))
            .WithName("SetContactAddresses")
            .WithSummary("Replace the contact's postal addresses wholesale; each entry needs a LupiraGeoApi place id (resolve the address there first — no free-text).")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}/emergency-contacts", (Guid id, SetEmergencyContactsRequest body, ContactsHandler h, CancellationToken ct) => h.SetEmergencyContactsAsync(id, body, ct))
            .WithName("SetEmergencyContacts")
            .WithSummary("Replace the contact's emergency-contact designation wholesale (order = priority, empty clears). A designation, not a relation kind — your emergency contact is usually also a spouse or friend.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}/channels", (Guid id, SetContactChannelsRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.SetChannelsAsync(id, body, idempotencyKey, ct))
            .WithName("SetContactChannels")
            .WithSummary("Replace the contact's reach channels (emails + phones) wholesale (empty clears). Unlike the merge update, this can remove a channel; values are trimmed, type tokens lowercased, duplicates dropped, at most one preferred per medium.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/{id:guid}/tags", (Guid id, SetContactTagsRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.SetTagsAsync(id, body, idempotencyKey, ct))
            .WithName("SetContactTags")
            .WithSummary("Replace the contact's tags wholesale (empty clears). Unlike the merge update, this can remove a tag; entries are trimmed and de-duplicated case-insensitively.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}/relations", (Guid id, bool? includeInferred, ContactsHandler h, CancellationToken ct) => h.ListRelationsAsync(id, includeInferred ?? false, ct))
            .WithName("ListContactRelations")
            .WithSummary("Resolved relations, both directions: each entry's kind is the other contact's role relative to this one (incoming = derived inverse). Set includeInferred=true to also return kin derived from the parent/child graph (siblings, grandparents/-children, aunts/uncles, cousins, nieces/nephews), tagged Provenance=Inferred.")
            .Produces<List<ContactRelationEntryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/relations", (Guid id, AddContactRelationRequest body, ContactsHandler h, CancellationToken ct) => h.AddRelationAsync(id, body, ct))
            .WithName("AddContactRelation")
            .WithSummary("Upsert a relation: 'toContactId is this contact's kind' (re-adding the same target+kind revises the label).")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}/relations/{toContactId:guid}", (Guid id, Guid toContactId, ContactRelationKind kind, ContactsHandler h, CancellationToken ct) => h.RemoveRelationAsync(id, toContactId, kind, ct))
            .WithName("RemoveContactRelation")
            .WithSummary("Remove the relation edge to a contact with the given kind — for edges entered by mistake. A relationship that ran its course should be ended instead.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/relations/{toContactId:guid}/end", (Guid id, Guid toContactId, EndContactRelationRequest body, ContactsHandler h, CancellationToken ct) => h.EndRelationAsync(id, toContactId, body, ct))
            .WithName("EndContactRelation")
            .WithSummary("Mark a relation as ended (ex-spouse, falling-out): the edge stays, flagged with an optional end date, and no longer asserts current kinship. Re-adding the same relation revives it.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

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
            .WithSummary("Search contacts by name: every query word is a name part, or the name text contains the query (case/diacritic-insensitive).")
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
            .WithSummary("Batch-match a list of names to contacts for imports: per name Matched (→contactId) / Ambiguous / NotFound, with candidate refs. Case/diacritic-insensitive full-name or all-tokens match, not phonetic.")
            .Produces<List<ContactNameMatch>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/lookup", (LookupContactsRequest body, ContactsHandler h, CancellationToken ct) => h.LookupAsync(body, ct))
            .WithName("LookupContacts")
            .WithSummary("Resolve contact ids to id + display name for contacts the caller can read (max 100). Unknown, deleted, or inaccessible ids are omitted — the existence check sibling services (cal-api attendees) run.")
            .Produces<List<ContactRef>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/birthdays", (ContactsHandler h, CancellationToken ct) => h.BirthdaysAsync(ct))
            .WithName("ListContactBirthdays")
            .WithSummary("Live, non-deceased contacts with a birthday across the caller's readable address books — the source of cal-api's Birthdays calendar. Year is null when only the month-day is known.")
            .Produces<List<ContactBirthdayDto>>(StatusCodes.Status200OK);

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

        group.MapPost("/{id:guid}/metadata", (Guid id, System.Text.Json.Nodes.JsonObject patch, DateTimeOffset? occurredAt, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.AttachMetadataAsync(id, patch, occurredAt, idempotencyKey, ct))
            .WithName("MergeContactMetadata")
            .WithSummary("Merge arbitrary JSON metadata into a contact (top-level keys overwrite). Also the channel for completeness N/A acknowledgments: {\"completeness\":{\"na\":[\"organisation\"]}}.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", (Guid id, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ContactsHandler h, CancellationToken ct) => h.DeleteAsync(id, idempotencyKey, ct))
            .WithName("DeleteContact")
            .WithSummary("Delete a contact (soft delete + tombstone). 409 if it is a member's own contact.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/move", (Guid id, MoveContactRequest body, ContactsHandler h, CancellationToken ct) => h.MoveAsync(id, body, ct))
            .WithName("MoveContact")
            .WithSummary("Move a contact to another address book, keeping its id (so relations, group memberships and links to it survive), content and ETag. Needs write access to both books; moving it to its current book is a no-op. The sync feed reports it deleted to readers of the old book and changed to readers of the new one.")
            .Produces<ContactDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

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
            .WithSummary("Undo a deceased marking recorded in error — the only way to clear it.")
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
            .WithSummary("The contact's relationships, identical whichever side stores them: each entry's kind is the other contact's role relative to this one and its label this contact's own name for them. Set includeInferred=true to also return kin derived from the parent/child graph (siblings, grandparents/-children, aunts/uncles, cousins, nieces/nephews), tagged Provenance=Inferred.")
            .Produces<List<ContactRelationEntryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/relations", (Guid id, AddContactRelationRequest body, ContactsHandler h, CancellationToken ct) => h.AddRelationAsync(id, body, ct))
            .WithName("AddContactRelation")
            .WithSummary("Upsert a relationship from either side: 'toContactId is this contact's kind'. The label is this contact's own name for the other; since and note are shared. Re-adding revises it and revives an ended one.")
            .Produces<ContactRelationEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}/relations/{toContactId:guid}", (Guid id, Guid toContactId, ContactRelationKind kind, ContactsHandler h, CancellationToken ct) => h.RemoveRelationAsync(id, toContactId, kind, ct))
            .WithName("RemoveContactRelation")
            .WithSummary("Remove a relationship entered by mistake, from either side. A relationship that ran its course should be ended instead.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/relations/{toContactId:guid}/end", (Guid id, Guid toContactId, EndContactRelationRequest body, ContactsHandler h, CancellationToken ct) => h.EndRelationAsync(id, toContactId, body, ct))
            .WithName("EndContactRelation")
            .WithSummary("Mark a relationship as ended, from either side (ex-spouse, falling-out): it stays, flagged with an optional end date, and no longer asserts current kinship. Re-adding it revives it.")
            .Produces<ContactRelationEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

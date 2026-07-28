using LupiraContactApi.Application;
using LupiraContactApi.Auth;
using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.Contacts;
using LupiraContactApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

public sealed class ContactsHandler(CurrentUser user, ContactService contacts)
{
    public async Task<Results<Ok<List<ContactDto>>, ProblemHttpResult, UnauthorizedHttpResult>> QueryAsync(string? query, Guid? addressBookId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await contacts.QueryAsync(u.Id, query, addressBookId, ct));
    }

    public async Task<Results<Ok<List<ContactDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ThinAsync(Guid? addressBookId, double? maxScore, int? take, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await contacts.ThinContactsAsync(u.Id, addressBookId, maxScore, take, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AttachMetadataAsync(Guid id, System.Text.Json.Nodes.JsonNode patch, DateTimeOffset? occurredAt, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.AttachMetadataAsync(u.Id, id, patch, occurredAt, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ContactDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreateContactRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await contacts.CreateAsync(u.Id, body, ct));
    }

    public async Task<Results<Ok<List<ContactDto>>, ProblemHttpResult, UnauthorizedHttpResult>> CreateBatchAsync(CreateContactsBatchRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await contacts.CreateBatchAsync(u.Id, body.Contacts, ct));
    }

    public async Task<Results<Ok<List<ContactNameMatch>>, ProblemHttpResult, UnauthorizedHttpResult>> ResolveByNameAsync(ResolveContactsByNameRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await contacts.ResolveByNameAsync(u.Id, body.Names, body.AddressBookId, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.GetAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ReviseAsync(Guid id, ReviseContactRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.ReviseAsync(u.Id, id, body, idempotencyKey, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid id, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await contacts.DeleteAsync(u.Id, id, idempotencyKey, ct));
    }

    public async Task<Results<Ok<List<ContactRelationEntryDto>>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ListRelationsAsync(Guid id, bool includeInferred, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.ListRelationsAsync(u.Id, id, includeInferred, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AddRelationAsync(Guid id, AddContactRelationRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.AddRelationAsync(u.Id, id, body, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RemoveRelationAsync(Guid id, Guid toContactId, ContactRelationKind kind, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.RemoveRelationAsync(u.Id, id, toContactId, kind, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> EndRelationAsync(Guid id, Guid toContactId, EndContactRelationRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.EndRelationAsync(u.Id, id, toContactId, body.Kind, body.Until, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetDeceasedAsync(Guid id, SetDeceasedRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetDeceasedAsync(u.Id, id, body.DeathDate, body.OccurredAt, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ClearDeceasedAsync(Guid id, DateTimeOffset? occurredAt, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.ClearDeceasedAsync(u.Id, id, occurredAt, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetProfilesAsync(Guid id, SetContactProfilesRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetProfilesAsync(u.Id, id, body.Profiles, body.OccurredAt, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetAddressesAsync(Guid id, SetContactAddressesRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetAddressesAsync(u.Id, id, body.Addresses, body.OccurredAt, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetAvatarAsync(Guid id, SetContactAvatarRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetAvatarAsync(u.Id, id, body.AvatarRef, body.OccurredAt, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetEmergencyContactsAsync(Guid id, SetEmergencyContactsRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetEmergencyContactsAsync(u.Id, id, body.ContactIds, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetChannelsAsync(Guid id, SetContactChannelsRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetChannelsAsync(u.Id, id, body.Channels, body.OccurredAt, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetTagsAsync(Guid id, SetContactTagsRequest body, Guid? idempotencyKey, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetTagsAsync(u.Id, id, body.Tags, body.OccurredAt, idempotencyKey, ct));
    }

    public async Task<Results<Ok<ContactCirclesDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> CirclesAsync(Guid? focusId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.CirclesAsync(u.Id, focusId, ct));
    }
}

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

    public async Task<Results<Ok<ContactDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(CreateContactRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await contacts.CreateAsync(u.Id, body, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> GetAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.GetAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ReviseAsync(Guid id, ReviseContactRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.ReviseAsync(u.Id, id, body, ct));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await contacts.DeleteAsync(u.Id, id, ct));
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

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetDeceasedAsync(Guid id, SetDeceasedRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetDeceasedAsync(u.Id, id, body.DeathDate, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> ClearDeceasedAsync(Guid id, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.ClearDeceasedAsync(u.Id, id, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetProfilesAsync(Guid id, SetContactProfilesRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetProfilesAsync(u.Id, id, body.Profiles, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetAddressesAsync(Guid id, SetContactAddressesRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetAddressesAsync(u.Id, id, body.Addresses, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetAvatarAsync(Guid id, SetContactAvatarRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetAvatarAsync(u.Id, id, body.AvatarRef, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetEmergencyContactsAsync(Guid id, SetEmergencyContactsRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetEmergencyContactsAsync(u.Id, id, body.ContactIds, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetChannelsAsync(Guid id, SetContactChannelsRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetChannelsAsync(u.Id, id, body.Channels, ct));
    }

    public async Task<Results<Ok<ContactDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> SetTagsAsync(Guid id, SetContactTagsRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.SetTagsAsync(u.Id, id, body.Tags, ct));
    }

    public async Task<Results<Ok<ContactCirclesDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> CirclesAsync(Guid? focusId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await contacts.CirclesAsync(u.Id, focusId, ct));
    }
}

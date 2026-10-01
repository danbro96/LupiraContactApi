using LupiraContactApi.Auth;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Application.Results;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

/// <summary>Contact groups (personal groupings + organizations) and their membership.</summary>
public sealed class ContactGroupsHandler(CurrentUser user, ContactGroupService groups)
{
    public async Task<Results<Ok<List<ContactGroupDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ListAsync(Guid addressBookId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await groups.ListAsync(u.Id, addressBookId, ct));
    }

    public async Task<Results<Ok<ContactGroupDto>, ProblemHttpResult, UnauthorizedHttpResult>> CreateAsync(Guid addressBookId, string? kind, string name, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkProblem(await groups.CreateAsync(u.Id, addressBookId, kind, name, ct));
    }

    public async Task<Results<Ok<ContactGroupDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RenameAsync(Guid groupId, string name, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await groups.RenameAsync(u.Id, groupId, name, ct));
    }

    public async Task<Results<Ok<ContactGroupDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> AddMemberAsync(Guid groupId, Guid contactId, string? role, DateOnly? since, DateOnly? until, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await groups.AddMemberAsync(u.Id, groupId, contactId, role, since, until, ct));
    }

    public async Task<Results<Ok<ContactGroupDto>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RemoveMemberAsync(Guid groupId, Guid contactId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.OkNotFoundProblem(await groups.RemoveMemberAsync(u.Id, groupId, contactId, ct));
    }

    public async Task<Results<Ok<MoveContactGroupResponse>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> MoveAsync(Guid groupId, MoveContactGroupRequest body, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        var r = await groups.MoveAsync(u.Id, groupId, body.AddressBookId, body.IncludeMembers, ct);
        return OpResultMap.OkNotFoundProblem(r.Value is { } moved
            ? OpResult<MoveContactGroupResponse>.Ok(new MoveContactGroupResponse
            {
                Group = moved.Group,
                MembersMoved = moved.Members.Count(m => m.Outcome == ContactMoveOutcome.Moved),
                MembersSkipped = moved.Members.Count(m => m.Outcome == ContactMoveOutcome.Skipped),
            })
            : new OpResult<MoveContactGroupResponse>(r.Status, null, r.Error));
    }

    public async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> DeleteAsync(Guid groupId, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return OpResultMap.NoContentNotFoundProblem(await groups.DeleteAsync(u.Id, groupId, ct));
    }
}

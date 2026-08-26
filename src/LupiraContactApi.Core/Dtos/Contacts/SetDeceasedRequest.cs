namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Marks a contact as deceased; the date may be unknown.</summary>
public sealed class SetDeceasedRequest
{
    public DateOnly? DeathDate { get; set; }
    /// <summary>Client wall-clock of the edit, for last-writer-wins conflict resolution. Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

}

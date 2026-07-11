namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Marks a contact as deceased; the date may be unknown.</summary>
public sealed class SetDeceasedRequest
{
    public DateOnly? DeathDate { get; set; }
}

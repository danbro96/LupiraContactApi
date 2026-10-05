using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Core.Dtos.Contacts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace LupiraContactApi.Handlers;

public sealed class ContactDraftsHandler(CurrentUser<Principal> user)
{
    public const int MaxFileBytes = 5 * 1024 * 1024;

    public static readonly string[] FileContentTypes = ["text/vcard", "text/x-vcard", "text/directory", "text/plain"];

    public async Task<Results<Ok<List<ContactDraftDto>>, ProblemHttpResult, UnauthorizedHttpResult>> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !FileContentTypes.Contains(mediaType.MediaType.Value, StringComparer.OrdinalIgnoreCase))
            return TypedResults.Problem($"Send the contacts file as one of: {string.Join(", ", FileContentTypes)}.", statusCode: StatusCodes.Status415UnsupportedMediaType);
        if (request.ContentLength > MaxFileBytes) return TooLarge();

        using var file = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (file.Length + read > MaxFileBytes) return TooLarge();
            file.Write(chunk, 0, read);
        }

        return OpResultMap.OkProblem(ContactDraftReader.Read(u.Id, file.ToArray(), mediaType.Charset.Value));
    }

    private static ProblemHttpResult TooLarge() =>
        TypedResults.Problem($"The contacts file exceeds {MaxFileBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status413PayloadTooLarge);
}

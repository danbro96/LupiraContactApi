using System.Text;
using Lupira.Results;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Mappers;
using LupiraContactApi.Core.Serialization;

namespace LupiraContactApi.Core.Application;

/// <summary>Reads a contacts file into contact drafts without saving anything.</summary>
public static class ContactDraftReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static OpResult<List<ContactDraftDto>> Read(Guid principalId, byte[] file, string? charset)
    {
        try
        {
            return OpResult<List<ContactDraftDto>>.Ok([.. VCardSerializer.ParseAll(Decode(file, charset)).Select(p => p.ToDraft(principalId))]);
        }
        catch (FormatException e)
        {
            return OpResult<List<ContactDraftDto>>.Invalid(e.Message);
        }
    }

    // Without a declared charset the file is UTF-8 unless its bytes say otherwise; older exporters write Latin-1.
    private static string Decode(byte[] file, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset)) return ReadText(file, VCardSerializer.CharsetEncoding(charset));
        try
        {
            return ReadText(file, StrictUtf8);
        }
        catch (DecoderFallbackException)
        {
            return ReadText(file, Encoding.Latin1);
        }
    }

    private static string ReadText(byte[] file, Encoding encoding)
    {
        using var reader = new StreamReader(new MemoryStream(file), encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

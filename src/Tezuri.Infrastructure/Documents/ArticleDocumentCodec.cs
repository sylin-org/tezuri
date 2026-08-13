using System.Security.Cryptography;
using System.Text;
using Tezuri.Domain.Documents;

namespace Tezuri.Infrastructure.Documents;

public sealed class ArticleDocumentCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public ArticleSourceEnvelopeV1 Open(
        string relativePath,
        ReadOnlyMemory<byte> sourceBytes,
        string? articleId = null,
        string? slug = null,
        string? displayTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalizedPath = NormalizeRelativePath(relativePath);
        var bytes = sourceBytes.ToArray();
        var hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
        ValidateUtf8(normalizedPath, bytes.AsSpan(hasBom ? Utf8Bom.Length : 0));

        var frontmatterEnd = FindFrontmatterEnd(normalizedPath, bytes, hasBom ? Utf8Bom.Length : 0);
        var contentStart = hasBom ? Utf8Bom.Length : 0;
        var frontmatter = Slice(bytes, contentStart, frontmatterEnd);
        var body = Slice(bytes, frontmatterEnd, bytes.Length);
        var articleSlug = slug ?? DeriveSlug(normalizedPath);
        var articleTitle = displayTitle ?? articleSlug;
        var segment = new ArticleSourceSegmentV1(
            Kind: "rich",
            Id: $"body:{body.Range.Start}:{body.Range.EndExclusive}",
            Range: body.Range,
            Source: body,
            Syntax: "gfm");

        return new ArticleSourceEnvelopeV1(
            Protocol: ArticleSourceProtocolV1.ArticleSource,
            Version: ArticleSourceProtocolV1.Version,
            Article: new ArticleDescriptorV1(
                Id: articleId ?? articleSlug,
                Slug: articleSlug,
                DisplayTitle: articleTitle,
                RelativePath: normalizedPath),
            Base: new CanonicalSourceBytesV1(
                Encoding: "utf-8",
                Bom: hasBom ? "utf-8" : "none",
                LineEndings: DetectLineEndings(bytes.AsSpan(contentStart)),
                ByteLength: bytes.LongLength,
                Sha256: HashBytes(bytes),
                Utf8Base64: Convert.ToBase64String(bytes)),
            Projection: new ArticleSourceProjectionV1(
                Frontmatter: frontmatter,
                Body: body,
                Segments: [segment]),
            Capabilities: new ArticleSourceCapabilitiesV1(
                RichEditing: "available",
                ProtectedSegmentCount: 0),
            Diagnostics: []);
    }

    public byte[] Apply(ArticleSourceEnvelopeV1 envelope, SourcePatchSetV1 patchSet)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(patchSet);
        ValidateProtocols(envelope, patchSet);

        var original = Convert.FromBase64String(envelope.Base.Utf8Base64);
        if (!StringComparer.OrdinalIgnoreCase.Equals(HashBytes(original), envelope.Base.Sha256))
        {
            throw new ArticleDocumentException("The source envelope payload does not match its SHA-256.");
        }

        if (patchSet.Operations.Count == 0)
        {
            return original;
        }

        var operations = ValidateAndOrderOperations(original, patchSet.Operations);
        using var output = new MemoryStream(original.Length);
        var cursor = 0;
        foreach (var operation in operations)
        {
            var start = checked((int)operation.Range.Start);
            var end = checked((int)operation.Range.EndExclusive);
            output.Write(original, cursor, start - cursor);
            var replacement = DecodeBase64(operation.ReplacementUtf8Base64, "replacement");
            ValidateUtf8("replacement", replacement);
            output.Write(replacement);
            cursor = end;
        }

        output.Write(original, cursor, original.Length - cursor);
        var updated = output.ToArray();
        ValidateUtf8(envelope.Article.RelativePath, updated);
        return updated;
    }

    public static string HashBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void ValidateProtocols(ArticleSourceEnvelopeV1 envelope, SourcePatchSetV1 patchSet)
    {
        if (envelope.Protocol != ArticleSourceProtocolV1.ArticleSource ||
            envelope.Version != ArticleSourceProtocolV1.Version)
        {
            throw new ArticleDocumentException("Unsupported article source protocol.");
        }

        if (patchSet.Protocol != ArticleSourceProtocolV1.SourcePatchSet ||
            patchSet.Version != ArticleSourceProtocolV1.Version)
        {
            throw new ArticleDocumentException("Unsupported source patch protocol.");
        }

        if (!StringComparer.Ordinal.Equals(envelope.Article.Id, patchSet.ArticleId) ||
            !StringComparer.Ordinal.Equals(envelope.Article.RelativePath, NormalizeRelativePath(patchSet.RelativePath)))
        {
            throw new ArticleDocumentException("The patch set targets a different article.");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(envelope.Base.Sha256, patchSet.BaseSha256))
        {
            throw new ArticleDocumentException("The patch set does not target the opened source version.");
        }
    }

    private static IReadOnlyList<ReplaceSourceRangeOperationV1> ValidateAndOrderOperations(
        byte[] source,
        IReadOnlyList<ReplaceSourceRangeOperationV1> operations)
    {
        var ordered = operations.OrderBy(operation => operation.Range.Start).ToArray();
        long previousEnd = 0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var operation = ordered[index];
            if (!StringComparer.Ordinal.Equals(operation.Kind, "replace") ||
                operation.Range.Start < 0 ||
                operation.Range.EndExclusive < operation.Range.Start ||
                operation.Range.EndExclusive > source.LongLength)
            {
                throw new ArticleDocumentException($"Operation {index} has an invalid source byte range.");
            }

            if (index > 0 && operation.Range.Start < previousEnd)
            {
                throw new ArticleDocumentException($"Operation {index} overlaps the preceding operation.");
            }

            var expected = DecodeBase64(operation.ExpectedUtf8Base64, "expected source");
            var start = checked((int)operation.Range.Start);
            var length = checked((int)operation.Range.Length);
            if (expected.Length != length || !source.AsSpan(start, length).SequenceEqual(expected))
            {
                throw new ArticleDocumentException($"Operation {index} expected bytes do not match the source.");
            }

            previousEnd = operation.Range.EndExclusive;
        }

        return ordered;
    }

    private static byte[] DecodeBase64(string value, string field)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ArticleDocumentException($"The {field} bytes are not valid base64: {exception.Message}");
        }
    }

    private static void ValidateUtf8(string path, ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArticleDocumentException(
                $"'{path}' is not valid UTF-8 and cannot be edited safely: {exception.Message}");
        }
    }

    private static int FindFrontmatterEnd(string path, byte[] bytes, int contentStart)
    {
        var content = bytes.AsSpan(contentStart);
        var openingLength = content.StartsWith("---\r\n"u8) ? 5 : content.StartsWith("---\n"u8) ? 4 : 0;
        if (openingLength == 0)
        {
            return contentStart;
        }

        var cursor = openingLength;
        while (cursor <= content.Length)
        {
            var relativeNewline = content[cursor..].IndexOf((byte)'\n');
            var lineEnd = relativeNewline < 0 ? content.Length : cursor + relativeNewline;
            var line = content[cursor..lineEnd];
            if (line.EndsWith("\r"u8))
            {
                line = line[..^1];
            }

            if (line.SequenceEqual("---"u8) || line.SequenceEqual("..."u8))
            {
                return contentStart + (relativeNewline < 0 ? lineEnd : lineEnd + 1);
            }

            if (relativeNewline < 0)
            {
                break;
            }

            cursor = lineEnd + 1;
        }

        throw new ArticleDocumentException(
            $"'{path}' starts YAML frontmatter but has no closing delimiter.");
    }

    private static SourceSliceV1 Slice(byte[] source, int start, int end)
    {
        var length = end - start;
        var bytes = source.AsSpan(start, length);
        return new SourceSliceV1(
            Range: new SourceByteRangeV1(start, end),
            Sha256: HashBytes(bytes),
            Utf8Base64: Convert.ToBase64String(bytes));
    }

    private static string DetectLineEndings(ReadOnlySpan<byte> bytes)
    {
        var lf = 0;
        var crlf = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\n')
            {
                continue;
            }

            if (index > 0 && bytes[index - 1] == (byte)'\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        return (lf, crlf) switch
        {
            (0, 0) => "none",
            ( > 0, 0) => "lf",
            (0, > 0) => "crlf",
            _ => "mixed"
        };
    }

    private static string DeriveSlug(string path)
    {
        var parent = Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar));
        return parent is null ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(parent);
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');
}

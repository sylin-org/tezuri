using System.Text;
using Tezuri.Domain.Documents;
using Tezuri.Infrastructure.Documents;

namespace Tezuri.Infrastructure.Tests.Documents;

public sealed class ArticleDocumentCodecTests
{
    private readonly ArticleDocumentCodec _codec = new();

    [Fact]
    public void NoOpPatchReturnsByteIdenticalSource()
    {
        var content = Encoding.UTF8.GetBytes("---\r\ntitle: Patina\r\n---\r\n\r\nA paragraph.\r\n");
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(content).ToArray();
        var envelope = _codec.Open("src/writing/patina/index.md", bytes);

        var actual = _codec.Apply(envelope, PatchSet(envelope, []));

        Assert.Equal(bytes, actual);
        Assert.Equal("utf-8", envelope.Base.Bom);
        Assert.Equal("crlf", envelope.Base.LineEndings);
    }

    [Fact]
    public void OneParagraphBytePatchPreservesUnicodeAndSurroundingSource()
    {
        const string source = "---\ntitle: Patina\nunknown: keep me\n---\n\nFirst.\n\nSecond 🪴 paragraph.\n\nThird.\n";
        var bytes = Encoding.UTF8.GetBytes(source);
        var envelope = _codec.Open("src/writing/patina/index.md", bytes);
        var expectedBytes = Encoding.UTF8.GetBytes("Second 🪴 paragraph.");
        var start = bytes.AsSpan().IndexOf(expectedBytes);
        var replacement = Encoding.UTF8.GetBytes("Changed 🪴 paragraph.");

        var actual = _codec.Apply(
            envelope,
            PatchSet(
                envelope,
                [new ReplaceSourceRangeOperationV1(
                    "replace",
                    new SourceByteRangeV1(start, start + expectedBytes.Length),
                    Convert.ToBase64String(expectedBytes),
                    Convert.ToBase64String(replacement),
                    "rich-edit",
                    envelope.Projection.Segments[0].Id)]));

        var expected = source.Replace("Second 🪴 paragraph.", "Changed 🪴 paragraph.", StringComparison.Ordinal);
        Assert.Equal(expected, Encoding.UTF8.GetString(actual));
        Assert.StartsWith("---\ntitle: Patina\nunknown: keep me\n---", expected, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlappingPatchesAreRejected()
    {
        var envelope = _codec.Open("article.md", "abcdef"u8.ToArray());
        var patches = PatchSet(
            envelope,
            [
                Replace(envelope, 1, 4, "x"),
                Replace(envelope, 2, 3, "y")
            ]);

        var error = Assert.Throws<ArticleDocumentException>(() => _codec.Apply(envelope, patches));

        Assert.Contains("overlaps", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpectedBytesMustMatchSource()
    {
        var envelope = _codec.Open("article.md", "abcdef"u8.ToArray());
        var operation = new ReplaceSourceRangeOperationV1(
            "replace",
            new SourceByteRangeV1(1, 3),
            Convert.ToBase64String("zz"u8),
            Convert.ToBase64String("x"u8),
            "source-edit");

        var error = Assert.Throws<ArticleDocumentException>(() =>
            _codec.Apply(envelope, PatchSet(envelope, [operation])));

        Assert.Contains("expected bytes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PatchCannotSplitAUnicodeCodePoint()
    {
        var bytes = Encoding.UTF8.GetBytes("before 🪴 after");
        var envelope = _codec.Open("article.md", bytes);
        var plant = Encoding.UTF8.GetBytes("🪴");
        var plantStart = bytes.AsSpan().IndexOf(plant);
        var operation = new ReplaceSourceRangeOperationV1(
            "replace",
            new SourceByteRangeV1(plantStart + 1, plantStart + 2),
            Convert.ToBase64String(bytes[(plantStart + 1)..(plantStart + 2)]),
            string.Empty,
            "source-edit");

        var error = Assert.Throws<ArticleDocumentException>(() =>
            _codec.Apply(envelope, PatchSet(envelope, [operation])));

        Assert.Contains("not valid UTF-8", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnterminatedFrontmatterIsRejected()
    {
        var error = Assert.Throws<ArticleDocumentException>(() =>
            _codec.Open("article.md", "---\ntitle: Never closes\n"u8.ToArray()));

        Assert.Contains("no closing delimiter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SourcePatchSetV1 PatchSet(
        ArticleSourceEnvelopeV1 envelope,
        IReadOnlyList<ReplaceSourceRangeOperationV1> operations) => new(
        ArticleSourceProtocolV1.SourcePatchSet,
        ArticleSourceProtocolV1.Version,
        envelope.Article.Id,
        envelope.Article.RelativePath,
        envelope.Base.Sha256,
        operations);

    private static ReplaceSourceRangeOperationV1 Replace(
        ArticleSourceEnvelopeV1 envelope,
        int start,
        int end,
        string replacement)
    {
        var source = Convert.FromBase64String(envelope.Base.Utf8Base64);
        return new ReplaceSourceRangeOperationV1(
            "replace",
            new SourceByteRangeV1(start, end),
            Convert.ToBase64String(source[start..end]),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(replacement)),
            "source-edit");
    }
}

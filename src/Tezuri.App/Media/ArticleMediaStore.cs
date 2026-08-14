using System.Buffers;
using System.Security.Cryptography;
using Tezuri.Media;
using Tezuri.Workspace;

namespace Tezuri.Media;

/// <summary>
/// Article-owned images. Every asset lands in <c>media/</c> beside the article that displays it,
/// named by its own SHA-256, so the same bytes uploaded twice are one file and a folder can be moved
/// or committed with everything it needs.
/// </summary>
public sealed class ArticleMediaStore(
    WorkspacePathGuard paths,
    WorkspaceSettings settings,
    AtomicFileWriter writer)
{
    private const int ReadBufferBytes = 81_920;
    private const int MaximumArticleIdCharacters = 100;
    private const int MaximumOriginalFileNameCharacters = 180;

    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly IReadOnlyDictionary<string, MediaFormat> SupportedFormats =
        new Dictionary<string, MediaFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".avif"] = new("image/avif", IsCompleteAvif),
            [".gif"] = new("image/gif", IsCompleteGif),
            [".jpeg"] = new("image/jpeg", IsCompleteJpeg),
            [".jpg"] = new("image/jpeg", IsCompleteJpeg),
            [".png"] = new("image/png", IsCompletePng),
            [".webp"] = new("image/webp", IsCompleteWebp)
        };

    private readonly MediaPolicy _policy = settings.Media;

    public Task<MediaAssetReceiptV1> IngestAsync(
        string articleId,
        string originalFileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var stream = new MemoryStream(content.ToArray(), writable: false);
        return IngestOwnedStreamAsync(stream, articleId, originalFileName, cancellationToken);
    }

    public Task<MediaAssetReceiptV1> IngestAsync(
        string articleId,
        string originalFileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        return IngestStreamAsync(articleId, originalFileName, content, cancellationToken);
    }

    public ArticleMediaFile? Find(string articleId, string storedFileName)
    {
        EnsurePortableArticleId(articleId);
        var extension = EnsureSupportedFileName(storedFileName);
        var contentHash = Path.GetFileNameWithoutExtension(storedFileName);
        if (contentHash.Length != 64 ||
            contentHash.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsAsciiLetterUpper(character)))
        {
            throw new MediaAssetException(
                MediaAssetFailure.InvalidInput,
                "Stored media names must use Tezuri's lowercase SHA-256 content name.");
        }

        var articleDirectory = EnsureArticleDirectory(articleId);
        var mediaDirectory = paths.Resolve(WorkspaceLayout.MediaFolder(articleId));
        EnsureDirectChild(articleDirectory, mediaDirectory, "The media directory must be owned by the article.");
        var absolutePath = paths.Resolve(WorkspaceLayout.MediaFile(articleId, storedFileName));
        EnsureDirectChild(mediaDirectory, absolutePath, "Media files must stay in the article media directory.");

        return File.Exists(absolutePath)
            ? new ArticleMediaFile(absolutePath, SupportedFormats[extension].MediaType)
            : null;
    }

    private async Task<MediaAssetReceiptV1> IngestOwnedStreamAsync(
        Stream content,
        string articleId,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        await using (content)
        {
            return await IngestStreamAsync(
                articleId,
                originalFileName,
                content,
                cancellationToken);
        }
    }

    private async Task<MediaAssetReceiptV1> IngestStreamAsync(
        string articleId,
        string originalFileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        EnsurePortableArticleId(articleId);
        var extension = EnsureSupportedFileName(originalFileName);

        if (!content.CanRead)
        {
            throw new MediaAssetException(
                MediaAssetFailure.InvalidInput,
                "The uploaded media stream is not readable.");
        }

        var articleDirectory = EnsureArticleDirectory(articleId);
        var mediaDirectory = paths.Resolve(WorkspaceLayout.MediaFolder(articleId));
        EnsureDirectChild(articleDirectory, mediaDirectory, "The media directory must be owned by the article.");

        var bytes = await ReadBoundedAsync(content, cancellationToken);
        var format = SupportedFormats[extension];
        if (!format.IsComplete(bytes))
        {
            throw new MediaAssetException(
                MediaAssetFailure.ExtensionMismatch,
                $"The uploaded bytes are not a complete, structurally valid '{extension}' image.");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var storedFileName = sha256 + extension;
        var relativePath = WorkspaceLayout.MediaFile(articleId, storedFileName);
        var targetPath = paths.Resolve(relativePath);
        EnsureDirectChild(mediaDirectory, targetPath, "Media files must stay in the article media directory.");

        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            if (Directory.Exists(targetPath))
            {
                throw Collision(relativePath);
            }

            if (await ContentEqualsAsync(targetPath, bytes, cancellationToken))
            {
                return CreateReceipt(
                    articleId,
                    originalFileName,
                    storedFileName,
                    relativePath,
                    format.MediaType,
                    sha256,
                    bytes.LongLength,
                    deduplicated: true);
            }

            throw Collision(relativePath);
        }

        var stagingRelativePath = WorkspaceLayout.MediaFile(
            articleId,
            $".tezuri-{Guid.NewGuid():N}.upload");
        var stagingPath = paths.Resolve(stagingRelativePath);
        EnsureDirectChild(mediaDirectory, stagingPath, "Media staging files must stay in the article media directory.");

        try
        {
            await writer.WriteAsync(stagingPath, bytes, cancellationToken);

            // Re-resolve after the asynchronous write so a link introduced during the
            // operation cannot redirect the final move outside the workspace.
            targetPath = paths.Resolve(relativePath);
            EnsureDirectChild(mediaDirectory, targetPath, "Media files must stay in the article media directory.");
            try
            {
                File.Move(stagingPath, targetPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                targetPath = paths.Resolve(relativePath);
                if (Directory.Exists(targetPath) ||
                    !await ContentEqualsAsync(targetPath, bytes, cancellationToken))
                {
                    throw Collision(relativePath);
                }

                return CreateReceipt(
                    articleId,
                    originalFileName,
                    storedFileName,
                    relativePath,
                    format.MediaType,
                    sha256,
                    bytes.LongLength,
                    deduplicated: true);
            }
        }
        finally
        {
            TryDeleteGuardedStagingFile(stagingRelativePath);
        }

        return CreateReceipt(
            articleId,
            originalFileName,
            storedFileName,
            relativePath,
            format.MediaType,
            sha256,
            bytes.LongLength,
            deduplicated: false);
    }

    private async Task<byte[]> ReadBoundedAsync(
        Stream content,
        CancellationToken cancellationToken)
    {
        if (_policy.MaximumAssetBytes <= 0)
        {
            throw new InvalidOperationException("The configured media byte limit must be positive.");
        }

        var initialCapacity = (int)Math.Min(_policy.MaximumAssetBytes, ReadBufferBytes);
        using var collected = new MemoryStream(initialCapacity);
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        try
        {
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (collected.Length > _policy.MaximumAssetBytes - read)
                {
                    throw new MediaAssetException(
                        MediaAssetFailure.TooLarge,
                        $"The media asset exceeds the configured {_policy.MaximumAssetBytes}-byte limit.");
                }

                await collected.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return collected.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private string EnsureSupportedFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > MaximumOriginalFileNameCharacters ||
            !StringComparer.Ordinal.Equals(fileName, Path.GetFileName(fileName)) ||
            fileName[0] is '.' ||
            fileName[^1] is '.' or ' ' ||
            fileName.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new MediaAssetException(
                MediaAssetFailure.InvalidInput,
                "Media file names must be portable ASCII file names without directories.");
        }

        var firstSegment = fileName.Split('.', 2)[0];
        if (ReservedFileNames.Contains(firstSegment))
        {
            throw new MediaAssetException(
                MediaAssetFailure.InvalidInput,
                $"'{fileName}' is not a portable media file name.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var allowed = _policy.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        if (!allowed || !SupportedFormats.ContainsKey(extension))
        {
            throw new MediaAssetException(
                MediaAssetFailure.UnsupportedMedia,
                $"The '{extension}' media extension is not supported by this workspace.");
        }

        return extension;
    }

    /// <summary>
    /// An article is its folder. Media can only be written into one that already exists, so an
    /// upload naming an article nobody created cannot bring a directory into being.
    /// </summary>
    private string EnsureArticleDirectory(string articleId)
    {
        var articleDirectory = paths.Resolve(WorkspaceLayout.ArticleFolder(articleId));
        if (!Directory.Exists(articleDirectory))
        {
            throw new MediaAssetException(
                MediaAssetFailure.ArticleNotFound,
                $"Article '{articleId}' does not exist in this workspace.");
        }

        return articleDirectory;
    }

    private static void EnsurePortableArticleId(string articleId)
    {
        if (string.IsNullOrWhiteSpace(articleId) ||
            articleId.Length > MaximumArticleIdCharacters ||
            !IsPortableSegment(articleId) ||
            ReservedFileNames.Contains(articleId))
        {
            throw new MediaAssetException(
                MediaAssetFailure.InvalidInput,
                "Article ids may contain only ASCII letters, digits, hyphens, and underscores.");
        }
    }

    private static bool IsPortableSegment(string value) =>
        value.Length > 0 &&
        value is not "." and not ".." &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static void EnsureDirectChild(string parent, string child, string message)
    {
        var expectedParent = Path.GetFullPath(parent);
        var actualParent = Path.GetDirectoryName(Path.GetFullPath(child));
        if (!PlatformPathComparer.Equals(expectedParent, actualParent))
        {
            throw new WorkspacePathException(child, message);
        }
    }

    private static async Task<bool> ContentEqualsAsync(
        string path,
        ReadOnlyMemory<byte> expected,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != expected.Length)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ReadBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        try
        {
            var offset = 0;
            while (offset < expected.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0 || !buffer.AsSpan(0, read).SequenceEqual(expected.Span.Slice(offset, read)))
                {
                    return false;
                }

                offset += read;
            }

            return await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) == 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void TryDeleteGuardedStagingFile(string stagingRelativePath)
    {
        try
        {
            var resolved = paths.Resolve(stagingRelativePath);
            if (File.Exists(resolved))
            {
                File.Delete(resolved);
            }
        }
        catch (WorkspacePathException)
        {
            // Refuse cleanup through a link or outside the workspace. A same-workspace
            // orphan is preferable to following an attacker-controlled path.
        }
    }

    private static MediaAssetException Collision(string relativePath) => new(
        MediaAssetFailure.Conflict,
        $"A different file already occupies deterministic media path '{relativePath}'.");

    private static MediaAssetReceiptV1 CreateReceipt(
        string articleId,
        string originalFileName,
        string storedFileName,
        string relativePath,
        string mediaType,
        string sha256,
        long byteLength,
        bool deduplicated) => new(
        MediaAssetProtocolV1.Receipt,
        MediaAssetProtocolV1.Version,
        articleId,
        originalFileName,
        storedFileName,
        relativePath,
        mediaType,
        sha256,
        byteLength,
        deduplicated);

    private static bool IsCompletePng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 45 ||
            !bytes.StartsWith(PngSignature) ||
            ReadBigEndianUInt32(bytes.Slice(8, 4)) != 13 ||
            !bytes.Slice(12, 4).SequenceEqual("IHDR"u8) ||
            ReadBigEndianUInt32(bytes.Slice(16, 4)) == 0 ||
            ReadBigEndianUInt32(bytes.Slice(20, 4)) == 0)
        {
            return false;
        }

        var offset = 8;
        var sawImageData = false;
        while (offset <= bytes.Length - 12)
        {
            var dataLength = ReadBigEndianUInt32(bytes.Slice(offset, 4));
            if (dataLength > int.MaxValue || dataLength > bytes.Length - offset - 12)
            {
                return false;
            }

            var type = bytes.Slice(offset + 4, 4);
            sawImageData |= type.SequenceEqual("IDAT"u8) && dataLength > 0;
            offset += checked((int)dataLength + 12);
            if (type.SequenceEqual("IEND"u8))
            {
                return dataLength == 0 && sawImageData && offset == bytes.Length;
            }
        }

        return false;
    }

    private static bool IsCompleteJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 &&
        bytes[0] == 0xFF &&
        bytes[1] == 0xD8 &&
        bytes[^2] == 0xFF &&
        bytes[^1] == 0xD9;

    private static bool IsCompleteGif(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 14 &&
        (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) &&
        ReadLittleEndianUInt16(bytes.Slice(6, 2)) > 0 &&
        ReadLittleEndianUInt16(bytes.Slice(8, 2)) > 0 &&
        bytes[^1] == 0x3B;

    private static bool IsCompleteWebp(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 20 &&
        bytes[..4].SequenceEqual("RIFF"u8) &&
        bytes.Slice(8, 4).SequenceEqual("WEBP"u8) &&
        ReadLittleEndianUInt32(bytes.Slice(4, 4)) == bytes.Length - 8 &&
        (bytes.Slice(12, 4).SequenceEqual("VP8 "u8) ||
         bytes.Slice(12, 4).SequenceEqual("VP8L"u8) ||
         bytes.Slice(12, 4).SequenceEqual("VP8X"u8));

    private static bool IsCompleteAvif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || !bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return false;
        }

        var boxLength = ReadBigEndianUInt32(bytes[..4]);
        if (boxLength < 16 || boxLength > bytes.Length)
        {
            return false;
        }

        var hasAvifBrand = false;
        for (var offset = 8; offset + 4 <= boxLength; offset += 4)
        {
            var brand = bytes.Slice(offset, 4);
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
            {
                hasAvifBrand = true;
                break;
            }
        }

        if (!hasAvifBrand)
        {
            return false;
        }

        var offsetAfterFtyp = (int)boxLength;
        while (offsetAfterFtyp <= bytes.Length - 8)
        {
            var nextBoxLength = ReadBigEndianUInt32(bytes.Slice(offsetAfterFtyp, 4));
            if (nextBoxLength < 8 || nextBoxLength > bytes.Length - offsetAfterFtyp)
            {
                return false;
            }

            offsetAfterFtyp += (int)nextBoxLength;
        }

        return offsetAfterFtyp == bytes.Length && bytes.Length > boxLength;
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> bytes) =>
        ((uint)bytes[0] << 24) |
        ((uint)bytes[1] << 16) |
        ((uint)bytes[2] << 8) |
        bytes[3];

    private static uint ReadLittleEndianUInt32(ReadOnlySpan<byte> bytes) =>
        bytes[0] |
        ((uint)bytes[1] << 8) |
        ((uint)bytes[2] << 16) |
        ((uint)bytes[3] << 24);

    private static ushort ReadLittleEndianUInt16(ReadOnlySpan<byte> bytes) =>
        (ushort)(bytes[0] | (bytes[1] << 8));

    private static StringComparer PlatformPathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private delegate bool ImageValidator(ReadOnlySpan<byte> bytes);

    private sealed record MediaFormat(
        string MediaType,
        ImageValidator IsComplete);
}

public sealed record ArticleMediaFile(string AbsolutePath, string MediaType);

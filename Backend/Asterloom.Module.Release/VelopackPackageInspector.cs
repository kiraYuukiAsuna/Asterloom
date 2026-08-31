using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Asterloom.Modules.Release.Model;

namespace Asterloom.Modules.Release;

internal sealed record VelopackPackageMetadata(
    string PackageId,
    string Version,
    string Channel,
    string RuntimeId,
    ReleaseArtifactKind ArtifactKind);

internal sealed class VelopackPackageInspectionException(
    string failureReason,
    string message) : Exception(message)
{
    public string FailureReason { get; } = failureReason;
}

internal static partial class VelopackPackageInspector
{
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const uint CentralDirectoryHeaderSignature = 0x02014b50;
    private const int LocalFileHeaderLength = 30;
    private const int MaximumEntriesBeforeNuspec = 64;
    private const int MaximumBytesBeforeNuspec = 16 * 1024 * 1024;
    private const int MaximumNuspecCompressedBytes = 4 * 1024 * 1024;
    private const int MaximumNuspecBytes = 1024 * 1024;

    public static async Task<VelopackPackageMetadata> InspectAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var artifactKind = ParseArtifactKind(fileName);
        var header = new byte[LocalFileHeaderLength];
        long bytesBeforeEntry = 0;
        for (var index = 0; index < MaximumEntriesBeforeNuspec; index++)
        {
            if (bytesBeforeEntry > MaximumBytesBeforeNuspec)
            {
                break;
            }
            await ReadExactlyAsync(content, header, cancellationToken);
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (signature == CentralDirectoryHeaderSignature)
            {
                throw Invalid("velopack_nuspec_missing", "The package does not contain a root NuSpec file.");
            }
            if (signature != LocalFileHeaderSignature)
            {
                throw Invalid("velopack_zip_invalid", "The package is not a supported ZIP archive.");
            }

            var flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
            var compressionMethod = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
            var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(18));
            var uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(22));
            var fileNameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
            var extraFieldLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
            if ((flags & 0x0001) != 0)
            {
                throw Invalid("velopack_zip_encrypted", "Encrypted Velopack packages are not supported.");
            }
            if ((flags & 0x0008) != 0
                || compressedSize == uint.MaxValue
                || uncompressedSize == uint.MaxValue)
            {
                throw Invalid(
                    "velopack_zip_unsupported",
                    "The Velopack NuSpec must use ordinary ZIP sizes without a data descriptor.");
            }
            if (fileNameLength is 0 or > 4_096)
            {
                throw Invalid("velopack_zip_invalid", "The package contains an invalid ZIP entry header.");
            }

            var entryNameBytes = new byte[fileNameLength];
            await ReadExactlyAsync(content, entryNameBytes, cancellationToken);
            var entryName = Encoding.UTF8.GetString(entryNameBytes).Replace('\\', '/');
            await SkipExactlyAsync(content, extraFieldLength, cancellationToken);
            var payloadOffset = bytesBeforeEntry
                + LocalFileHeaderLength
                + fileNameLength
                + extraFieldLength;
            if (!entryName.Contains('/', StringComparison.Ordinal)
                && entryName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            {
                if (compressedSize > MaximumNuspecCompressedBytes
                    || uncompressedSize > MaximumNuspecBytes)
                {
                    throw Invalid("velopack_nuspec_too_large", "The Velopack NuSpec is too large.");
                }
                var compressed = new byte[checked((int)compressedSize)];
                await ReadExactlyAsync(content, compressed, cancellationToken);
                var xml = await DecompressNuspecAsync(
                    compressed,
                    compressionMethod,
                    checked((int)uncompressedSize),
                    cancellationToken);
                return ParseNuspec(xml, entryName, artifactKind);
            }

            if (payloadOffset + compressedSize > MaximumBytesBeforeNuspec)
            {
                break;
            }
            await SkipExactlyAsync(content, compressedSize, cancellationToken);
            bytesBeforeEntry = payloadOffset + compressedSize;
        }

        throw Invalid(
            "velopack_nuspec_missing",
            "The package does not expose a root NuSpec near the start of the archive.");
    }

    private static ReleaseArtifactKind ParseArtifactKind(string fileName)
    {
        var match = ArtifactFileNamePattern().Match(fileName?.Trim() ?? string.Empty);
        if (!match.Success)
        {
            throw Invalid(
                "velopack_file_name_invalid",
                "A Velopack package file name must end in -full.nupkg or -delta.nupkg.");
        }
        return string.Equals(match.Groups[1].Value, "full", StringComparison.OrdinalIgnoreCase)
            ? ReleaseArtifactKind.Full
            : ReleaseArtifactKind.Delta;
    }

    private static VelopackPackageMetadata ParseNuspec(
        string xml,
        string entryName,
        ReleaseArtifactKind artifactKind)
    {
        XDocument document;
        try
        {
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = MaximumNuspecBytes,
                XmlResolver = null,
            });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw Invalid("velopack_nuspec_invalid", "The Velopack NuSpec XML is invalid.", exception);
        }

        var metadata = document.Descendants().FirstOrDefault(static element =>
            element.Name.LocalName == "metadata")
            ?? throw Invalid("velopack_nuspec_invalid", "The NuSpec metadata element is missing.");
        var packageId = Required(metadata, "id");
        var version = Required(metadata, "version");
        var channel = Required(metadata, "channel").ToLowerInvariant();
        var runtimeId = Required(metadata, "rid").ToLowerInvariant();
        if (!PackageIdPattern().IsMatch(packageId)
            || !string.Equals(
                Path.GetFileNameWithoutExtension(entryName),
                packageId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(
                "velopack_package_id_invalid",
                "The NuSpec package ID is invalid or does not match the NuSpec file name.");
        }
        if (!StableKeyPattern().IsMatch(channel))
        {
            throw Invalid("velopack_channel_invalid", "The NuSpec channel is invalid.");
        }
        if (!StableKeyPattern().IsMatch(runtimeId))
        {
            throw Invalid("velopack_runtime_invalid", "The NuSpec runtime identifier is invalid.");
        }

        return new(packageId, version, channel, runtimeId, artifactKind);
    }

    private static string Required(XElement metadata, string name)
    {
        var value = metadata.Elements().FirstOrDefault(element =>
            element.Name.LocalName == name)?.Value.Trim();
        return !string.IsNullOrEmpty(value)
            ? value
            : throw Invalid("velopack_nuspec_invalid", $"The NuSpec {name} value is required.");
    }

    private static async Task<string> DecompressNuspecAsync(
        byte[] compressed,
        ushort compressionMethod,
        int expectedSize,
        CancellationToken cancellationToken)
    {
        await using var input = new MemoryStream(compressed, writable: false);
        await using Stream payload = compressionMethod switch
        {
            0 => input,
            8 => new DeflateStream(input, CompressionMode.Decompress, leaveOpen: false),
            _ => throw Invalid(
                "velopack_zip_unsupported",
                "The Velopack NuSpec uses an unsupported ZIP compression method."),
        };
        await using var output = new MemoryStream(Math.Min(expectedSize, MaximumNuspecBytes));
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await payload.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaximumNuspecBytes)
            {
                throw Invalid("velopack_nuspec_too_large", "The Velopack NuSpec is too large.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (expectedSize != 0 && output.Length != expectedSize)
        {
            throw Invalid("velopack_zip_invalid", "The Velopack NuSpec size does not match its ZIP header.");
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static async Task ReadExactlyAsync(
        Stream source,
        byte[] destination,
        CancellationToken cancellationToken)
    {
        try
        {
            await source.ReadExactlyAsync(destination, cancellationToken);
        }
        catch (EndOfStreamException exception)
        {
            throw Invalid("velopack_zip_invalid", "The Velopack ZIP archive ended unexpectedly.", exception);
        }
    }

    private static async Task SkipExactlyAsync(
        Stream source,
        long count,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        while (count > 0)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)),
                cancellationToken);
            if (read == 0)
            {
                throw Invalid("velopack_zip_invalid", "The Velopack ZIP archive ended unexpectedly.");
            }
            count -= read;
        }
    }

    private static VelopackPackageInspectionException Invalid(
        string reason,
        string message,
        Exception? innerException = null) => innerException is null
        ? new(reason, message)
        : new VelopackPackageInspectionException(reason, $"{message} {innerException.Message}");

    [GeneratedRegex(@"-(full|delta)\.nupkg$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactFileNamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,198}[A-Za-z0-9])?$")]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9._-]{0,98}[a-z0-9])?$")]
    private static partial Regex StableKeyPattern();
}

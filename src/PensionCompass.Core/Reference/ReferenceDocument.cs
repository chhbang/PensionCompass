namespace PensionCompass.Core.Reference;

/// <summary>
/// Metadata for one reference PDF in the user's library. The bytes live next to this entry on disk
/// (<c>&lt;References&gt;/&lt;Id&gt;.pdf</c>); this record is what's serialized into <c>references.json</c>
/// and bound by the management UI.
/// </summary>
public sealed record ReferenceDocument
{
    /// <summary>Stable opaque id; also the on-disk file stem (<c>&lt;Id&gt;.pdf</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Original file name, shown to the user.</summary>
    public required string FileName { get; init; }

    public ReferenceCategory Category { get; init; } = ReferenceCategory.Other;

    public long SizeBytes { get; init; }

    /// <summary>When the document was added (UTC).</summary>
    public DateTime AddedUtc { get; init; }

    /// <summary>When true, this document is attached to AI rebalance requests. Default true on add.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// When true, this PDF is mirrored to the user's Google Drive (drive.appdata) so other PCs can
    /// pull it. Default FALSE — PDFs are large and consume the user's Drive quota, so cloud sync is
    /// strictly opt-in per document. Only meaningful while Google Drive sync is connected.
    /// </summary>
    public bool CloudSync { get; init; }
}

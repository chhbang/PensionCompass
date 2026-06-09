using System.Text.Json;
using System.Text.Json.Serialization;

namespace PensionCompass.Core.Reference;

/// <summary>
/// Manages the user's library of reference PDFs under a base folder: PDF bytes at
/// <c>&lt;base&gt;/References/&lt;id&gt;.pdf</c> and metadata in <c>&lt;base&gt;/References/references.json</c>.
/// Pure System.IO so it's testable against a temp directory. Whole-file index rewrite on each mutation
/// (the library is tiny — a handful of entries). Best-effort/resilient: a missing or corrupt index
/// reads as an empty library rather than throwing.
///
/// Cloud sync of the PDF bytes (drive.appdata) is a deliberate follow-up — see
/// doc/pdf-reference-attachment-plan.md §D3. This service is local-only for now.
/// </summary>
public sealed class ReferenceLibraryService
{
    public const string FolderName = "References";
    private const string IndexFileName = "references.json";

    private readonly string _folder;
    private readonly string _indexPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ReferenceLibraryService(string baseFolder)
    {
        _folder = Path.Combine(baseFolder, FolderName);
        _indexPath = Path.Combine(_folder, IndexFileName);
    }

    public string FolderPath => _folder;

    /// <summary>All entries, oldest-added first. Empty when nothing's been added or the index is corrupt.</summary>
    public IReadOnlyList<ReferenceDocument> List()
    {
        try
        {
            if (!File.Exists(_indexPath)) return [];
            var bytes = File.ReadAllBytes(_indexPath);
            var items = JsonSerializer.Deserialize<List<ReferenceDocument>>(bytes, JsonOptions);
            return items is null ? [] : items.OrderBy(d => d.AddedUtc).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Adds a PDF (writes bytes + index entry) and returns the new entry. Enabled by default.</summary>
    public ReferenceDocument Add(string fileName, byte[] content, ReferenceCategory category)
    {
        Directory.CreateDirectory(_folder);
        var id = Guid.NewGuid().ToString("N");
        var doc = new ReferenceDocument
        {
            Id = id,
            FileName = SanitizeName(fileName),
            Category = category,
            SizeBytes = content.LongLength,
            AddedUtc = DateTime.UtcNow,
            Enabled = true,
        };
        File.WriteAllBytes(PdfPath(id), content);
        var list = List().ToList();
        list.Add(doc);
        Save(list);
        return doc;
    }

    public void Remove(string id)
    {
        Save(List().Where(d => d.Id != id).ToList());
        try
        {
            var p = PdfPath(id);
            if (File.Exists(p)) File.Delete(p);
        }
        catch { /* best-effort — index entry is already gone */ }
    }

    public void SetEnabled(string id, bool enabled) => Mutate(id, d => d with { Enabled = enabled });

    public void SetCategory(string id, ReferenceCategory category) => Mutate(id, d => d with { Category = category });

    public void SetCloudSync(string id, bool cloudSync) => Mutate(id, d => d with { CloudSync = cloudSync });

    /// <summary>
    /// Adds a document with a CALLER-SUPPLIED id (vs <see cref="Add"/> which mints a new one), used to
    /// pull a cloud document down to this device keeping the same id across PCs. No-op (returns null)
    /// if the id already exists locally.
    /// </summary>
    public ReferenceDocument? Import(ReferenceDocument doc, byte[] content)
    {
        var list = List().ToList();
        if (list.Any(d => d.Id == doc.Id)) return null;
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(PdfPath(doc.Id), content);
        var imported = doc with { Enabled = true };
        list.Add(imported);
        Save(list);
        return imported;
    }

    /// <summary>Serializes one document's metadata for a cloud sidecar (<c>&lt;id&gt;.json</c>).</summary>
    public static byte[] SerializeMetadata(ReferenceDocument doc)
        => JsonSerializer.SerializeToUtf8Bytes(doc, JsonOptions);

    /// <summary>Parses a cloud sidecar's metadata; null on failure.</summary>
    public static ReferenceDocument? DeserializeMetadata(byte[] bytes)
    {
        try { return JsonSerializer.Deserialize<ReferenceDocument>(bytes, JsonOptions); }
        catch { return null; }
    }

    /// <summary>Reads a document's PDF bytes, or null if missing/unreadable.</summary>
    public byte[]? ReadBytes(string id)
    {
        try
        {
            var p = PdfPath(id);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }
        catch
        {
            return null;
        }
    }

    private void Mutate(string id, Func<ReferenceDocument, ReferenceDocument> transform)
    {
        var list = List().ToList();
        var idx = list.FindIndex(d => d.Id == id);
        if (idx < 0) return;
        list[idx] = transform(list[idx]);
        Save(list);
    }

    private void Save(IReadOnlyList<ReferenceDocument> list)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(_indexPath, JsonSerializer.SerializeToUtf8Bytes(list, JsonOptions));
    }

    private string PdfPath(string id) => Path.Combine(_folder, id + ".pdf");

    private static string SanitizeName(string name)
    {
        var stripped = Path.GetFileName(name);
        return string.IsNullOrWhiteSpace(stripped) ? "document.pdf" : stripped;
    }
}

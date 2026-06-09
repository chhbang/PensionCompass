using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.Reference;
using PensionCompass.Services;

namespace PensionCompass.ViewModels;

/// <summary>
/// Manages the reference-PDF library screen: list/add/categorize/enable/delete. Mutations write
/// through <see cref="ReferenceLibraryService"/> immediately (no separate save step), matching the
/// app's auto-persist convention.
/// </summary>
public sealed partial class ReferenceLibraryViewModel : ObservableObject
{
    private ReferenceLibraryService Library => AppState.Instance.References;

    public ObservableCollection<ReferenceRowViewModel> Documents { get; } = [];

    /// <summary>Category applied to the next added PDF (0=FundGuide, 1=MarketReport, 2=Other).</summary>
    [ObservableProperty]
    private int _newCategoryIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocuments))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private int _documentCount;

    public bool HasDocuments => DocumentCount > 0;

    /// <summary>True when Google Drive is connected, so the per-document cloud-sync toggle is usable.</summary>
    public bool CloudAvailable => AppState.Instance.IsReferenceCloudAvailable;

    /// <summary>Explains the cloud-sync option and its Google-Drive quota cost (or how to enable it).</summary>
    public string CloudCaption => CloudAvailable
        ? "각 자료의 \"클라우드\"를 켜면 해당 PDF가 본인 Google Drive의 앱 전용 폴더에 저장되어 다른 PC에서도 받아볼 수 있습니다. PDF는 용량이 커서(예: 10~20MB) Google 계정 저장공간을 사용하니, 꼭 필요한 자료만 켜시길 권장합니다. (기본값: 꺼짐 — 켜기 전엔 이 PC에만 저장)"
        : "참고 자료의 클라우드 동기화는 환경 설정에서 Google 계정을 연결하면 사용할 수 있습니다. 연결 전에는 PDF가 이 PC에만 저장됩니다.";

    public string SummaryText
    {
        get
        {
            if (DocumentCount == 0)
                return "등록된 참고 자료가 없습니다. PDF를 추가하면 AI 리밸런싱 시 함께 전달할 수 있습니다.";
            var enabled = Documents.Count(d => d.Enabled);
            var totalBytes = Documents.Where(d => d.Enabled).Sum(d => d.SizeBytes);
            var cloud = Documents.Count(d => d.CloudSync);
            var cloudBytes = Documents.Where(d => d.CloudSync).Sum(d => d.SizeBytes);
            var cloudPart = cloud > 0 ? $"  ·  클라우드 동기 {cloud}개 ({FormatSize(cloudBytes)})" : string.Empty;
            return $"총 {DocumentCount}개  ·  AI 첨부 {enabled}개 (합계 {FormatSize(totalBytes)}){cloudPart}";
        }
    }

    public void Refresh()
    {
        // Bring down any reference PDFs another PC opted to sync (Google Drive mode) before listing.
        AppState.Instance.PullCloudReferencesToLocal();

        var cloudAvailable = CloudAvailable;
        Documents.Clear();
        foreach (var doc in Library.List())
            Documents.Add(new ReferenceRowViewModel(doc, Library, OnRowChanged, cloudAvailable));
        DocumentCount = Documents.Count;
        OnPropertyChanged(nameof(CloudAvailable));
        OnPropertyChanged(nameof(CloudCaption));
    }

    public void Add(string fileName, byte[] content)
    {
        Library.Add(fileName, content, (ReferenceCategory)NewCategoryIndex);
        Refresh();
    }

    public void Remove(ReferenceRowViewModel row)
    {
        // RemoveReference also deletes the cloud copy if the doc was synced.
        AppState.Instance.RemoveReference(row.Id);
        Refresh();
    }

    private void OnRowChanged()
    {
        // A row's enabled/category toggle changed → recompute the summary line.
        OnPropertyChanged(nameof(SummaryText));
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes} B";
    }
}

/// <summary>One reference document row. Category/enabled edits persist immediately.</summary>
public sealed partial class ReferenceRowViewModel : ObservableObject
{
    private readonly ReferenceLibraryService _library;
    private readonly Action _onChanged;

    public ReferenceRowViewModel(ReferenceDocument doc, ReferenceLibraryService library, Action onChanged, bool cloudAvailable)
    {
        _library = library;
        _onChanged = onChanged;
        Id = doc.Id;
        FileName = doc.FileName;
        SizeBytes = doc.SizeBytes;
        AddedText = doc.AddedUtc.ToLocalTime().ToString("yyyy-MM-dd");
        CloudAvailable = cloudAvailable;
        _categoryIndex = (int)doc.Category;
        _enabled = doc.Enabled;
        _cloudSync = doc.CloudSync;
    }

    public string Id { get; }
    public string FileName { get; }
    public long SizeBytes { get; }
    public string SizeText => ReferenceLibraryViewModel.FormatSize(SizeBytes);
    public string AddedText { get; }

    /// <summary>Whether the cloud toggle is usable (Google connected). Bound to the checkbox's IsEnabled.</summary>
    public bool CloudAvailable { get; }

    /// <summary>0=FundGuide, 1=MarketReport, 2=Other — matches the enum order and the combo items.</summary>
    [ObservableProperty]
    private int _categoryIndex;

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private bool _cloudSync;

    partial void OnCategoryIndexChanged(int value)
    {
        if (value < 0) return; // ignore ComboBox -1 writeback during re-layout
        _library.SetCategory(Id, (ReferenceCategory)value);
        _onChanged();
    }

    partial void OnEnabledChanged(bool value)
    {
        _library.SetEnabled(Id, value);
        _onChanged();
    }

    partial void OnCloudSyncChanged(bool value)
    {
        // Routes through AppState so the PDF is uploaded/removed on Drive (Google mode), not just flagged.
        AppState.Instance.SetReferenceCloudSync(Id, value);
        _onChanged();
    }
}

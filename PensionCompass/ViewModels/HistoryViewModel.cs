using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.Ai;
using PensionCompass.Core.History;
using PensionCompass.Core.Models;
using PensionCompass.Core.Pdf;
using PensionCompass.Services;

namespace PensionCompass.ViewModels;

/// <summary>
/// Drives the 이력 (history) screen. Lists every saved <see cref="RebalanceSession"/> across
/// LocalState\History\ and the configured sync folder, lets the user pick one to read,
/// re-export to PDF, or delete. The "다음 리밸런싱에 이 추천 참고하기" hand-off works by
/// stashing the selected entry's path on <see cref="AppState"/> and navigating; the AI
/// Rebalance VM picks it up on its next refresh.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<HistoryEntryRow> Entries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedSummary))]
    private HistoryEntryRow? _selectedEntry;

    /// <summary>The full session loaded for the current selection — used to render markdown.</summary>
    [ObservableProperty]
    private RebalanceSession? _loadedSession;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasSelection => SelectedEntry != null;

    public string SelectedSummary
    {
        get
        {
            if (SelectedEntry is null) return "(왼쪽에서 회차를 선택하세요)";
            var m = SelectedEntry.Meta;
            return $"{m.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {m.ProviderName} ({m.ModelId}, 사고 수준 {m.ThinkingLevel.ToKoreanLabel()})  ·  보유 {m.HoldingsCount}개, 총 {m.TotalAmount:N0}원";
        }
    }

    public void Refresh()
    {
        Entries.Clear();
        try
        {
            // Listing returns metadata only; load each session's full body so we can compute
            // the period comparison between adjacent entries (and current account state for
            // the newest one). Sessions are small (<100 KB) so the I/O is negligible.
            var rawEntries = RebalanceHistoryStore.List(AppState.Instance.CandidateHistoryRoots());
            var loaded = rawEntries
                .Select(e => (Entry: e, Session: RebalanceHistoryStore.Load(e.FilePath)))
                .Where(x => x.Session is not null)
                .ToList();

            // entries are sorted newest-first. For each, compare to the next-newer (i-1) session,
            // or to the current live account when it's the newest (i=0).
            for (int i = 0; i < loaded.Count; i++)
            {
                var (entry, session) = loaded[i];
                PeriodComparison? comp;
                if (i == 0)
                {
                    comp = PeriodComparisonCalculator.Compare(
                        session!, AppState.Instance.Account, DateTime.Now);
                }
                else
                {
                    comp = PeriodComparisonCalculator.Compare(session!, loaded[i - 1].Session!);
                }
                Entries.Add(new HistoryEntryRow(entry, comp));
            }

            StatusMessage = Entries.Count switch
            {
                0 => "저장된 이력이 없습니다. AI 리밸런싱 화면에서 추천을 받은 뒤 \"이력에 저장\" 버튼을 누르면 여기에 쌓입니다.",
                1 => "이력 1건",
                _ => $"이력 {Entries.Count}건",
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"이력 목록 읽기 실패: {ex.Message}";
        }
    }

    partial void OnSelectedEntryChanged(HistoryEntryRow? value)
    {
        if (value is null)
        {
            LoadedSession = null;
            return;
        }
        LoadedSession = RebalanceHistoryStore.Load(value.FilePath);
        if (LoadedSession is null)
            StatusMessage = "선택한 이력 파일을 읽을 수 없습니다 (이동·삭제·손상되었을 수 있습니다).";
    }

    /// <summary>
    /// Loads a session from an arbitrary path on disk (PC 어디서든 불러오기 시나리오).
    /// Inserts it into the listing if it's not already there so the user can select it. The
    /// imported row carries no period comparison (it stands alone outside the time-ordered list).
    /// </summary>
    public void LoadFromExternalFile(string path)
    {
        if (!File.Exists(path)) return;
        var session = RebalanceHistoryStore.Load(path);
        if (session is null)
        {
            StatusMessage = "파일을 RebalanceSession JSON으로 읽을 수 없습니다.";
            return;
        }

        var entry = new RebalanceSessionEntry(Path.GetFullPath(path), session.Meta);
        var existing = Entries.FirstOrDefaultMatching(entry.FilePath);
        if (existing is null)
        {
            existing = new HistoryEntryRow(entry, comparison: null);
            Entries.Insert(0, existing);
        }
        SelectedEntry = existing;
    }

    public async Task ExportSelectedToPdfAsync(string filePath)
    {
        if (LoadedSession is not { } session) return;
        try
        {
            var report = new PdfReport(
                GeneratedAt: session.Meta.Timestamp,
                ProviderName: session.Meta.ProviderName,
                ModelId: session.Meta.ModelId,
                Account: session.Account,
                AiResponseMarkdown: session.RecommendationMarkdown);
            await Task.Run(() => PdfExporter.Export(filePath, report));
            StatusMessage = $"PDF 저장 완료: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF 저장 오류: {ex.Message}";
        }
    }

    public bool DeleteSelected()
    {
        if (SelectedEntry is null) return false;
        var ok = RebalanceHistoryStore.Delete(SelectedEntry.FilePath);
        if (ok)
        {
            Entries.Remove(SelectedEntry);
            SelectedEntry = null;
            LoadedSession = null;
            StatusMessage = "삭제했습니다.";
        }
        else
        {
            StatusMessage = "삭제 실패. 파일이 다른 프로세스에 잠겨 있거나 권한 문제일 수 있습니다.";
        }
        return ok;
    }
}

internal static class HistoryListExtensions
{
    public static HistoryEntryRow? FirstOrDefaultMatching(this ObservableCollection<HistoryEntryRow> list, string path)
    {
        foreach (var item in list)
            if (string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase))
                return item;
        return null;
    }
}

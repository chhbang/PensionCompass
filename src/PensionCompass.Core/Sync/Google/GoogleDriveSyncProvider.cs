using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace PensionCompass.Core.Sync.Google;

/// <summary>
/// <see cref="ISyncProvider"/> backed by the user's own Google Drive — files live in the special
/// <c>appDataFolder</c> space which is private to this OAuth client + Google account combo (the
/// user can't see them in the Drive web UI, and other apps with different client IDs can't read
/// them). Powered by the official <c>Google.Apis.Drive.v3</c> SDK to inherit OAuth (PKCE
/// loopback), token refresh, retries, and quota handling for free.
///
/// Threading model:
/// - <see cref="GetModifiedTime"/> / <see cref="Read"/>: sync-over-async via <c>Task.Run + GetAwaiter().GetResult</c>.
///   Called only at process startup so blocking briefly is fine.
/// - <see cref="Write"/> / <see cref="Delete"/>: enqueued to an internal channel and processed
///   on a background task — fire-and-forget so UI thread never waits on a network round-trip.
///
/// Lifecycle:
/// - First call to any method triggers the OAuth flow (browser pops up). Tokens are persisted
///   via the <see cref="IDataStore"/> the caller supplies (PasswordVault on Windows).
/// - Subsequent calls reuse the cached <see cref="DriveService"/> in-process; tokens auto-refresh
///   when the access token expires.
/// </summary>
public sealed class GoogleDriveSyncProvider : ISyncProvider, IAsyncDisposable
{
    private const string AppDataFolderId = "appDataFolder";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string DefaultFileMimeType = "application/octet-stream";
    private const string ApplicationName = "PensionCompass";

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly IDataStore _tokenStore;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private DriveService? _drive;

    private readonly ConcurrentDictionary<string, string> _fileIdCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _folderIdCache = new(StringComparer.Ordinal);

    private readonly Channel<WriteOp> _writeQueue;
    private readonly Task _writeProcessor;
    private readonly CancellationTokenSource _shutdownCts = new();

    public GoogleDriveSyncProvider(string clientId, string clientSecret, IDataStore tokenStore)
    {
        _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _clientSecret = clientSecret ?? throw new ArgumentNullException(nameof(clientSecret));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _writeQueue = Channel.CreateUnbounded<WriteOp>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _writeProcessor = Task.Run(ProcessWriteQueueAsync);
    }

    public bool IsConfigured => true;

    /// <summary>
    /// Tracks the last write attempt's outcome so a Settings UI can surface "synced N seconds ago"
    /// or "last error: ...". Updated by the background processor; safe to read from any thread.
    /// </summary>
    public DateTime? LastSuccessfulSyncUtc { get; private set; }
    public string? LastErrorMessage { get; private set; }

    public DateTime? GetModifiedTime(string fileName)
        => RunSync(() => GetModifiedTimeInternalAsync(fileName));

    public byte[]? Read(string fileName)
        => RunSync(() => ReadInternalAsync(fileName));

    public void Write(string fileName, byte[] content)
    {
        // Fire-and-forget via channel; internal worker uploads in background. We don't surface
        // the immediate enqueue result — full-bounded channel could fail to write but ours is
        // unbounded so TryWrite always succeeds in practice.
        _writeQueue.Writer.TryWrite(new WriteOp(fileName, content));
    }

    public void Delete(string fileName)
    {
        // Best-effort fire-and-forget. Errors logged into LastErrorMessage but otherwise swallowed.
        _ = Task.Run(async () =>
        {
            try
            {
                var id = await ResolveFileIdAsync(fileName).ConfigureAwait(false);
                if (id is null) return;
                var drive = await EnsureDriveAsync().ConfigureAwait(false);
                await drive.Files.Delete(id).ExecuteAsync().ConfigureAwait(false);
                _fileIdCache.TryRemove(fileName, out _);
                LastSuccessfulSyncUtc = DateTime.UtcNow;
                LastErrorMessage = null;
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"삭제 실패: {ex.Message}";
            }
        });
    }

    /// <summary>
    /// Forces immediate authorization (browser flow if no cached tokens). Useful for the Settings
    /// "Connect" button: the caller wants to know whether sign-in succeeded right now, not when
    /// the first save happens to fire.
    /// </summary>
    public async Task<DriveService> InitializeAsync(CancellationToken ct = default)
        => await EnsureDriveAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Awaits any pending background writes — used at app shutdown to avoid losing the last save,
    /// and in tests for deterministic assertions.
    /// </summary>
    public async Task FlushPendingWritesAsync(TimeSpan? timeout = null)
    {
        // Snapshot the current queue length and wait for the processor to catch up. We don't
        // close the writer because the provider may still be in active use.
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (_writeQueue.Reader.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50).ConfigureAwait(false);
    }

    private async Task<DriveService> EnsureDriveAsync(CancellationToken ct = default)
    {
        if (_drive is not null) return _drive;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_drive is not null) return _drive;

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets { ClientId = _clientId, ClientSecret = _clientSecret },
                new[] { DriveService.ScopeConstants.DriveAppdata },
                user: "default",
                taskCancellationToken: ct,
                dataStore: _tokenStore).ConfigureAwait(false);

            _drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
            return _drive;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<DateTime?> GetModifiedTimeInternalAsync(string fileName)
    {
        var id = await ResolveFileIdAsync(fileName).ConfigureAwait(false);
        if (id is null) return null;
        var drive = await EnsureDriveAsync().ConfigureAwait(false);
        var req = drive.Files.Get(id);
        req.Fields = "modifiedTime";
        var file = await req.ExecuteAsync().ConfigureAwait(false);
        if (file.ModifiedTimeRaw is null) return null;
        return DateTime.Parse(file.ModifiedTimeRaw, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    private async Task<byte[]?> ReadInternalAsync(string fileName)
    {
        var id = await ResolveFileIdAsync(fileName).ConfigureAwait(false);
        if (id is null) return null;
        var drive = await EnsureDriveAsync().ConfigureAwait(false);
        using var ms = new MemoryStream();
        await drive.Files.Get(id).DownloadAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }

    private async Task ProcessWriteQueueAsync()
    {
        try
        {
            await foreach (var op in _writeQueue.Reader.ReadAllAsync(_shutdownCts.Token).ConfigureAwait(false))
            {
                try
                {
                    await WriteInternalAsync(op.FileName, op.Content).ConfigureAwait(false);
                    LastSuccessfulSyncUtc = DateTime.UtcNow;
                    LastErrorMessage = null;
                }
                catch (Exception ex)
                {
                    LastErrorMessage = $"업로드 실패 ({op.FileName}): {ex.Message}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown — drain quietly
        }
    }

    private async Task WriteInternalAsync(string fileName, byte[] content)
    {
        var (folderId, name) = await ParsePathAsync(fileName).ConfigureAwait(false);
        var existingId = await ResolveFileIdAsync(fileName).ConfigureAwait(false);
        var drive = await EnsureDriveAsync().ConfigureAwait(false);

        using var ms = new MemoryStream(content);
        if (existingId is not null)
        {
            // Update content of existing file. Empty metadata = keep name/parents unchanged.
            var meta = new DriveFile();
            var req = drive.Files.Update(meta, existingId, ms, DefaultFileMimeType);
            await req.UploadAsync(_shutdownCts.Token).ConfigureAwait(false);
        }
        else
        {
            var meta = new DriveFile
            {
                Name = name,
                Parents = new List<string> { folderId },
            };
            var req = drive.Files.Create(meta, ms, DefaultFileMimeType);
            req.Fields = "id";
            await req.UploadAsync(_shutdownCts.Token).ConfigureAwait(false);
            if (req.ResponseBody is { } resp)
                _fileIdCache[fileName] = resp.Id;
        }
    }

    /// <summary>
    /// Splits a logical name like <c>"History/2026-05-10_xyz.json"</c> into the parent folder ID
    /// (creating the folder lazily if needed) and the bare file name. <c>"account.json"</c>
    /// without a slash returns <c>(appDataFolder, "account.json")</c>.
    /// </summary>
    private async Task<(string FolderId, string FileName)> ParsePathAsync(string virtualPath)
    {
        var idx = virtualPath.LastIndexOf('/');
        if (idx < 0) return (AppDataFolderId, virtualPath);
        var folderName = virtualPath.Substring(0, idx);
        var fileName = virtualPath.Substring(idx + 1);
        var folderId = await ResolveOrCreateFolderIdAsync(folderName).ConfigureAwait(false);
        return (folderId, fileName);
    }

    private async Task<string?> ResolveFileIdAsync(string virtualPath)
    {
        if (_fileIdCache.TryGetValue(virtualPath, out var cached)) return cached;
        var (folderId, name) = await ParsePathAsync(virtualPath).ConfigureAwait(false);
        var drive = await EnsureDriveAsync().ConfigureAwait(false);

        var req = drive.Files.List();
        req.Spaces = "appDataFolder";
        req.Q = $"name = '{EscapeQ(name)}' and '{folderId}' in parents and trashed = false";
        req.Fields = "files(id, name)";
        var result = await req.ExecuteAsync().ConfigureAwait(false);

        var found = result.Files?.FirstOrDefault();
        if (found is not null) _fileIdCache[virtualPath] = found.Id;
        return found?.Id;
    }

    private async Task<string> ResolveOrCreateFolderIdAsync(string folderName)
    {
        if (_folderIdCache.TryGetValue(folderName, out var cached)) return cached;
        var drive = await EnsureDriveAsync().ConfigureAwait(false);

        var req = drive.Files.List();
        req.Spaces = "appDataFolder";
        req.Q = $"name = '{EscapeQ(folderName)}' and '{AppDataFolderId}' in parents and mimeType = '{FolderMimeType}' and trashed = false";
        req.Fields = "files(id)";
        var result = await req.ExecuteAsync().ConfigureAwait(false);

        var existing = result.Files?.FirstOrDefault();
        if (existing is not null)
        {
            _folderIdCache[folderName] = existing.Id;
            return existing.Id;
        }

        var meta = new DriveFile
        {
            Name = folderName,
            MimeType = FolderMimeType,
            Parents = new List<string> { AppDataFolderId },
        };
        var createReq = drive.Files.Create(meta);
        createReq.Fields = "id";
        var created = await createReq.ExecuteAsync().ConfigureAwait(false);
        _folderIdCache[folderName] = created.Id;
        return created.Id;
    }

    /// <summary>
    /// Escapes a single quote inside a Drive query string. Drive's query language uses
    /// backslash-escaped single quotes within string literals.
    /// </summary>
    private static string EscapeQ(string s) => s.Replace("'", "\\'");

    /// <summary>
    /// Runs an async operation synchronously on a background thread to escape any sync context
    /// the caller is on (e.g. the WinUI UI thread). Returns the inner exception if the task
    /// faulted, except we swallow exceptions here and return null since callers of this provider
    /// treat null as "unavailable / not yet present".
    /// </summary>
    private static T? RunSync<T>(Func<Task<T?>> work) where T : class
    {
        try
        {
            return Task.Run(work).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? RunSync(Func<Task<DateTime?>> work)
    {
        try
        {
            return Task.Run(work).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _writeQueue.Writer.TryComplete();
        try { await _writeProcessor.ConfigureAwait(false); } catch { /* shutting down */ }
        _shutdownCts.Cancel();
        _drive?.Dispose();
        _initLock.Dispose();
        _shutdownCts.Dispose();
    }

    private record WriteOp(string FileName, byte[] Content);
}

using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.Web.Services;

public sealed record SkillRecoveryPdfUploadInfo(Guid UploadId, string FileName, long ByteLength, string Sha256, DateTimeOffset ExpiresAt);

/// <summary>Short-lived, session/source-bound validated PDFs. No database writes occur here.</summary>
public sealed class SkillRecoveryPdfUploads : IDisposable
{
    private readonly DeveloperRoleService _developers;
    private readonly FileStorageOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SkillRecoveryPdfUploads> _logger;
    private readonly int _globalLimit, _sessionLimit;
    private readonly long _byteLimit;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly HashSet<Entry> _owned = new();
    private readonly List<Reservation> _pending = new();
    private readonly ITimer _timer;
    private bool _disposed;

    public SkillRecoveryPdfUploads(DeveloperRoleService developers, FileStorageOptions options, TimeProvider clock,
        ILogger<SkillRecoveryPdfUploads>? logger = null)
        : this(developers, options, clock, logger, 16, 2, 256L * 1024 * 1024) { }

    internal SkillRecoveryPdfUploads(DeveloperRoleService developers, FileStorageOptions options, TimeProvider clock,
        ILogger<SkillRecoveryPdfUploads>? logger, int globalLimit, int sessionLimit, long byteLimit)
    {
        _developers = developers; _options = options; _clock = clock;
        _logger = logger ?? NullLogger<SkillRecoveryPdfUploads>.Instance;
        if (globalLimit < 1 || sessionLimit < 1 || byteLimit < 1) throw new ArgumentOutOfRangeException(nameof(globalLimit));
        _globalLimit = globalLimit; _sessionLimit = sessionLimit; _byteLimit = byteLimit;
        _timer = clock.CreateTimer(_ => Cleanup(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task<SkillRecoveryPdfUploadInfo> StageAsync(string actor, string sessionId, SkillRecoveryKey key,
        IFormFile upload, CancellationToken ct)
    {
        Validate(actor, sessionId, key);
        ct.ThrowIfCancellationRequested();
        if (!_options.ShouldWriteSql(key.EmployeeCode, null)) throw new InvalidOperationException("SQL attachment writes are not enabled.");
        if (upload == null || upload.Length < 1 || upload.Length > _options.MaxCertificateBytes)
            throw new ArgumentException("Choose a PDF within the allowed file size.");
        var name = SkillRecoveryFiles.SafeFileName(upload.FileName);
        if (!string.Equals(Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a PDF file.");
        var reservation = Reserve(actor, sessionId, key);
        StagedEmployeeFile? staged = null;
        try
        {
            await using var input = upload.OpenReadStream();
            staged = await FileStager.StageAsync(input, key.EmployeeCode, FileKind.Certificate, name, actor, _options, ct,
                "skill-recovery-upload:" + reservation.UploadId.ToString("D"));
            Validate(actor, sessionId, key);
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ThrowIfDisposed();
                CleanupLocked();
                if (reservation.Replaces is Guid previous && _entries.TryGetValue(previous, out var prior) && prior.Borrowed)
                    throw new InvalidOperationException("The previous PDF is in use. Try again after that request finishes.");
                if (reservation.Replaces is Guid old && _entries.TryGetValue(old, out var replaced)) RetireLocked(replaced);
                var info = new SkillRecoveryPdfUploadInfo(reservation.UploadId, name, staged.Request.ByteLength,
                    Convert.ToHexString(staged.Request.Sha256), _clock.GetUtcNow().AddMinutes(10));
                var entry = new Entry(actor, sessionId, key, info, staged);
                _entries.Add(info.UploadId, entry); _owned.Add(entry); _pending.Remove(reservation);
                staged = null;
                return info;
            }
        }
        finally
        {
            lock (_gate) _pending.Remove(reservation);
            if (staged != null) DisposeStage(staged);
        }
    }

    public SkillRecoveryPdfUploadInfo Describe(string actor, string sessionId, SkillRecoveryKey key, Guid uploadId)
    {
        Validate(actor, sessionId, key);
        lock (_gate) return FindLocked(actor, sessionId, key, uploadId).Info;
    }

    public Task<SkillRecoveryPdfUploadLease> AcquireAsync(string actor, string sessionId, SkillRecoveryKey key,
        Guid uploadId, CancellationToken ct)
    {
        Validate(actor, sessionId, key);
        ct.ThrowIfCancellationRequested();
        Entry entry;
        lock (_gate)
        {
            entry = FindLocked(actor, sessionId, key, uploadId);
            if (entry.Borrowed) throw new InvalidOperationException("This PDF is in use. Try again after that request finishes.");
            try { entry.Stage.Content.Position = 0; }
            catch { RetireLocked(entry); throw; }
            entry.Borrowed = true;
        }
        var lease = new SkillRecoveryPdfUploadLease(this, entry);
        try { Validate(actor, sessionId, key); return Task.FromResult(lease); }
        catch { lease.Dispose(); throw; }
    }

    public async Task<SkillRecoveryPdf> OpenPreviewAsync(string actor, string sessionId, Guid uploadId, CancellationToken ct)
    {
        ValidateSession(actor, sessionId);
        SkillRecoveryKey key;
        lock (_gate)
        {
            ThrowIfDisposed(); CleanupLocked();
            if (!_entries.TryGetValue(uploadId, out var entry)) throw Missing();
            if (entry.Actor != actor || entry.SessionId != sessionId) throw new UnauthorizedAccessException("This PDF belongs to another session.");
            key = entry.Key;
        }
        var lease = await AcquireAsync(actor, sessionId, key, uploadId, ct);
        try { return new(new BorrowedReadStream(lease.Stage.Content), lease.Info.FileName, lease); }
        catch { lease.Dispose(); throw; }
    }

    internal void Discard(string actor, string sessionId, SkillRecoveryKey key, Guid uploadId)
    {
        Validate(actor, sessionId, key);
        lock (_gate) RetireLocked(FindLocked(actor, sessionId, key, uploadId));
    }

    private Reservation Reserve(string actor, string sessionId, SkillRecoveryKey key)
    {
        lock (_gate)
        {
            ThrowIfDisposed(); CleanupLocked();
            var previous = _entries.Values.SingleOrDefault(entry => entry.Actor == actor && entry.SessionId == sessionId && entry.Key == key);
            if (_owned.Any(entry => entry.Actor == actor && entry.SessionId == sessionId && entry.Key == key && entry.Borrowed) ||
                _pending.Any(item => item.Actor == actor && item.SessionId == sessionId && item.Key == key))
                throw new InvalidOperationException("This skill already has a PDF request in progress.");
            var sessionCount = _owned.Where(entry => entry.Actor == actor && entry.SessionId == sessionId).Select(entry => entry.Key)
                .Concat(_pending.Where(item => item.Actor == actor && item.SessionId == sessionId).Select(item => item.Key)).Distinct().Count();
            if (previous == null && sessionCount >= _sessionLimit)
                throw new InvalidOperationException("Two PDFs are already staged in this session. Wait for one to expire.");
            var bytes = _owned.Sum(entry => entry.Info.ByteLength) + _pending.Sum(item => item.ReservedBytes);
            if (_owned.Count + _pending.Count >= _globalLimit || _options.MaxCertificateBytes > _byteLimit - bytes)
                throw new InvalidOperationException("PDF staging is busy. Try again after an existing upload expires.");
            var reservation = new Reservation(Guid.NewGuid(), actor, sessionId, key, previous?.Info.UploadId, _options.MaxCertificateBytes);
            _pending.Add(reservation);
            return reservation;
        }
    }

    private Entry FindLocked(string actor, string sessionId, SkillRecoveryKey key, Guid id)
    {
        ThrowIfDisposed(); CleanupLocked();
        if (!_entries.TryGetValue(id, out var entry)) throw Missing();
        if (entry.Actor != actor || entry.SessionId != sessionId || entry.Key != key)
            throw new UnauthorizedAccessException("This PDF belongs to another session or skill.");
        return entry;
    }

    private void Validate(string actor, string sessionId, SkillRecoveryKey key)
    {
        ValidateSession(actor, sessionId);
        if (key == null || key.RowId == Guid.Empty || key.Table is not (SkillRecoveryTable.Current or SkillRecoveryTable.Archive))
            throw new ArgumentException("Select an exact skill record.");
        SkillRecoveryFiles.ValidateEmployee(key.EmployeeCode);
    }

    private void ValidateSession(string actor, string sessionId)
    {
        if (!_developers.IsDeveloper(actor)) throw new UnauthorizedAccessException("Developer access is required.");
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 200 || sessionId != sessionId.Trim() || sessionId.Any(char.IsControl))
            throw new ArgumentException("Reload this page to establish a valid session.");
    }

    private void Cleanup()
    {
        lock (_gate) if (!_disposed) CleanupLocked();
    }

    private void CleanupLocked()
    {
        foreach (var entry in _entries.Values.Where(entry => entry.Info.ExpiresAt <= _clock.GetUtcNow()).ToArray()) RetireLocked(entry);
    }

    private void RetireLocked(Entry entry)
    {
        entry.Retired = true;
        _entries.Remove(entry.Info.UploadId);
        if (!entry.Borrowed && _owned.Remove(entry)) DisposeStage(entry.Stage);
    }

    internal void Consume(Entry entry)
    {
        // Called after the SQL commit: cleanup failures must never turn success into an error.
        try { lock (_gate) RetireLocked(entry); }
        catch (Exception error) { LogCleanup(error); }
    }

    internal void Release(Entry entry)
    {
        try
        {
            lock (_gate)
            {
                entry.Borrowed = false;
                if (entry.Retired || _disposed || entry.Info.ExpiresAt <= _clock.GetUtcNow()) RetireLocked(entry);
            }
        }
        catch (Exception error) { LogCleanup(error); }
    }

    private void DisposeStage(StagedEmployeeFile stage)
    {
        try { stage.Dispose(); } catch (Exception error) { LogCleanup(error); }
    }

    private void LogCleanup(Exception error)
    {
        try { _logger.LogWarning(error, "Recovery PDF staging cleanup could not complete."); } catch { }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static FileNotFoundException Missing() => new("The staged PDF expired or was removed. Choose it again.");

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            foreach (var entry in _owned.ToArray()) RetireLocked(entry);
        }
    }

    internal sealed record Reservation(Guid UploadId, string Actor, string SessionId, SkillRecoveryKey Key, Guid? Replaces, long ReservedBytes);
    internal sealed class Entry(string actor, string sessionId, SkillRecoveryKey key, SkillRecoveryPdfUploadInfo info, StagedEmployeeFile stage)
    {
        public string Actor { get; } = actor;
        public string SessionId { get; } = sessionId;
        public SkillRecoveryKey Key { get; } = key;
        public SkillRecoveryPdfUploadInfo Info { get; } = info;
        public StagedEmployeeFile Stage { get; } = stage;
        public bool Borrowed { get; set; }
        public bool Retired { get; set; }
    }

    private sealed class BorrowedReadStream(Stream inner) : Stream
    {
        private bool _disposed;
        public override bool CanRead => !_disposed && inner.CanRead;
        public override bool CanSeek => !_disposed && inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length { get { Check(); return inner.Length; } }
        public override long Position { get { Check(); return inner.Position; } set { Check(); inner.Position = value; } }
        private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);
        public override int Read(byte[] buffer, int offset, int count) { Check(); return inner.Read(buffer, offset, count); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) { Check(); return inner.ReadAsync(buffer, offset, count, ct); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { Check(); return inner.ReadAsync(buffer, ct); }
        public override long Seek(long offset, SeekOrigin origin) { Check(); return inner.Seek(offset, origin); }
        public override void Flush() { Check(); }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
    }
}

public sealed class SkillRecoveryPdfUploadLease : IDisposable
{
    private SkillRecoveryPdfUploads? _owner;
    private readonly SkillRecoveryPdfUploads.Entry _entry;
    internal SkillRecoveryPdfUploadLease(SkillRecoveryPdfUploads owner, SkillRecoveryPdfUploads.Entry entry) { _owner = owner; _entry = entry; }
    public SkillRecoveryPdfUploadInfo Info => _entry.Info;
    public StagedEmployeeFile Stage => _owner != null ? _entry.Stage : throw new ObjectDisposedException(nameof(SkillRecoveryPdfUploadLease));
    public void Consume() => _owner?.Consume(_entry);
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(_entry);
}

using System.IO.Compression;
using Javideo.Worker.Db;
using Microsoft.Data.Sqlite;

namespace Javideo.Worker.Services;

/// <summary>
/// Backup / restore of all user data: the SQLite database, cached actor
/// avatars (actors/), preview images (previews/) and cloud library cache
/// (cache/). Uses the standard
/// library ZipFile — no third-party dependency.
/// </summary>
public sealed class BackupService
{
    private const string PendingDbName = "library.db.restore-pending";
    private readonly DbConnectionFactory _db;
    public BackupService(DbConnectionFactory db) => _db = db;

    /// <summary>Export all user data to a temp zip file and return its path.
    /// The caller (endpoint) streams it to the client and deletes the temp.</summary>
    public string Export()
    {
        // Unique name — two exports in the same second must not fight over the
        // same file while one of them is still being streamed.
        var tempZip = Path.Combine(Path.GetTempPath(), $"javideo-backup-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");

        using var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create);

        // 1. SQLite database — snapshot it via VACUUM INTO, which produces a
        //    consistent copy even while other endpoints are writing (a plain
        //    File.Copy of a live db can tear mid-transaction).
        var tempDb = Path.Combine(Path.GetTempPath(), $"javideo-db-{Guid.NewGuid():N}.db");
        try
        {
            using (var snap = new SqliteConnection($"Data Source={_db.DbPath}"))
            {
                snap.Open();
                using var cmd = snap.CreateCommand();
                cmd.CommandText = "VACUUM INTO @p";
                cmd.Parameters.AddWithValue("@p", tempDb);
                cmd.ExecuteNonQuery();
            }
            archive.CreateEntryFromFile(tempDb, "library.db");
        }
        finally
        {
            try { File.Delete(tempDb); } catch { }
        }

        // 2. Cached actor avatars.
        AddDirectory(archive, _db.AvatarsDir, "actors/");

        // 3. Cached preview images.
        var previewsDir = Path.Combine(_db.DataDir, "previews");
        AddDirectory(archive, previewsDir, "previews/");

        // 4. Cloud-drive library metadata cache.
        AddDirectory(archive, Path.Combine(_db.DataDir, "cache"), "cache/");

        // 5. Settings are inside library.db, no separate file needed.

        return tempZip;
    }

    /// <summary>Validate an uploaded backup and stage its database for the
    /// next startup. Replacing a live WAL database can leave the old WAL file
    /// attached to the new database, so the database swap must happen before
    /// any connection opens after restart.</summary>
    public void Import(string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("备份文件不存在");

        // Extract to a temp staging dir first, validate, then move.
        var staging = Path.Combine(Path.GetTempPath(), $"javideo-import-{Guid.NewGuid():N}");
        try
        {
            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);

            // Validate: must contain a real SQLite database.
            var srcDb = Path.Combine(staging, "library.db");
            if (!File.Exists(srcDb))
                throw new InvalidDataException("备份文件无效:缺少 library.db");
            if (!IsSqliteFile(srcDb))
                throw new InvalidDataException("备份文件无效:library.db 不是有效的 SQLite 数据库");
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = srcDb,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString()))
            {
                source.Open();
                using var check = source.CreateCommand();
                check.CommandText = "PRAGMA integrity_check";
                if (check.ExecuteScalar() as string != "ok")
                    throw new InvalidDataException("library.db 完整性检查失败");
            }

            // Cache files may be copied while running. The database is only
            // replaced on startup, after every old connection has closed.
            CopyDirOverwrite(Path.Combine(staging, "actors"), _db.AvatarsDir);
            CopyDirOverwrite(Path.Combine(staging, "previews"), Path.Combine(_db.DataDir, "previews"));
            CopyDirOverwrite(Path.Combine(staging, "cache"), Path.Combine(_db.DataDir, "cache"));

            var pending = Path.Combine(_db.DataDir, PendingDbName);
            var pendingTemp = pending + ".tmp";
            try
            {
                File.Copy(srcDb, pendingTemp, overwrite: true);
                File.Move(pendingTemp, pending, overwrite: true);
            }
            finally
            {
                if (File.Exists(pendingTemp)) File.Delete(pendingTemp);
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>Apply the staged database before DbInitializer opens it.</summary>
    public static void ApplyPendingRestore(DbConnectionFactory db)
    {
        var pending = Path.Combine(db.DataDir, PendingDbName);
        if (!File.Exists(pending)) return;

        // Preserve a consistent snapshot of the current database, including
        // committed pages still in its WAL, for manual recovery if needed.
        if (File.Exists(db.DbPath))
        {
            var backupTemp = db.DbPath + ".bak.tmp";
            try
            {
                using (var current = db.Create())
                using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = backupTemp,
                    Pooling = false
                }.ToString()))
                {
                    current.Open();
                    backup.Open();
                    current.BackupDatabase(backup);
                }
                File.Move(backupTemp, db.DbPath + ".bak", overwrite: true);
            }
            finally
            {
                if (File.Exists(backupTemp)) File.Delete(backupTemp);
            }
        }

        SqliteConnection.ClearAllPools();
        File.Delete(db.DbPath + "-wal");
        File.Delete(db.DbPath + "-shm");
        File.Move(pending, db.DbPath, overwrite: true);
    }

    // --- helpers ---

    /// <summary>SQLite files start with the 16-byte magic header.</summary>
    private static bool IsSqliteFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            if (fs.Read(header) < header.Length) return false;
            return "SQLite format 3\0"u8.SequenceEqual(header);
        }
        catch { return false; }
    }

    private static void AddDirectory(ZipArchive archive, string dir, string prefix)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = prefix + Path.GetRelativePath(dir, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, rel);
        }
    }

    private static void CopyDirOverwrite(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}

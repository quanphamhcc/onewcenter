using Microsoft.Data.Sqlite;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Phase-0 shared SQLite infrastructure. Owns the path to the single shared
/// application database (<c>app.db</c>) and hands out connections to it.
/// </summary>
/// <remarks>
/// This is intentionally thin — a connection/path factory, nothing store-specific.
/// It mirrors the connection-string and directory-creation shape of
/// <see cref="Bible.BibleDatabaseService"/> (<c>Data Source=&lt;path&gt;</c>, a new
/// <see cref="SqliteConnection"/> per call, no shared/pooled connection kept open).
///
/// Each store (phase) owns its own table(s): it keeps its own
/// <c>CREATE TABLE IF NOT EXISTS</c> DDL and runs it against a connection from
/// <see cref="CreateConnection"/> during its own initialization. Because every DDL
/// statement is <c>IF NOT EXISTS</c>, multiple stores sharing one <c>app.db</c> can
/// each run their schema-init idempotently and in any order with no conflict — so
/// schema-init is safe to call repeatedly and from multiple stores. No central
/// table registry is needed; a store simply initializes itself on first use.
///
/// The Bible database (<c>bible.db</c>) stays SEPARATE from <c>app.db</c>.
/// </remarks>
public class AppDatabase
{
    /// <summary>Default file name for the shared application database.</summary>
    public const string DefaultFileName = "app.db";

    /// <summary>
    /// Default path to <c>app.db</c> under the app output folder
    /// (<see cref="AppDomain.CurrentDomain"/>.<see cref="AppDomain.BaseDirectory"/>),
    /// alongside <c>bible.db</c>.
    /// </summary>
    public static string DefaultPath =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultFileName);

    private readonly string _connectionString;

    /// <summary>
    /// Create an <see cref="AppDatabase"/> pointed at a specific database file.
    /// Tests inject a temp path; production passes <see cref="DefaultPath"/>.
    /// </summary>
    public AppDatabase(string databasePath)
    {
        DatabasePath = databasePath;
        _connectionString = $"Data Source={databasePath}";
    }

    /// <summary>The resolved path to the database file this instance manages.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Create a new (closed) connection to the database. The caller opens and
    /// disposes it, exactly like each <see cref="Bible.BibleDatabaseService"/> method does per call.
    /// </summary>
    public SqliteConnection CreateConnection() => new SqliteConnection(_connectionString);

    /// <summary>
    /// Ensure the parent directory of the database file exists. Safe to call repeatedly.
    /// Mirrors the directory-creation step in <see cref="Bible.BibleDatabaseService.InitializeAsync"/>.
    /// </summary>
    public void EnsureDirectory()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}

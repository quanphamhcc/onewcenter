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

    /// <summary>
    /// Environment variable that, when set to a path, overrides where <c>app.db</c> lives.
    /// Takes priority over the sidecar file and the default location.
    /// </summary>
    public const string PathOverrideEnvVar = "WORSHIPPLAYER_DB";

    /// <summary>
    /// Sidecar file (next to the app) whose first non-empty line is the path to <c>app.db</c>.
    /// Lets the operator point the whole app at a shared/network database (e.g. a UNC path)
    /// WITHOUT a rebuild, and revert just by deleting or clearing the file.
    /// </summary>
    public const string PathOverrideFileName = "db-path.txt";

    /// <summary>
    /// Resolve the <c>app.db</c> path every store should use. Priority:
    /// <list type="number">
    ///   <item>the <see cref="PathOverrideEnvVar"/> environment variable, when set to a non-empty value;</item>
    ///   <item>the first non-empty, non-comment line of <see cref="PathOverrideFileName"/> next to the app;</item>
    ///   <item><see cref="DefaultPath"/> (app-folder <c>app.db</c>) otherwise.</item>
    /// </list>
    /// All resolution is best-effort and never throws — any error falls through to the next option,
    /// so a bad override can never stop the app from starting on its local database.
    /// </summary>
    public static string ResolvePath()
    {
        // 1) Environment variable wins (easy per-machine / per-launch override).
        try
        {
            var env = Environment.GetEnvironmentVariable(PathOverrideEnvVar);
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();
        }
        catch { /* fall through */ }

        // 2) Sidecar file next to the app (first non-empty, non-'#' line).
        try
        {
            var sidecar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PathOverrideFileName);
            if (File.Exists(sidecar))
            {
                foreach (var raw in File.ReadAllLines(sidecar))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#'))
                        continue;
                    return line;
                }
            }
        }
        catch { /* fall through */ }

        // 3) Default: app-folder app.db.
        return DefaultPath;
    }

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

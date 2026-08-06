using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SoundDeck.Core;

namespace SoundDeck.Infrastructure;

public sealed class SqliteSoundRepository : ISoundRepository
{
    private readonly string _connectionString;

    public SqliteSoundRepository(string? databasePath = null)
    {
        databasePath ??= AppPaths.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS Boards(
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Categories(
                Id TEXT PRIMARY KEY,
                BoardId TEXT NOT NULL REFERENCES Boards(Id) ON DELETE CASCADE,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Sounds(
                Id TEXT PRIMARY KEY,
                BoardId TEXT NOT NULL REFERENCES Boards(Id) ON DELETE CASCADE,
                CategoryId TEXT NULL REFERENCES Categories(Id) ON DELETE SET NULL,
                Name TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                Color TEXT NOT NULL,
                Icon TEXT NOT NULL,
                DurationSeconds REAL NOT NULL,
                TrimStartSeconds REAL NOT NULL,
                TrimEndSeconds REAL NOT NULL,
                FadeInSeconds REAL NOT NULL,
                FadeOutSeconds REAL NOT NULL,
                GainDb REAL NOT NULL,
                Route INTEGER NOT NULL,
                Hotkey TEXT NULL,
                MidiNote INTEGER NULL,
                SortOrder INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Sounds_BoardId ON Sounds(BoardId, SortOrder);
            CREATE TABLE IF NOT EXISTS Settings(
                Id INTEGER PRIMARY KEY CHECK(Id = 1),
                Json TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM Boards;";
        var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (count == 0)
            await SaveBoardAsync(new SoundBoard { Name = "Mi tablero" }, cancellationToken);
    }

    public async Task<IReadOnlyList<SoundBoard>> GetBoardsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SoundBoard>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, SortOrder FROM Boards ORDER BY SortOrder, Name;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SoundBoard
            {
                Id = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                SortOrder = reader.GetInt32(2)
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<SoundCategory>> GetCategoriesAsync(
        Guid boardId, CancellationToken cancellationToken = default)
    {
        var result = new List<SoundCategory>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, BoardId, Name, SortOrder FROM Categories WHERE BoardId=$boardId ORDER BY SortOrder, Name;";
        command.Parameters.AddWithValue("$boardId", boardId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SoundCategory
            {
                Id = Guid.Parse(reader.GetString(0)),
                BoardId = Guid.Parse(reader.GetString(1)),
                Name = reader.GetString(2),
                SortOrder = reader.GetInt32(3)
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<SoundClip>> GetSoundsAsync(
        Guid boardId, string? search = null, CancellationToken cancellationToken = default)
    {
        var result = new List<SoundClip>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, BoardId, CategoryId, Name, FilePath, Color, Icon, DurationSeconds,
                   TrimStartSeconds, TrimEndSeconds, FadeInSeconds, FadeOutSeconds, GainDb,
                   Route, Hotkey, MidiNote, SortOrder
            FROM Sounds
            WHERE BoardId=$boardId AND ($search='' OR Name LIKE '%' || $search || '%' COLLATE NOCASE)
            ORDER BY SortOrder, Name;
            """;
        command.Parameters.AddWithValue("$boardId", boardId.ToString());
        command.Parameters.AddWithValue("$search", search?.Trim() ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SoundClip
            {
                Id = Guid.Parse(reader.GetString(0)),
                BoardId = Guid.Parse(reader.GetString(1)),
                CategoryId = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                Name = reader.GetString(3),
                FilePath = reader.GetString(4),
                Color = reader.GetString(5),
                Icon = reader.GetString(6),
                DurationSeconds = reader.GetDouble(7),
                TrimStartSeconds = reader.GetDouble(8),
                TrimEndSeconds = reader.GetDouble(9),
                FadeInSeconds = reader.GetDouble(10),
                FadeOutSeconds = reader.GetDouble(11),
                GainDb = reader.GetDouble(12),
                Route = (AudioRoute)reader.GetInt32(13),
                Hotkey = reader.IsDBNull(14) ? null : reader.GetString(14),
                MidiNote = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                SortOrder = reader.GetInt32(16)
            });
        }
        return result;
    }

    public async Task SaveBoardAsync(SoundBoard board, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Boards(Id, Name, SortOrder) VALUES($id, $name, $sort)
            ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name, SortOrder=excluded.SortOrder;
            """;
        command.Parameters.AddWithValue("$id", board.Id.ToString());
        command.Parameters.AddWithValue("$name", board.Name);
        command.Parameters.AddWithValue("$sort", board.SortOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveCategoryAsync(SoundCategory category, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Categories(Id, BoardId, Name, SortOrder) VALUES($id, $boardId, $name, $sort)
            ON CONFLICT(Id) DO UPDATE SET BoardId=excluded.BoardId, Name=excluded.Name, SortOrder=excluded.SortOrder;
            """;
        command.Parameters.AddWithValue("$id", category.Id.ToString());
        command.Parameters.AddWithValue("$boardId", category.BoardId.ToString());
        command.Parameters.AddWithValue("$name", category.Name);
        command.Parameters.AddWithValue("$sort", category.SortOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSoundAsync(SoundClip sound, CancellationToken cancellationToken = default)
    {
        sound.Validate();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Sounds(
                Id, BoardId, CategoryId, Name, FilePath, Color, Icon, DurationSeconds,
                TrimStartSeconds, TrimEndSeconds, FadeInSeconds, FadeOutSeconds, GainDb,
                Route, Hotkey, MidiNote, SortOrder)
            VALUES(
                $id, $boardId, $categoryId, $name, $filePath, $color, $icon, $duration,
                $trimStart, $trimEnd, $fadeIn, $fadeOut, $gain, $route, $hotkey, $midiNote, $sort)
            ON CONFLICT(Id) DO UPDATE SET
                BoardId=excluded.BoardId, CategoryId=excluded.CategoryId, Name=excluded.Name,
                FilePath=excluded.FilePath, Color=excluded.Color, Icon=excluded.Icon,
                DurationSeconds=excluded.DurationSeconds, TrimStartSeconds=excluded.TrimStartSeconds,
                TrimEndSeconds=excluded.TrimEndSeconds, FadeInSeconds=excluded.FadeInSeconds,
                FadeOutSeconds=excluded.FadeOutSeconds, GainDb=excluded.GainDb, Route=excluded.Route,
                Hotkey=excluded.Hotkey, MidiNote=excluded.MidiNote, SortOrder=excluded.SortOrder;
            """;
        AddSoundParameters(command, sound);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSoundAsync(Guid soundId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sounds WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", soundId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Json FROM Settings WHERE Id=1;";
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrWhiteSpace(json)
            ? new AppSettings()
            : JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Settings(Id, Json) VALUES(1, $json)
            ON CONFLICT(Id) DO UPDATE SET Json=excluded.Json;
            """;
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static void AddSoundParameters(SqliteCommand command, SoundClip sound)
    {
        command.Parameters.AddWithValue("$id", sound.Id.ToString());
        command.Parameters.AddWithValue("$boardId", sound.BoardId.ToString());
        command.Parameters.AddWithValue("$categoryId", (object?)sound.CategoryId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$name", sound.Name);
        command.Parameters.AddWithValue("$filePath", sound.FilePath);
        command.Parameters.AddWithValue("$color", sound.Color);
        command.Parameters.AddWithValue("$icon", sound.Icon);
        command.Parameters.AddWithValue("$duration", sound.DurationSeconds);
        command.Parameters.AddWithValue("$trimStart", sound.TrimStartSeconds);
        command.Parameters.AddWithValue("$trimEnd", sound.TrimEndSeconds);
        command.Parameters.AddWithValue("$fadeIn", sound.FadeInSeconds);
        command.Parameters.AddWithValue("$fadeOut", sound.FadeOutSeconds);
        command.Parameters.AddWithValue("$gain", sound.GainDb);
        command.Parameters.AddWithValue("$route", (int)sound.Route);
        command.Parameters.AddWithValue("$hotkey", (object?)sound.Hotkey ?? DBNull.Value);
        command.Parameters.AddWithValue("$midiNote", (object?)sound.MidiNote ?? DBNull.Value);
        command.Parameters.AddWithValue("$sort", sound.SortOrder);
    }
}

public static class AppPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundDeck");
    public static string Library { get; } = Path.Combine(Root, "Library");
    public static string DatabasePath { get; } = Path.Combine(Root, "sounddeck.db");
}

using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OgameSkaner.Core.Storage.Sqlite;

/// <summary>Small ADO.NET helpers: prepared commands with <c>$name</c> parameters and the time format of the database.</summary>
internal static partial class Sql
{
    /// <summary>A command whose <c>$name</c> parameters are created once; set them with <see cref="Set"/> before each run.</summary>
    public static SqliteCommand Prepare(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var name in ParameterPattern().Matches(sql).Select(m => m.Value).Distinct())
            command.Parameters.Add(new SqliteParameter(name, null));
        return command;
    }

    public static SqliteCommand Set(this SqliteCommand command, string name, object? value)
    {
        command.Parameters[name].Value = value ?? DBNull.Value;
        return command;
    }

    public static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = Prepare(connection, transaction, sql);
        command.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = Prepare(connection, null, sql);
        return command.ExecuteScalar();
    }

    /// <summary>Times are stored as UTC ticks: exact, sortable and independent of the offset they were taken in.</summary>
    public static long Ticks(DateTimeOffset time) => time.UtcTicks;

    public static DateTimeOffset Time(long ticks) => new DateTimeOffset(ticks, TimeSpan.Zero).ToLocalTime();

    public static string? NullableString(this SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static long? NullableInt64(this SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    public static int? NullableInt32(this SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    [GeneratedRegex(@"\$[A-Za-z_]+")]
    private static partial Regex ParameterPattern();
}

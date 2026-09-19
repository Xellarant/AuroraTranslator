#nullable enable
using System.IO;
using Microsoft.Data.Sqlite;

namespace AuroraTranslator.Content;

public static class ContentDatabase
{
    /// <summary>
    /// Opens an existing database for queries. Normal reads remain read-only, but a leftover
    /// rollback journal requires a writable connection so SQLite can recover an interrupted
    /// transaction before serving data.
    /// </summary>
    public static SqliteConnection OpenReadableConnection(string sqlitePath)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Mode = File.Exists(sqlitePath + "-journal")
                    ? SqliteOpenMode.ReadWrite
                    : SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
        connection.Open();
        return connection;
    }
}

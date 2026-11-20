using Microsoft.Data.Sqlite;

namespace VoiceRecorder.Storage;

/// <summary>
/// Migra las sesiones de la antigua base <c>sessions.db</c> a la estructura de
/// carpetas de <see cref="FileSessionStore"/>, moviendo además los WAV citados
/// en las notas a la carpeta de su sesión.
/// </summary>
public static class SqliteMigration
{
    private static readonly string[] AudioNotePrefixes = ["Audio (micro): ", "Audio (llamada): "];

    /// <summary>
    /// Si existe <paramref name="databasePath"/> y el almacén está vacío, copia
    /// todas las sesiones y renombra la base a <c>.migrated</c> (no se borra).
    /// Devuelve el número de sesiones migradas.
    /// </summary>
    public static int MigrateIfNeeded(string databasePath, FileSessionStore target)
    {
        if (!File.Exists(databasePath) || !target.IsEmpty)
        {
            return 0;
        }

        var migrated = 0;
        using (var source = new SQLiteSessionStore(databasePath))
        {
            foreach (var summary in source.ListSessions())
            {
                if (source.GetSession(summary.Id) is not var (session, segments))
                {
                    continue;
                }

                var id = target.ImportSession(session, segments);
                var audio = ExtractAudioPaths(session.Notes).Where(File.Exists).ToList();
                if (audio.Count > 0)
                {
                    target.AttachFiles(id, audio);
                }

                migrated++;
            }
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = databasePath + suffix;
            if (File.Exists(file))
            {
                File.Move(file, file + ".migrated", overwrite: true);
            }
        }

        return migrated;
    }

    /// <summary>Rutas de audio registradas en las notas ("Audio (micro): ruta").</summary>
    public static IEnumerable<string> ExtractAudioPaths(string? notes)
    {
        foreach (var line in (notes ?? string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            foreach (var prefix in AudioNotePrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                {
                    yield return trimmed[prefix.Length..].Trim();
                }
            }
        }
    }
}

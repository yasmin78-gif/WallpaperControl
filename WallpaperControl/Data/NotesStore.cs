using System.IO;
using System.Text.Json;

namespace WallpaperControl
{
    internal sealed class NotesDocument
    {
        public int Version { get; set; } = 1;
        public List<NoteEntry> Entries { get; set; } = new();
    }

    /// <summary>UI-thread owned immutable entries with atomic replacement, backup and corrupt-file preservation.</summary>
    internal sealed class NotesStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WallpaperControl", "notes.json");
        private readonly string path;
        private readonly Func<DateTime> clock;
        private NoteEntry[] entries = Array.Empty<NoteEntry>();
        internal event Action? Changed;
        internal IReadOnlyList<NoteEntry> Entries => Array.AsReadOnly(entries);
        internal bool CanWrite { get; private set; } = true;
        internal bool LoadIssue { get; private set; }
        internal DateTime Now => clock();

        internal NotesStore(string? path = null, Func<DateTime>? clock = null)
        {
            this.path = path ?? DefaultPath;
            this.clock = clock ?? (() => DateTime.Now);
            Load();
        }

        private static NotesDocument Read(string candidate)
        {
            if (new FileInfo(candidate).Length > 16 * 1024 * 1024) throw new JsonException("Notes document exceeds the size limit.");
            NotesDocument document = JsonSerializer.Deserialize<NotesDocument>(File.ReadAllText(candidate), JsonOptions)
                ?? throw new JsonException("Missing notes document.");
            if (document.Version is not (1 or 2)) throw new NotSupportedException("Unsupported notes version.");
            if (document.Entries == null || document.Entries.Count > 10000 || document.Entries.Any(e => e == null || !e.IsValid)
                || document.Entries.Select(e => e.Id).Distinct().Count() != document.Entries.Count)
                throw new JsonException("Invalid notes document.");
            return document;
        }

        private void Load()
        {
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try { entries = Read(candidate).Entries.ToArray(); return; }
                catch (NotSupportedException) { LoadIssue = true; CanWrite = false; return; }
                catch (JsonException)
                {
                    LoadIssue = true;
                    try { File.Move(candidate, candidate + ".corrupt-" + Guid.NewGuid().ToString("N")); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CanWrite = false; return; }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LoadIssue = true; CanWrite = false; return; }
            }
        }

        internal bool SaveEntry(NoteEntry entry)
        {
            if (!entry.IsValid) return false;
            NoteEntry? existing = entries.FirstOrDefault(e => e.Id == entry.Id);
            if (existing != null && existing.CreatedAt != entry.CreatedAt) return false;
            return Commit(entries.Where(e => e.Id != entry.Id).Append(entry with { Title = entry.Title.Trim() }).ToArray());
        }

        internal bool Complete(Guid id, bool completed)
        {
            NoteEntry? entry = entries.FirstOrDefault(e => e.Id == id);
            return entry != null && SaveEntry(entry with { IsCompleted = completed,
                CompletedAt = completed ? entry.IsCompletedOn(Now) ? entry.CompletedAt : Now : null });
        }

        internal bool Delete(Guid id) => entries.Any(e => e.Id == id) && Commit(entries.Where(e => e.Id != id).ToArray());

        private bool Commit(NoteEntry[] next)
        {
            if (!CanWrite || next.Length > 10000) return false;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    // Older releases must not silently turn recurring tasks into one-off notes.
                    JsonSerializer.Serialize(stream, new NotesDocument { Version = next.Any(e => e.RepeatsDaily) ? 2 : 1, Entries = next.ToList() }, JsonOptions);
                    if (stream.Length > 16 * 1024 * 1024) throw new IOException("Notes document exceeds the size limit.");
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                entries = next;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SettingsPersistence.ReportFailure("Notes could not be saved.", ex);
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { SettingsPersistence.ReportFailure("Notes temporary file could not be removed.", ex); }
            }
            Changed?.Invoke();
            return true;
        }
    }
}

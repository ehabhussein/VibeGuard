using GuardCode.Content.Validation;

namespace GuardCode.Content.Loading;

/// <summary>
/// Walks the archetypes root directory, loads every archetype, runs
/// validation, and returns the result. Implements the path-traversal
/// defense from design spec §6.1 by verifying every resolved file path
/// sits strictly under the root.
/// </summary>
public sealed class FileSystemArchetypeRepository : IArchetypeRepository
{
    private readonly string _rootFullPath;

    public FileSystemArchetypeRepository(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("root path must be non-empty", nameof(rootPath));
        }
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException(
                $"archetypes root does not exist: {rootPath}");
        }
        _rootFullPath = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
    }

    public IReadOnlyList<Archetype> LoadAll()
    {
        var filesByDirectory = GroupMarkdownFilesByDirectory();

        var archetypes = new List<Archetype>();
        foreach (var (directory, files) in filesByDirectory)
        {
            if (!files.ContainsKey("_principles.md")) continue;

            var archetypeId = DeriveArchetypeId(directory);
            var (archetype, rawLineCounts) = ArchetypeLoader.LoadWithLineCounts(archetypeId, files);
            ArchetypeValidator.Validate(archetype, rawLineCounts);
            archetypes.Add(archetype);
        }

        archetypes.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        return archetypes;
    }

    private Dictionary<string, Dictionary<string, string>> GroupMarkdownFilesByDirectory()
    {
        var filesByDirectory = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(_rootFullPath, "*.md", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            if (!fullPath.StartsWith(_rootFullPath, StringComparison.Ordinal))
            {
                throw new ArchetypeLoadException(
                    $"refusing to load file outside archetypes root: {fullPath}");
            }

            var directory = Path.GetDirectoryName(fullPath)!;
            if (!filesByDirectory.TryGetValue(directory, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                filesByDirectory[directory] = map;
            }
            map[Path.GetFileName(fullPath)] = File.ReadAllText(fullPath);
        }

        return filesByDirectory;
    }

    private string DeriveArchetypeId(string fullDirectory)
    {
        var relative = Path.GetRelativePath(_rootFullPath, fullDirectory);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.Length == 0) return path;
        var last = path[^1];
        if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar) return path;
        return path + Path.DirectorySeparatorChar;
    }
}

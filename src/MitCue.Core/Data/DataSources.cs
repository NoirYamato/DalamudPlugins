using System.Reflection;

namespace MitCue.Core.Data;

/// <summary>
/// Read-only access to MitCue's bundled data (fights, timelines, actions, plans) by
/// forward-slash relative paths such as "fights/top.yaml" or "timelines/the_omega_protocol.txt".
/// </summary>
public interface IDataSource
{
    /// <summary>Relative paths of all files directly or indirectly under <paramref name="folder"/>.</summary>
    IEnumerable<string> List(string folder);

    bool Exists(string path);

    string ReadText(string path);
}

/// <summary>Data embedded in MitCue.Core (resource names "MitCue.data/&lt;path&gt;").</summary>
public sealed class EmbeddedDataSource : IDataSource
{
    private readonly Assembly assembly;
    private readonly string prefix;
    private readonly Dictionary<string, string> resources;

    public EmbeddedDataSource(string prefix = "MitCue.data/", Assembly? assembly = null)
    {
        this.assembly = assembly ?? typeof(EmbeddedDataSource).Assembly;
        this.prefix = prefix;
        this.resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in this.assembly.GetManifestResourceNames())
        {
            var normalized = name.Replace('\\', '/');
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                this.resources[normalized[prefix.Length..]] = name;
        }
    }

    /// <summary>Plans bundled with the plugin (resource names "MitCue.plans/&lt;path&gt;").</summary>
    public static EmbeddedDataSource Plans() => new("MitCue.plans/");

    public IEnumerable<string> List(string folder)
    {
        var f = folder.Replace('\\', '/').TrimEnd('/') + "/";
        if (f == "/")
            return this.resources.Keys.OrderBy(k => k, StringComparer.Ordinal);
        return this.resources.Keys.Where(k => k.StartsWith(f, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k, StringComparer.Ordinal);
    }

    public bool Exists(string path) => this.resources.ContainsKey(path.Replace('\\', '/'));

    public string ReadText(string path)
    {
        if (!this.resources.TryGetValue(path.Replace('\\', '/'), out var name))
            throw new FileNotFoundException($"Embedded data not found: {path}");
        using var stream = this.assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Embedded data not found: {path}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Data read from a directory on disk (the repository's data/ folder in tests and tools).</summary>
public sealed class DirectoryDataSource(string root) : IDataSource
{
    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>Finds the repository's data/ folder by walking up from <paramref name="start"/>.</summary>
    public static DirectoryDataSource FindRepoData(string? start = null, string folder = "data")
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, folder);
            if (Directory.Exists(Path.Combine(candidate, "timelines")) || (folder != "data" && Directory.Exists(candidate)))
                return new DirectoryDataSource(candidate);
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not find '{folder}' above {start ?? AppContext.BaseDirectory}");
    }

    public IEnumerable<string> List(string folder)
    {
        var dir = Path.Combine(this.Root, folder);
        if (!Directory.Exists(dir))
            return [];
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(this.Root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    public bool Exists(string path) => File.Exists(Path.Combine(this.Root, path));

    public string ReadText(string path) => File.ReadAllText(Path.Combine(this.Root, path));
}

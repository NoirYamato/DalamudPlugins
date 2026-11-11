// RepoJsonCheck: validates a Dalamud custom plugin repository file ("pluginmaster", repo.json) the way
// Dalamud itself reads it, using Dalamud's own internal manifest types loaded by reflection from a Dalamud
// installation (no compile-time reference, so this builds anywhere and checks against the Dalamud you point
// it at).
//
// Usage:
//   dotnet run --project tools/RepoJsonCheck -- <repo.json> [--zip <latest.zip>] [--internal-name <Name>]
//                                              [--dalamud <dir>] [--strict] [--verbose]
//
//   --dalamud        Dalamud folder holding Dalamud.dll and Newtonsoft.Json.dll (default: $DALAMUD_HOME, else
//                    the Dalamud.NET.Sdk default for this OS, e.g. ~/.xlcore/dalamud/Hooks/dev on Linux).
//   --zip            the plugin zip the entry's DownloadLinkInstall serves; checked like Dalamud's installer does.
//   --internal-name  the entry that must exist (and that --zip belongs to; needed when the file lists several).
//   --strict         treat warnings as errors (CI uses it to notice new Dalamud manifest fields early).
//   --verbose        print every manifest property as Dalamud deserialized it.
//
// What is checked (Dalamud 15 source in brackets):
//   1. The text deserializes as List<RemotePluginManifest> through Newtonsoft with default settings
//      [PluginRepository.ReloadAsync].
//   2. Every JSON key is a property of RemotePluginManifest; Dalamud silently ignores unknown keys, so a
//      misspelt key would silently lose its value. Properties the file leaves out are listed as warnings.
//   3. Each entry passes PluginRepository.IsValidManifest (InternalName, Name, AssemblyVersion).
//   4. This Dalamud can install and update it: DalamudApiLevel equals its API level
//      [PluginManager.DetectAvailablePluginUpdates], MinimumDalamudVersion is not newer, ApplicableVersion is
//      "any", the entry is neither hidden nor testing-exclusive, and the download links are absolute http(s) URLs
//      (15.0.4.0 downloads DownloadLinkInstall for installs and updates alike [PluginManager.DownloadPluginAsync]).
//   5. With --zip: <InternalName>.dll and <InternalName>.json are at the zip root, the json deserializes as
//      LocalPluginManifest, its InternalName and AssemblyVersion equal the repository's (System.Version equality)
//      and it sets no WorkingPluginId [PluginManager.InstallPluginInternalAsync]; the DLL's assembly version
//      matches as well.
//
// Exit codes: 0 valid, 1 invalid, 2 usage error or Dalamud could not be loaded.
using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace MitCue.Tools.RepoJsonCheck;

internal static class Program
{
    private const string Usage =
        "usage: RepoJsonCheck <repo.json> [--zip <latest.zip>] [--internal-name <Name>] [--dalamud <dir>] [--strict] [--verbose]";

    private const string Help = """

        Checks a Dalamud custom plugin repository file (repo.json) the way Dalamud reads it, with Dalamud's own
        RemotePluginManifest and LocalPluginManifest types loaded from a Dalamud installation.

          --zip            the plugin zip the entry's DownloadLinkInstall serves; checked like the installer does
          --internal-name  the entry that must exist (and that --zip belongs to)
          --dalamud        folder with Dalamud.dll and Newtonsoft.Json.dll (default: $DALAMUD_HOME, else the
                           Dalamud.NET.Sdk default for this OS)
          --strict         treat warnings as errors
          --verbose        print every manifest property as Dalamud deserialized it

        Exit codes: 0 valid, 1 invalid, 2 usage error or Dalamud could not be loaded.
        """;

    /// <summary>Assemblies Dalamud provides to every plugin; shipping copies in the zip invites version conflicts.</summary>
    private static readonly string[] DalamudProvided =
    [
        "Dalamud.dll", "Dalamud.Common.dll", "Dalamud.Bindings.ImGui.dll", "Dalamud.Bindings.ImPlot.dll",
        "Dalamud.Bindings.ImGuizmo.dll", "FFXIVClientStructs.dll", "InteropGenerator.Runtime.dll", "Newtonsoft.Json.dll",
        "Lumina.dll", "Lumina.Excel.dll", "Serilog.dll", "Microsoft.Extensions.ObjectPool.dll",
    ];

    private static int Main(string[] args)
    {
        Options? parsed;
        try
        {
            parsed = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (parsed is not { } options)
        {
            Console.WriteLine(Usage);
            Console.WriteLine(Help);
            return 0;
        }

        DalamudTypes dalamud;
        try
        {
            dalamud = DalamudTypes.Load(options.DalamudDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not load Dalamud's manifest types from {options.DalamudDir}: {Describe(ex)}");
            return 2;
        }

        string text;
        try
        {
            text = File.ReadAllText(options.RepoJson);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: cannot read {options.RepoJson}: {ex.Message}");
            return 2;
        }

        var report = new Report();
        Console.WriteLine($"Checking {options.RepoJson} with {dalamud.Describe()}");
        var entries = Check(text, options, dalamud, report);
        report.Finish(options.RepoJson, entries, dalamud, options.Strict);
        return report.Errors > 0 || (options.Strict && report.Warnings > 0) ? 1 : 0;
    }

    private static int Check(string text, Options options, DalamudTypes dalamud, Report report)
    {
        // 1. Exactly what PluginRepository.ReloadAsync does.
        IList list;
        try
        {
            list = dalamud.DeserializeRemoteList(text)
                   ?? throw new InvalidDataException("Deserialized PluginMaster was null.");
        }
        catch (Exception ex)
        {
            report.Error($"Dalamud cannot read the file as a list of RemotePluginManifest: {Describe(ex)}");
            return 0;
        }

        if (list.Count == 0)
            report.Error("the repository lists no plugins");

        // 2. Keys versus the properties Newtonsoft maps for RemotePluginManifest.
        CheckKeys(text, dalamud, report);

        // 3 + 4. Per-entry checks.
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        object? target = null;
        for (var i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (entry == null)
            {
                report.Error($"entry {i} is null");
                continue;
            }

            var internalName = dalamud.Get<string>(entry, "InternalName");
            var label = $"entry {i} ({(string.IsNullOrWhiteSpace(internalName) ? "?" : internalName)})";
            CheckEntry(entry, label, dalamud, report);

            if (!string.IsNullOrWhiteSpace(internalName))
            {
                if (names.TryGetValue(internalName, out var first))
                    report.Error($"{label}: InternalName is also used by entry {first}");
                else
                    names[internalName] = i;

                if (options.InternalName != null && string.Equals(internalName, options.InternalName, StringComparison.Ordinal))
                    target = entry;
            }

            if (options.Verbose)
                dalamud.Dump(entry, label);
        }

        if (options.InternalName != null && target == null)
            report.Error($"no entry has InternalName \"{options.InternalName}\"");

        // 5. The zip, as the installer unpacks it.
        if (options.Zip != null)
        {
            if (target == null && options.InternalName == null)
            {
                if (list.Count == 1)
                    target = list[0];
                else
                    report.Error("--zip needs --internal-name when the repository lists several plugins");
            }

            if (target != null)
                CheckZip(options.Zip, target, dalamud, report);
        }

        return list.Count;
    }

    private static void CheckKeys(string text, DalamudTypes dalamud, Report report)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            report.Warning($"skipped the key check: the file is not strict JSON ({ex.Message})");
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return;

            var known = dalamud.RemoteJsonProperties;
            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    report.Error($"entry {index} is not a JSON object");
                    index++;
                    continue;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name))
                        report.Warning($"entry {index}: key \"{property.Name}\" appears twice (Newtonsoft keeps the last one)");

                    if (known.Contains(property.Name))
                        continue;
                    var caseInsensitive = known.FirstOrDefault(k => string.Equals(k, property.Name, StringComparison.OrdinalIgnoreCase));
                    if (caseInsensitive != null)
                        report.Warning($"entry {index}: key \"{property.Name}\" only matches \"{caseInsensitive}\" ignoring case");
                    else
                        report.Error($"entry {index}: key \"{property.Name}\" is not a RemotePluginManifest property; Dalamud ignores it");
                }

                var missing = known.Where(k => !element.EnumerateObject().Any(p => string.Equals(p.Name, k, StringComparison.OrdinalIgnoreCase))).ToList();
                if (missing.Count > 0)
                    report.Warning($"entry {index}: not set, Dalamud uses its defaults: {string.Join(", ", missing)}");
                index++;
            }
        }
    }

    private static void CheckEntry(object entry, string label, DalamudTypes dalamud, Report report)
    {
        // PluginRepository.IsValidManifest: entries failing these are dropped from the installer.
        var internalName = dalamud.Get<string>(entry, "InternalName");
        var name = dalamud.Get<string>(entry, "Name");
        var version = dalamud.Get<Version>(entry, "AssemblyVersion");
        if (string.IsNullOrWhiteSpace(internalName))
            report.Error($"{label}: InternalName is missing or blank (Dalamud drops the entry)");
        if (string.IsNullOrWhiteSpace(name))
            report.Error($"{label}: Name is missing or blank (Dalamud drops the entry)");
        if (version == null)
            report.Error($"{label}: AssemblyVersion is missing (Dalamud drops the entry)");

        var testingVersion = dalamud.Get<Version>(entry, "TestingAssemblyVersion");
        var testingApi = dalamud.Get<int?>(entry, "TestingDalamudApiLevel");
        if (testingVersion != null && version != null && testingVersion > version && testingApi == null)
            report.Warning($"{label}: TestingAssemblyVersion is set without TestingDalamudApiLevel");

        // PluginManager.IsManifestEligible / DetectAvailablePluginUpdates / plugin loading.
        var api = dalamud.Get<int>(entry, "DalamudApiLevel");
        if (api != dalamud.ApiLevel)
        {
            report.Error($"{label}: DalamudApiLevel is {api}, but this Dalamud is API {dalamud.ApiLevel}; " +
                         "it would be listed as outdated or hidden and never offered as an update");
        }

        var minimum = dalamud.Get<Version>(entry, "MinimumDalamudVersion");
        if (minimum != null && minimum > dalamud.Version)
            report.Error($"{label}: MinimumDalamudVersion {minimum} is newer than this Dalamud ({dalamud.Version})");

        var applicable = dalamud.GetRaw(entry, "ApplicableVersion");
        if (applicable != null && !Equals(applicable, dalamud.AnyGameVersion))
            report.Warning($"{label}: ApplicableVersion is {applicable}; Dalamud hides the plugin once the game is newer than that");

        if (dalamud.Get<bool>(entry, "IsHide"))
            report.Warning($"{label}: IsHide is true, so the plugin installer does not list it");
        if (dalamud.Get<bool>(entry, "IsTestingExclusive"))
            report.Warning($"{label}: IsTestingExclusive is true, so only users with testing builds enabled see it");

        CheckUrl(entry, "DownloadLinkInstall", label, required: true, dalamud, report);
        CheckUrl(entry, "DownloadLinkUpdate", label, required: false, dalamud, report);
        CheckUrl(entry, "DownloadLinkTesting", label, required: false, dalamud, report);
        foreach (var key in new[] { "RepoUrl", "IconUrl" })
        {
            var value = dalamud.Get<string>(entry, key);
            if (!string.IsNullOrEmpty(value) && !IsHttpUrl(value))
                report.Warning($"{label}: {key} \"{value}\" is not an absolute http(s) URL");
        }

        var lastUpdate = dalamud.Get<long>(entry, "LastUpdate");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (lastUpdate <= 0)
            report.Warning($"{label}: LastUpdate is not set (the installer shows it as 1970)");
        else if (lastUpdate > now + 86400)
            report.Warning($"{label}: LastUpdate {lastUpdate} is in the future");

        if (string.IsNullOrWhiteSpace(dalamud.Get<string>(entry, "Punchline")))
            report.Warning($"{label}: Punchline is empty");
        if (string.IsNullOrWhiteSpace(dalamud.Get<string>(entry, "Description")))
            report.Warning($"{label}: Description is empty");

        if (version != null && !string.IsNullOrWhiteSpace(name))
        {
            var updated = lastUpdate > 0
                ? DateTimeOffset.FromUnixTimeSeconds(lastUpdate).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
                : "never";
            report.Info($"{label}: \"{name}\" {version}, API {api}, updated {updated}, installs from {dalamud.Get<string>(entry, "DownloadLinkInstall")}");
        }
    }

    private static void CheckUrl(object entry, string key, string label, bool required, DalamudTypes dalamud, Report report)
    {
        var value = dalamud.Get<string>(entry, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                report.Error($"{label}: {key} is empty; Dalamud downloads the plugin from it");
            else
                report.Warning($"{label}: {key} is empty");
            return;
        }

        if (!IsHttpUrl(value))
        {
            if (required)
                report.Error($"{label}: {key} \"{value}\" is not an absolute http(s) URL");
            else
                report.Warning($"{label}: {key} \"{value}\" is not an absolute http(s) URL");
        }
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static void CheckZip(string zipPath, object repoEntry, DalamudTypes dalamud, Report report)
    {
        var internalName = dalamud.Get<string>(repoEntry, "InternalName");
        var repoVersion = dalamud.Get<Version>(repoEntry, "AssemblyVersion");
        if (string.IsNullOrWhiteSpace(internalName))
            return;

        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(zipPath);
        }
        catch (Exception ex)
        {
            report.Error($"cannot open {zipPath}: {ex.Message}");
            return;
        }

        using (zip)
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repojsoncheck-root"));
            foreach (var e in zip.Entries)
            {
                var full = Path.GetFullPath(Path.Combine(root, e.FullName));
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    report.Error($"{zipPath}: entry \"{e.FullName}\" would extract outside the plugin folder; Dalamud refuses the zip");
            }

            ZipArchiveEntry? Find(string fileName)
            {
                var exact = zip.Entries.FirstOrDefault(e => e.FullName == fileName);
                if (exact != null)
                    return exact;
                var other = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, fileName, StringComparison.OrdinalIgnoreCase));
                if (other != null)
                    report.Warning($"{zipPath}: \"{other.FullName}\" differs in case from \"{fileName}\" (fine on Windows only)");
                return other;
            }

            var dllEntry = Find(internalName + ".dll");
            var jsonEntry = Find(internalName + ".json");
            if (dllEntry == null)
                report.Error($"{zipPath}: no {internalName}.dll at the zip root; Dalamud loads <InternalName>.dll");
            if (jsonEntry == null)
            {
                report.Error($"{zipPath}: no {internalName}.json at the zip root; Dalamud reads the plugin manifest from it");
                return;
            }

            // LocalPluginManifest.Load = JsonConvert.DeserializeObject<LocalPluginManifest>(text).
            object? local;
            try
            {
                using var reader = new StreamReader(jsonEntry.Open());
                local = dalamud.DeserializeLocal(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                report.Error($"{zipPath}!{jsonEntry.FullName}: Dalamud cannot read it as LocalPluginManifest: {Describe(ex)}");
                return;
            }

            if (local == null)
            {
                report.Error($"{zipPath}!{jsonEntry.FullName}: empty manifest (\"Plugin had no valid manifest\")");
                return;
            }

            var localName = dalamud.Get<string>(local, "InternalName");
            var localVersion = dalamud.Get<Version>(local, "AssemblyVersion");
            if (!string.Equals(localName, internalName, StringComparison.Ordinal))
                report.Error($"{zipPath}: manifest InternalName \"{localName}\" differs from the repository's \"{internalName}\"");
            if (!Equals(localVersion, repoVersion))
            {
                report.Error($"{zipPath}: manifest AssemblyVersion {localVersion} differs from the repository's {repoVersion} " +
                             "(\"Distributed plugin version does not match repo version\")");
            }

            if (dalamud.Get<Guid>(local, "WorkingPluginId") != Guid.Empty)
                report.Error($"{zipPath}: the manifest sets WorkingPluginId (\"Plugin shall not specify a WorkingPluginId\")");

            var localApi = dalamud.Get<int>(local, "DalamudApiLevel");
            if (localApi != dalamud.Get<int>(repoEntry, "DalamudApiLevel"))
                report.Error($"{zipPath}: manifest DalamudApiLevel {localApi} differs from the repository's");

            if (dllEntry != null)
                CheckDllVersion(zipPath, dllEntry, localVersion, report);

            foreach (var provided in DalamudProvided)
            {
                if (zip.Entries.Any(e => string.Equals(e.Name, provided, StringComparison.OrdinalIgnoreCase)))
                    report.Warning($"{zipPath}: contains {provided}, which Dalamud already provides");
            }

            var size = zip.Entries.Sum(e => e.Length);
            report.Info($"{zipPath}: {zip.Entries.Count} files, {size.ToString("N0", CultureInfo.InvariantCulture)} bytes unpacked: " +
                        string.Join(", ", zip.Entries.Select(e => e.FullName)));
        }
    }

    private static void CheckDllVersion(string zipPath, ZipArchiveEntry dllEntry, Version? manifestVersion, Report report)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"repojsoncheck-{Guid.NewGuid():N}.dll");
        try
        {
            dllEntry.ExtractToFile(temp, overwrite: true);
            var assemblyVersion = AssemblyName.GetAssemblyName(temp).Version;
            if (!Equals(assemblyVersion, manifestVersion))
                report.Error($"{zipPath}: {dllEntry.FullName} has assembly version {assemblyVersion}, but its manifest says {manifestVersion}");
        }
        catch (Exception ex)
        {
            report.Error($"{zipPath}: cannot read the assembly version of {dllEntry.FullName}: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string Describe(Exception ex)
    {
        while (ex is TargetInvocationException { InnerException: { } inner })
            ex = inner;
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private sealed class Options
    {
        public required string RepoJson { get; init; }

        public string? Zip { get; init; }

        public string? InternalName { get; init; }

        public required string DalamudDir { get; init; }

        public bool Strict { get; init; }

        public bool Verbose { get; init; }

        /// <summary>The parsed options, or null when help was requested.</summary>
        public static Options? Parse(string[] args)
        {
            string? repo = null, zip = null, name = null, dalamud = null;
            var verbose = false;
            var strict = false;
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
                switch (args[i])
                {
                    case "--zip":
                        zip = Value();
                        break;
                    case "--internal-name":
                        name = Value();
                        break;
                    case "--dalamud":
                        dalamud = Value();
                        break;
                    case "--strict":
                        strict = true;
                        break;
                    case "--verbose":
                    case "-v":
                        verbose = true;
                        break;
                    case "--help":
                    case "-h":
                        return null;
                    default:
                        if (args[i].StartsWith('-') || repo != null)
                            throw new ArgumentException($"unexpected argument {args[i]}");
                        repo = args[i];
                        break;
                }
            }

            return new Options
            {
                RepoJson = repo ?? throw new ArgumentException("the repo.json path is required"),
                Zip = zip,
                InternalName = name,
                DalamudDir = dalamud ?? DefaultDalamudDir(),
                Strict = strict,
                Verbose = verbose,
            };
        }

        /// <summary>Same lookup order as Dalamud.NET.Sdk's DalamudLibPath.</summary>
        private static string DefaultDalamudDir()
        {
            var env = Environment.GetEnvironmentVariable("DALAMUD_HOME");
            if (!string.IsNullOrEmpty(env))
                return env;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "addon", "Hooks", "dev");
            if (OperatingSystem.IsMacOS())
                return Path.Combine(home, "Library", "Application Support", "XIV on Mac", "dalamud", "Hooks", "dev");
            return Path.Combine(home, ".xlcore", "dalamud", "Hooks", "dev");
        }
    }

    /// <summary>Dalamud's manifest types and Newtonsoft entry points, reached by reflection.</summary>
    private sealed class DalamudTypes
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;

        private readonly MethodInfo deserialize;

        private DalamudTypes(Assembly dalamud, Assembly newtonsoft, string directory)
        {
            this.Directory = directory;
            this.Version = dalamud.GetName().Version ?? new Version(0, 0);
            this.Remote = dalamud.GetType("Dalamud.Plugin.Internal.Types.Manifest.RemotePluginManifest", throwOnError: true)!;
            this.Local = dalamud.GetType("Dalamud.Plugin.Internal.Types.Manifest.LocalPluginManifest", throwOnError: true)!;

            // PluginManager's static constructor sets DalamudApiLevel = Dalamud's assembly major version.
            try
            {
                var pluginManager = dalamud.GetType("Dalamud.Plugin.Internal.PluginManager", throwOnError: true)!;
                this.ApiLevel = (int)pluginManager.GetProperty("DalamudApiLevel", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            }
            catch (Exception)
            {
                this.ApiLevel = this.Version.Major;
            }

            var common = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, "Dalamud.Common.dll"));
            var gameVersion = common.GetType("Dalamud.Common.Game.GameVersion", throwOnError: true)!;
            this.AnyGameVersion = gameVersion.GetProperty("Any", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

            var jsonConvert = newtonsoft.GetType("Newtonsoft.Json.JsonConvert", throwOnError: true)!;
            this.deserialize = jsonConvert.GetMethod("DeserializeObject", [typeof(string), typeof(Type)])
                               ?? throw new MissingMethodException("JsonConvert.DeserializeObject(string, Type)");

            // The JSON property names Newtonsoft maps for RemotePluginManifest (the default contract resolver
            // is what JsonConvert.DeserializeObject uses without settings).
            var resolverType = newtonsoft.GetType("Newtonsoft.Json.Serialization.DefaultContractResolver", throwOnError: true)!;
            var resolver = Activator.CreateInstance(resolverType)!;
            var contract = resolverType.GetMethod("ResolveContract", [typeof(Type)])!.Invoke(resolver, [this.Remote])!;
            var properties = (IEnumerable)contract.GetType().GetProperty("Properties", Instance)!.GetValue(contract)!;
            var names = new List<string>();
            foreach (var p in properties)
            {
                var t = p.GetType();
                var ignored = (bool)t.GetProperty("Ignored", Instance)!.GetValue(p)!;
                var writable = (bool)t.GetProperty("Writable", Instance)!.GetValue(p)!;
                if (!ignored && writable)
                    names.Add((string)t.GetProperty("PropertyName", Instance)!.GetValue(p)!);
            }

            this.RemoteJsonProperties = names;
        }

        public string Directory { get; }

        public Version Version { get; }

        public int ApiLevel { get; }

        public Type Remote { get; }

        public Type Local { get; }

        public object AnyGameVersion { get; }

        /// <summary>JSON keys Dalamud reads for a repository entry.</summary>
        public IReadOnlyList<string> RemoteJsonProperties { get; }

        public static DalamudTypes Load(string directory)
        {
            directory = Path.GetFullPath(directory);
            var dalamudPath = Path.Combine(directory, "Dalamud.dll");
            if (!File.Exists(dalamudPath))
                throw new FileNotFoundException("Dalamud.dll not found (set --dalamud or DALAMUD_HOME)", dalamudPath);

            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            };

            var newtonsoft = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, "Newtonsoft.Json.dll"));
            var dalamud = AssemblyLoadContext.Default.LoadFromAssemblyPath(dalamudPath);
            return new DalamudTypes(dalamud, newtonsoft, directory);
        }

        public string Describe() =>
            $"Dalamud {this.Version} (API {this.ApiLevel}) from {this.Directory}";

        public IList? DeserializeRemoteList(string json)
        {
            var listType = typeof(List<>).MakeGenericType(this.Remote);
            return (IList?)this.deserialize.Invoke(null, [json, listType]);
        }

        public object? DeserializeLocal(string json) => this.deserialize.Invoke(null, [json, this.Local]);

        public object? GetRaw(object obj, string property) =>
            obj.GetType().GetProperty(property, Instance)?.GetValue(obj);

        public T Get<T>(object obj, string property)
        {
            var info = obj.GetType().GetProperty(property, Instance)
                       ?? throw new MissingMemberException(obj.GetType().Name, property);
            var value = info.GetValue(obj);
            return value is T typed ? typed : default!;
        }

        public void Dump(object obj, string label)
        {
            Console.WriteLine($"  {label} as Dalamud sees it:");
            foreach (var p in obj.GetType().GetProperties(Instance).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (p.Name == "SourceRepo" || p.GetIndexParameters().Length > 0)
                    continue;
                object? value;
                try
                {
                    value = p.GetValue(obj);
                }
                catch (Exception ex)
                {
                    value = "<" + (ex.InnerException ?? ex).GetType().Name + ">";
                }

                if (value is IEnumerable items and not string)
                    value = "[" + string.Join(", ", items.Cast<object?>()) + "]";
                Console.WriteLine($"    {p.Name} = {value ?? "null"}");
            }
        }
    }

    private sealed class Report
    {
        private static readonly bool InGitHubActions =
            string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

        public int Errors { get; private set; }

        public int Warnings { get; private set; }

        public void Error(string message)
        {
            this.Errors++;
            Console.WriteLine("error: " + message);
            if (InGitHubActions)
                Console.WriteLine("::error title=RepoJsonCheck::" + Escape(message));
        }

        public void Warning(string message)
        {
            this.Warnings++;
            Console.WriteLine("warning: " + message);
            if (InGitHubActions)
                Console.WriteLine("::warning title=RepoJsonCheck::" + Escape(message));
        }

        public void Info(string message) => Console.WriteLine("ok: " + message);

        public void Finish(string path, int entries, DalamudTypes dalamud, bool strict)
        {
            var verdict = this.Errors > 0 ? "INVALID" : strict && this.Warnings > 0 ? "INVALID (--strict)" : "valid";
            Console.WriteLine(
                $"{path}: {verdict} for Dalamud {dalamud.Version} (API {dalamud.ApiLevel}): {entries} plugin(s), " +
                $"{this.Errors} error(s), {this.Warnings} warning(s)");
        }

        private static string Escape(string message) =>
            message.Replace("%", "%25", StringComparison.Ordinal)
                   .Replace("\r", "%0D", StringComparison.Ordinal)
                   .Replace("\n", "%0A", StringComparison.Ordinal);
    }
}

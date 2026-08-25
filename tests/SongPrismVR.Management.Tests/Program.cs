using System.Security.Cryptography;
using System.Text.Json;
using SongPrismVR.Management;

List<(string Name, Action Test)> tests = new()
{
    ("Install and uninstall preserve settings and prior collisions", InstallAndUninstall),
    ("Upgrade rollback restores the previous managed version", UpgradeRollback),
    ("Localify paths are rejected and existing files survive", LocalifyProtection),
    ("Modified installed files are preserved with state", ModifiedFileProtection),
    ("Payload preflight rejects corruption before writing", PayloadPreflightProtection),
    ("Product preflight requires clean-install dependencies", ProductDependencyProtection),
    ("Release update version and checksum policy", ReleaseUpdateVersionAndChecksumPolicy)
};
if (args.Length == 1)
{
    string actualPackage = Path.GetFullPath(args[0]);
    tests.Add(("Actual distribution package installs and uninstalls", () =>
        ActualDistributionPackage(actualPackage)));
}

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {exception.Message}");
    }
}
Console.WriteLine($"Executed {tests.Count} management tests; failures: {failures}");
return failures == 0 ? 0 : 1;

static void ReleaseUpdateVersionAndChecksumPolicy()
{
    True(ReleaseUpdatePolicy.IsNewer("0.1.0", "v0.1.1-preview.1"));
    True(ReleaseUpdatePolicy.IsNewer("0.1.1-preview.1", "v0.1.1-preview.2"));
    True(ReleaseUpdatePolicy.IsNewer("0.1.1-preview.99999999999999999999", "v0.1.1-preview.100000000000000000000"));
    True(ReleaseUpdatePolicy.IsNewer("0.1.1-preview.9", "v0.1.1"));
    False(ReleaseUpdatePolicy.IsNewer("0.1.1", "v0.1.1-preview.9"));
    False(ReleaseUpdatePolicy.IsNewer("0.1.2-preview.1", "v0.1.1"));
    Equal("0.1.1-preview.2", ReleaseUpdatePolicy.NormalizeVersion("v0.1.1-preview.2+build.7"));
    Throws<InvalidDataException>(() => ReleaseUpdatePolicy.NormalizeVersion("0.1.1.0"));
    Throws<InvalidDataException>(() => ReleaseUpdatePolicy.NormalizeVersion("0.1.1-preview.01"));
    Throws<InvalidDataException>(() => ReleaseUpdatePolicy.NormalizeVersion("0.1.1+"));
    Equal(
        new string('A', 64),
        ReleaseUpdatePolicy.ParseSha256(
            $"{new string('a', 64)}  SongPrismVR-v0.166.0.zip",
            "SongPrismVR-v0.166.0.zip"));
    Equal(
        new string('B', 64),
        ReleaseUpdatePolicy.ParseSha256(
            $"sha256:{new string('b', 64)}",
            "SongPrismVR-v0.166.0.zip"));
}

static void InstallAndUninstall()
{
    using Fixture fixture = new();
    string originalProxy = fixture.WriteGameFile("winhttp.dll", "original-proxy");
    string package = fixture.CreatePackage("1.0.0", "runtime-one");
    InstallationEngine engine = new();
    InstallationResult installed = engine.Install(fixture.GameRoot, package);
    Equal("1.0.0", installed.Version);
    Equal(LocalifyStatus.Absent, installed.Localify);
    Equal("runtime-one", File.ReadAllText(Path.Combine(fixture.GameRoot, "vrmod/runtime/mod.dll")));

    InstallationResult removed = engine.Uninstall(fixture.GameRoot);
    False(removed.RestoredPreviousVersion);
    Equal(originalProxy, File.ReadAllText(Path.Combine(fixture.GameRoot, "winhttp.dll")));
    True(File.Exists(Path.Combine(fixture.GameRoot, "vrmod/config/settings.json")));
    False(File.Exists(Path.Combine(fixture.GameRoot, "vrmod/install-state.json")));
}

static void UpgradeRollback()
{
    using Fixture fixture = new();
    InstallationEngine engine = new();
    string first = fixture.CreatePackage("1.0.0", "runtime-one");
    engine.Install(fixture.GameRoot, first);
    string second = fixture.CreatePackage("2.0.0", "runtime-two");
    engine.Install(fixture.GameRoot, second);

    InstallationStatus upgraded = engine.Inspect(fixture.GameRoot, second);
    Equal("2.0.0", upgraded.InstalledVersion!);
    True(upgraded.HasPreviousVersion);
    InstallationResult rollback = engine.Uninstall(fixture.GameRoot);
    True(rollback.RestoredPreviousVersion);
    Equal("1.0.0", rollback.RestoredVersion!);
    Equal("runtime-one", File.ReadAllText(Path.Combine(fixture.GameRoot, "vrmod/runtime/mod.dll")));
    Equal("1.0.0", engine.Inspect(fixture.GameRoot, first).InstalledVersion!);
}

static void LocalifyProtection()
{
    using Fixture fixture = new();
    string protectedValue = fixture.WriteGameFile("version.dll", "localify-proxy");
    fixture.WriteGameFile("scsp-config.json", "{}");
    Directory.CreateDirectory(Path.Combine(fixture.GameRoot, "scsp_localify"));
    fixture.WriteGameFile("scsp_localify/localify.json", "{}");
    Equal(LocalifyStatus.Installed, InstallationEngine.DetectLocalify(fixture.GameRoot));
    string package = fixture.CreatePackage("1.0.0", "runtime", includeProtectedPath: true);
    InstallationEngine engine = new();
    try
    {
        engine.Install(fixture.GameRoot, package);
        throw new InvalidOperationException("Protected Localify path was accepted.");
    }
    catch (InstallationException exception) when (exception.Code == "ProtectedLocalifyPath")
    {
    }
    Equal(protectedValue, File.ReadAllText(Path.Combine(fixture.GameRoot, "version.dll")));
    Equal("{}", File.ReadAllText(Path.Combine(fixture.GameRoot, "scsp-config.json")));
}

static void ModifiedFileProtection()
{
    using Fixture fixture = new();
    InstallationEngine engine = new();
    string package = fixture.CreatePackage("1.0.0", "runtime-one");
    engine.Install(fixture.GameRoot, package);
    File.WriteAllText(Path.Combine(fixture.GameRoot, "vrmod/runtime/mod.dll"), "user-change");
    InstallationResult result = engine.Uninstall(fixture.GameRoot);
    True(result.Warnings.Any(warning => warning == "Modified:vrmod/runtime/mod.dll"));
    Equal("user-change", File.ReadAllText(Path.Combine(fixture.GameRoot, "vrmod/runtime/mod.dll")));
    Equal("managed-proxy", File.ReadAllText(Path.Combine(fixture.GameRoot, "winhttp.dll")));
    True(File.Exists(Path.Combine(fixture.GameRoot, "vrmod/install-state.json")));
}

static void PayloadPreflightProtection()
{
    using Fixture fixture = new();
    string package = fixture.CreatePackage("1.0.0", "runtime-one");
    File.WriteAllText(Path.Combine(package, "payload/vrmod/runtime/mod.dll"), "corrupted-after-manifest");
    InstallationEngine engine = new();
    try
    {
        engine.Install(fixture.GameRoot, package);
        throw new InvalidOperationException("Corrupted package was accepted.");
    }
    catch (InstallationException exception) when (exception.Code == "PackageHashMismatch")
    {
    }
    False(File.Exists(Path.Combine(fixture.GameRoot, "winhttp.dll")));
    False(File.Exists(Path.Combine(fixture.GameRoot, "vrmod/install-state.json")));
}

static void ProductDependencyProtection()
{
    using Fixture fixture = new();
    string package = fixture.CreatePackage(
        "1.0.0",
        "runtime-one",
        loader: "winhttp-doorstop");
    InstallationEngine engine = new();
    try
    {
        engine.Install(fixture.GameRoot, package);
        throw new InvalidOperationException("Incomplete product package was accepted.");
    }
    catch (InstallationException exception) when (exception.Code == "PackageRequiredFileMissing")
    {
    }
    False(File.Exists(Path.Combine(fixture.GameRoot, "winhttp.dll")));
    False(File.Exists(Path.Combine(fixture.GameRoot, "vrmod/install-state.json")));
}

static void ActualDistributionPackage(string package)
{
    using Fixture fixture = new();
    string localifyProxy = fixture.WriteGameFile("version.dll", "localify-proxy");
    fixture.WriteGameFile("scsp-config.json", "{}");
    Directory.CreateDirectory(Path.Combine(fixture.GameRoot, "scsp_localify"));
    fixture.WriteGameFile("scsp_localify/localify.json", "{}");
    InstallationEngine engine = new();
    InstallationResult installed = engine.Install(fixture.GameRoot, package);
    string packageVersion = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(package, "package-manifest.json")))
        .RootElement.GetProperty("version").GetString()!;
    Equal(packageVersion, installed.Version);
    Equal(LocalifyStatus.Installed, installed.Localify);
    Equal(localifyProxy, File.ReadAllText(Path.Combine(fixture.GameRoot, "version.dll")));
    True(File.Exists(Path.Combine(
        fixture.GameRoot,
        "vrmod/tools/SongPrismVR.Configurator.exe")));
    True(File.Exists(Path.Combine(
        fixture.GameRoot,
        "vrmod/runtime/openxr_loader.dll")));
    True(File.Exists(Path.Combine(
        fixture.GameRoot,
        "BepInEx/core/dobby.dll")));
    InstallationResult removed = engine.Uninstall(fixture.GameRoot);
    False(removed.RestoredPreviousVersion);
    Equal(0, removed.Warnings.Count);
    Equal(localifyProxy, File.ReadAllText(Path.Combine(fixture.GameRoot, "version.dll")));
    True(File.Exists(Path.Combine(fixture.GameRoot, "vrmod/config/settings.json")));
    False(File.Exists(Path.Combine(fixture.GameRoot, "BepInEx/core/dobby.dll")));

    using Fixture existingDobbyFixture = new();
    string existingDobby = existingDobbyFixture.WriteGameFile(
        "BepInEx/core/dobby.dll",
        "existing-localify-dobby");
    engine.Install(existingDobbyFixture.GameRoot, package);
    Equal(
        existingDobby,
        File.ReadAllText(Path.Combine(existingDobbyFixture.GameRoot, "BepInEx/core/dobby.dll")));
    InstallationResult existingDobbyRemoved = engine.Uninstall(existingDobbyFixture.GameRoot);
    Equal(0, existingDobbyRemoved.Warnings.Count);
    Equal(
        existingDobby,
        File.ReadAllText(Path.Combine(existingDobbyFixture.GameRoot, "BepInEx/core/dobby.dll")));
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("Expected true, got false.");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("Expected false, got true.");
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

internal sealed class Fixture : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "SongPrismVR.Management.Tests",
        Guid.NewGuid().ToString("N"));

    public Fixture()
    {
        GameRoot = Path.Combine(_root, "game");
        Directory.CreateDirectory(GameRoot);
        foreach (string required in new[] { "imasscprism.exe", "GameAssembly.dll", "UnityPlayer.dll" })
        {
            File.WriteAllText(Path.Combine(GameRoot, required), required);
        }
    }

    public string GameRoot { get; }

    public string WriteGameFile(string relative, string content)
    {
        string path = Path.Combine(GameRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return content;
    }

    public string CreatePackage(
        string version,
        string runtime,
        bool includeProtectedPath = false,
        string loader = "test")
    {
        string package = Path.Combine(_root, "packages", version + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(package, "payload");
        Directory.CreateDirectory(payload);
        List<PackageFile> files = new();
        AddPayload(payload, files, "winhttp.dll", "managed-proxy");
        AddPayload(payload, files, "vrmod/runtime/mod.dll", runtime);
        AddPayload(
            payload,
            files,
            "vrmod/config/settings.json",
            "{}",
            preserveExisting: true,
            preserveOnUninstall: true);
        if (includeProtectedPath)
        {
            AddPayload(payload, files, "scsp-config.json", "must-not-install");
        }
        PackageManifest manifest = new()
        {
            SchemaVersion = 1,
            Version = version,
            Loader = loader,
            LocalifyPolicy = "preserve",
            Files = files
        };
        File.WriteAllText(
            Path.Combine(package, "package-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
        return package;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void AddPayload(
        string payload,
        List<PackageFile> files,
        string relative,
        string content,
        bool preserveExisting = false,
        bool preserveOnUninstall = false)
    {
        string path = Path.Combine(payload, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        files.Add(new PackageFile
        {
            Path = relative,
            Sha256 = Hash(path),
            PreserveExisting = preserveExisting,
            PreserveOnUninstall = preserveOnUninstall
        });
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

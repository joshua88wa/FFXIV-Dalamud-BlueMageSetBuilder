using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BlueMageSetBuilder;

public sealed class ProfileFile
{
    public List<Profile> Profiles { get; set; } = new();
}

public sealed class Profile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>In priority order. Each pick fills at most one slot.</summary>
    public List<Pick> Picks { get; set; } = new();
}

public sealed class Pick
{
    /// <summary>
    /// Spellbook numbers. The first one that is learned, not excluded and not already in
    /// the set wins, so a pick can name a weaker spell as a stand-in for a stronger one.
    /// </summary>
    public List<uint> Any { get; set; } = new();

    public string Why { get; set; } = string.Empty;
}

/// <summary>
/// Loads the profiles. The copy embedded in the DLL is the default; a Profiles.json in
/// the plugin's config folder replaces it entirely, so editing your own never merges
/// with the built-in one in surprising ways.
/// </summary>
public sealed class ProfileStore
{
    private const string ResourceName = "BlueMageSetBuilder.Profiles.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public IReadOnlyList<Profile> Profiles { get; private set; } = Array.Empty<Profile>();

    /// <summary>Where the profiles came from, for the settings tab.</summary>
    public string LoadedFrom { get; private set; } = string.Empty;

    public string? LoadError { get; private set; }

    public static string UserFilePath =>
        Path.Combine(Service.Interface.GetPluginConfigDirectory(), "Profiles.json");

    public void Load()
    {
        this.LoadError = null;

        if (File.Exists(UserFilePath))
        {
            try
            {
                this.Profiles = Parse(File.ReadAllText(UserFilePath));
                this.LoadedFrom = UserFilePath;
                return;
            }
            catch (Exception ex)
            {
                // Fall back to the built-in set rather than leaving the window empty,
                // and say why, because a typo in hand-edited JSON is the likely cause.
                this.LoadError = $"Your Profiles.json could not be read, so the built-in profiles are in use: {ex.Message}";
                Service.Log.Warning(ex, "Failed to load user Profiles.json");
            }
        }

        this.Profiles = Parse(ReadBuiltIn());
        this.LoadedFrom = "built-in";
    }

    /// <summary>Copies the built-in file to the config folder so it can be edited.</summary>
    public void ExportBuiltIn()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserFilePath)!);
        File.WriteAllText(UserFilePath, ReadBuiltIn());
    }

    public void DeleteUserFile()
    {
        if (File.Exists(UserFilePath))
            File.Delete(UserFilePath);
    }

    public Profile? Find(string id) =>
        this.Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? this.Profiles.FirstOrDefault();

    private static List<Profile> Parse(string json) =>
        (JsonSerializer.Deserialize<ProfileFile>(json, Options) ?? new ProfileFile()).Profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.Id))
            .ToList();

    private static string ReadBuiltIn()
    {
        using var stream = typeof(ProfileStore).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

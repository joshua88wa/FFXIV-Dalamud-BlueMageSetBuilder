using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BlueMageSetBuilder;

public sealed class ProfileFile
{
    public List<Profile> Profiles { get; set; } = new();

    /// <summary>
    /// Fills any slots left over once a profile's picks run out, so a player who knows
    /// plenty of spells is not left with gaps. Shared by every profile that does not
    /// have its own Fill list. Never reported as worth learning.
    /// </summary>
    public List<Pick> Fill { get; set; } = new();
}

public sealed class Profile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Heading the profile is listed under in the Content menu, such as "Dungeons".</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Masked Carnivale stage number (1-32) for a stage set. Stage sets are not listed in
    /// the Content menu themselves; they appear under the Stage picker instead.
    /// </summary>
    public int? Stage { get; set; }

    /// <summary>In priority order. Each pick fills at most one slot.</summary>
    public List<Pick> Picks { get; set; } = new();

    /// <summary>Optional. Replaces the file's shared Fill list for this profile.</summary>
    public List<Pick> Fill { get; set; } = new();
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

    // Profiles without their own Fill list get the shared one, so SetBuilder only ever
    // has to look at the profile.
    private static List<Profile> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<ProfileFile>(json, Options) ?? new ProfileFile();
        var profiles = file.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.Id)).ToList();

        foreach (var p in profiles.Where(p => p.Fill.Count == 0))
            p.Fill = file.Fill;

        return profiles;
    }

    private static string ReadBuiltIn()
    {
        using var stream = typeof(ProfileStore).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

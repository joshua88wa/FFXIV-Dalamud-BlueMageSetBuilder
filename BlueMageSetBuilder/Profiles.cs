using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>
    /// Tops up Masked Carnivale stage sets after the stage's own spells. Our own list;
    /// the stage spells come from Carnivale.json.
    /// </summary>
    public List<Pick> CarnivaleFill { get; set; } = new();
}

public sealed class Profile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Heading the profile is listed under in the Content menu, such as "Dungeons".</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// Tank, Healer or DPS. When every profile in a Group has one, the window offers a
    /// Role picker for that Group instead of listing the profiles by name.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Masked Carnivale stage number (1-32) for a stage set. Stage sets are not listed in
    /// the Content menu themselves; they appear under the Stage picker instead.
    /// </summary>
    public int? Stage { get; set; }

    /// <summary>Where this set's spells came from, for crediting and linking, if not written here.</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>In priority order. Each pick fills at most one slot.</summary>
    public List<Pick> Picks { get; set; } = new();

    /// <summary>Optional. Replaces the file's shared Fill list for this profile.</summary>
    public List<Pick> Fill { get; set; } = new();
}

public sealed class Pick
{
    /// <summary>
    /// Spellbook numbers in order of preference. The first one that is learned, not
    /// excluded and not already in the set wins, so a later spell can stand in for an
    /// earlier, stronger one.
    /// </summary>
    [JsonIgnore]
    public List<uint> Spells { get; set; } = new();

    /// <summary>"Spell" in Profiles.json: one spellbook number, or a list of them.</summary>
    [JsonConverter(typeof(SpellNumbersConverter))]
    public List<uint> Spell
    {
        get => this.Spells;
        set => this.Spells = value ?? new();
    }

    /// <summary>
    /// The original name for "Spell". Still read, so a Profiles.json edited before
    /// the rename keeps working.
    /// </summary>
    [JsonConverter(typeof(SpellNumbersConverter))]
    public List<uint> Any
    {
        get => this.Spells;
        set => this.Spells = value ?? new();
    }

    public string Why { get; set; } = string.Empty;

    /// <summary>
    /// The content cannot reasonably be done without one of these spells. Set for
    /// Masked Carnivale picks a guide lists as required; the window warns at the top
    /// when none of the spells is learned.
    /// </summary>
    public bool Required { get; set; }
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
    // has to look at the profile. Masked Carnivale stage sets are generated here too,
    // for every stage the file does not define itself.
    private static List<Profile> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<ProfileFile>(json, Options) ?? new ProfileFile();
        var profiles = file.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.Id)).ToList();

        foreach (var p in profiles.Where(p => p.Fill.Count == 0))
            p.Fill = p.Stage is null || file.CarnivaleFill.Count == 0 ? file.Fill : file.CarnivaleFill;

        var carnivaleFill = file.CarnivaleFill.Count > 0 ? file.CarnivaleFill : file.Fill;
        profiles.AddRange(CarnivaleSets.Build(profiles, carnivaleFill));

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

/// <summary>Reads "Spell": 30 as well as "Spell": [ 11, 2 ].</summary>
internal sealed class SpellNumbersConverter : JsonConverter<List<uint>>
{
    public override List<uint> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new List<uint> { reader.GetUInt32() };

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("\"Spell\" must be a spellbook number, or a list of them in square brackets.");

        var list = new List<uint>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException("\"Spell\" lists can only hold spellbook numbers.");
            list.Add(reader.GetUInt32());
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<uint> value, JsonSerializerOptions options)
    {
        if (value.Count == 1)
        {
            writer.WriteNumberValue(value[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var number in value)
            writer.WriteNumberValue(number);
        writer.WriteEndArray();
    }
}
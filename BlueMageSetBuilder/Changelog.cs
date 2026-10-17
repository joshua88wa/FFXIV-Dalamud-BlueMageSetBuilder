using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlueMageSetBuilder;

/// <param name="Version">The version, or "Unreleased" for what is on the testing branch.</param>
/// <param name="Date">The release date as written in the file, or empty.</param>
public sealed record ChangelogEntry(string Version, string Date, IReadOnlyList<string> Items)
{
    public bool IsUnreleased => this.Version.Equals("Unreleased", StringComparison.OrdinalIgnoreCase);

    public string Title => this.IsUnreleased ? "In testing" : $"Version {this.Version}";
}

/// <summary>
/// CHANGELOG.md from the repository root, embedded at build time. Only "## version
/// (date)" headings and "- " bullets (with indented continuation lines) are read.
/// </summary>
public static class Changelog
{
    private const string ResourceName = "BlueMageSetBuilder.CHANGELOG.md";

    private static IReadOnlyList<ChangelogEntry>? entries;

    public static IReadOnlyList<ChangelogEntry> Entries => entries ??= Load();

    /// <summary>
    /// How many entries, from the top, are newer than the one last seen. With nothing
    /// seen yet, or an entry no longer in the file, only the newest counts.
    /// </summary>
    public static int UnseenCount(string lastSeen)
    {
        var list = Entries;
        if (list.Count == 0)
            return 0;

        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Version == lastSeen)
                return i;
        }

        return 1;
    }

    private static IReadOnlyList<ChangelogEntry> Load()
    {
        var result = new List<ChangelogEntry>();
        try
        {
            using var stream = typeof(Changelog).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                return result;

            using var reader = new StreamReader(stream);
            string? version = null;
            var date = string.Empty;
            var items = new List<string>();

            void Flush()
            {
                if (version is not null)
                    result.Add(new ChangelogEntry(version, date, items.ToList()));
                items.Clear();
            }

            foreach (var raw in reader.ReadToEnd().Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    Flush();
                    var m = Regex.Match(line[3..], @"^(.*?)\s*(?:\((.*)\))?$");
                    version = m.Groups[1].Value.Trim();
                    date = m.Groups[2].Value.Trim();
                }
                else if (version is null)
                {
                    continue;
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    items.Add(Plain(line[2..]));
                }
                else if (line.StartsWith("  ", StringComparison.Ordinal) && line.Trim().Length > 0 && items.Count > 0)
                {
                    items[^1] += " " + Plain(line.Trim());
                }
            }

            Flush();
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, "Could not read the changelog");
        }

        return result;
    }

    // The page shows plain text, so drop Markdown emphasis and code marks.
    private static string Plain(string text) => text.Replace("**", string.Empty).Replace("`", string.Empty).Trim();
}

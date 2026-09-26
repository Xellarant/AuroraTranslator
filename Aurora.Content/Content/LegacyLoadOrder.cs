#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Aurora.Content.Preparation;

/// <summary>
/// The order Aurora Legacy loads content files in, which decides who owns an element id.
///
/// Legacy does not merge duplicate declarations: when a later file re-declares an id it removes the
/// element it already had and adds the new one, so the last file to declare an id owns it outright.
/// Its order comes from an explicit ladder of top-level directories, and any directory it does not
/// recognise - which is where installed content packs land - is loaded after all the ones it does.
/// A homebrew pack therefore overrides core and supplements by design, and that is the mechanism
/// books use to revise official material.
///
/// Reproducing the ladder is what lets the database agree with Legacy about who won. The rank alone
/// is not enough: within one bucket Legacy walks a directory's own files before descending into its
/// subdirectories, so "core/zebra.xml" loads before "core/players-handbook/aboleth.xml" even though
/// it sorts after it. <see cref="Compare"/> reproduces that walk.
/// </summary>
internal static class LegacyLoadOrder
{
    /// <summary>Buckets in load order. Later buckets win, so a higher rank takes precedence.</summary>
    private static readonly string[] LadderDirectories =
    [
        "srd",
        "system-reference-document",
        "core",
        "supplements",
        "unearthed-arcana",
        "third-party",
        "homebrew",
    ];

    private const int ResidualRank = 100;      // Any directory the ladder does not name.
    private const int UserSubdirectoryRank = 101;
    private const int ContentRootRank = 102;   // Files sitting directly in the content root.
    private const int UserRank = 103;          // Files directly inside "user".

    /// <summary>
    /// Which bucket a file belongs to. Higher wins. Files under "ignore" are not loaded at all and
    /// never reach this method; they are dropped before the ladder is consulted.
    /// </summary>
    public static int Rank(string relativePath)
    {
        string[] segments = Segments(relativePath);
        if (segments.Length <= 1) return ContentRootRank;

        if (segments[0].Equals("user", StringComparison.OrdinalIgnoreCase))
            return segments.Length == 2 ? UserRank : UserSubdirectoryRank;

        int ladder = Array.FindIndex(LadderDirectories,
            directory => directory.Equals(segments[0], StringComparison.OrdinalIgnoreCase));
        return ladder < 0 ? ResidualRank : ladder;
    }

    /// <summary>True when the file is under an "ignore" directory, which Legacy never loads.</summary>
    public static bool IsIgnored(string relativePath) =>
        Segments(relativePath).SkipLast(1).Any(s => s.Equals("ignore", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Orders two files as Legacy loads them: negative when <paramref name="left"/> loads first.
    /// Within a bucket a directory's own files come before its subdirectories, matching the
    /// recursive walk Legacy performs.
    /// </summary>
    public static int Compare(string left, string right)
    {
        int byRank = Rank(left).CompareTo(Rank(right));
        if (byRank != 0) return byRank;

        string[] a = Segments(left), b = Segments(right);
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            // The last segment is the file name. A file in this directory is walked before any
            // subdirectory of it, so when one path ends here and the other keeps going, this one wins.
            bool aIsFile = i == a.Length - 1, bIsFile = i == b.Length - 1;
            if (aIsFile != bIsFile) return aIsFile ? -1 : 1;

            int bySegment = string.CompareOrdinal(a[i], b[i]);
            if (bySegment != 0) return bySegment;
        }
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>Comparer form of <see cref="Compare"/>, for sorting file lists.</summary>
    public static IComparer<string> Comparer { get; } = Comparer<string>.Create(Compare);

    private static string[] Segments(string relativePath) =>
        relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}

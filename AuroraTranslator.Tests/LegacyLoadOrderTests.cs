using Aurora.Content.Preparation;

/// <summary>
/// Locks down the load order Legacy uses, because it is what decides who owns an element id.
/// These expectations come from DataManager.GetCustomFiles in the decompiled engine, not from
/// anything the importer finds convenient.
/// </summary>
internal static class LegacyLoadOrderTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    /// <summary>True when <paramref name="first"/> is loaded before <paramref name="second"/>.</summary>
    private static bool Before(string first, string second) => LegacyLoadOrder.Compare(first, second) < 0;

    internal static void Ladder()
    {
        Require(Before("srd/a.xml", "core/a.xml"), "srd loads before core.");
        Require(Before("core/a.xml", "supplements/a.xml"), "core loads before supplements.");
        Require(Before("supplements/a.xml", "unearthed-arcana/a.xml"), "supplements load before unearthed arcana.");
        Require(Before("unearthed-arcana/a.xml", "third-party/a.xml"), "unearthed arcana loads before third party.");
        Require(Before("third-party/a.xml", "homebrew/a.xml"), "third party loads before homebrew.");

        // The case the ladder exists for: an installed pack sits in a directory Legacy does not
        // name, is loaded after everything it does name, and therefore overrides official material.
        Require(Before("core/class-ranger.xml", "the-book-of-xellarant/class-ranger-revised.xml"),
            "An unrecognised directory loads after core, so a pack can override official ids.");
        Require(Before("supplements/fizbans/drakewarden.xml", "the-book-of-xellarant/drakewarden.xml"),
            "An unrecognised directory loads after supplements too.");

        // Corrections live under user, which Legacy loads after every content pack.
        Require(Before("the-book-of-xellarant/a.xml", "user/local/fix.xml"), "A user subdirectory loads after packs.");
        Require(Before("user/local/fix.xml", "root-level.xml"), "Content-root files load after user subdirectories.");
        Require(Before("root-level.xml", "user/direct.xml"), "Files directly under user load last of all.");
    }

    internal static void WalkOrderAndIgnores()
    {
        // Legacy walks a directory's own files before descending, so a file in the bucket root
        // loads before one in a subdirectory even when it sorts after it.
        Require(Before("core/zebra.xml", "core/players-handbook/aboleth.xml"),
            "A directory's own files are walked before its subdirectories.");
        Require(Before("core/players-handbook/a.xml", "core/players-handbook/b.xml"),
            "Within one directory, files keep ordinal order.");
        Require(Before("core/a-book/x.xml", "core/b-book/x.xml"), "Sibling directories keep ordinal order.");

        Require(LegacyLoadOrder.Compare("core/a.xml", "core/a.xml") == 0, "A path is not ordered against itself.");
        Require(Before("core/a.xml", "core\b.xml"), "Separators are normalised before comparing.");

        Require(LegacyLoadOrder.IsIgnored("core/ignore/a.xml"), "An ignore directory is recognised.");
        Require(LegacyLoadOrder.IsIgnored("ignore/a.xml"), "A top-level ignore directory is recognised.");
        Require(!LegacyLoadOrder.IsIgnored("core/ignore.xml"), "A file merely named ignore is still loaded.");
    }
}

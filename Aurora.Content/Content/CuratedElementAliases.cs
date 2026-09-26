#nullable enable
using System.Collections.Generic;

namespace Aurora.Content.Preparation;

/// <summary>
/// Forwarding addresses the library knows about, for ids that only ever existed in saved characters.
///
/// Content can declare its own aliases when it renames something, but nothing can speak for an id
/// that was never a content id in the first place - a reference saved from a misspelling, or from a
/// build of upstream content that no longer exists anywhere to declare an alias from. Those belong
/// here.
///
/// Entries are held to the same rule as declared aliases: an id something still declares is never
/// forwarded, and an entry whose target the catalog does not declare is simply inapplicable, which
/// is ordinary rather than an error - not everyone installs the book a target lives in.
///
/// Keep this list short and evidenced. Each entry needs a reference that was observed in a real
/// saved character, and a target whose spelling the catalog actually uses.
/// </summary>
internal static class CuratedElementAliases
{
    internal sealed record Entry(string SavedId, string TargetId, string Note);

    internal static IReadOnlyList<Entry> All { get; } =
    [
        // Observed in saved characters; no content declares the misspelling, and the Dungeon Master's
        // Guide proficiency it means is spelled LASER.
        new("ID_WOTC_DMG_PROFICIENCY_WEAPON_FUTURISTIC_FIREARMS_LASTER_PISTOL",
            "ID_WOTC_DMG_PROFICIENCY_WEAPON_FUTURISTIC_FIREARMS_LASER_PISTOL",
            "Misspelling of the futuristic firearms laser pistol proficiency."),
    ];
}

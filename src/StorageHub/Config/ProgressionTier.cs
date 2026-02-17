using System.Collections.Generic;

namespace StorageHub.Config
{
    /// <summary>
    /// Defines the tier progression system for Storage Hub.
    ///
    /// Design rationale:
    /// - 4 tiers instead of 6: Players don't want to upgrade constantly
    /// - Consumable items instead of boss flags: Player controls timing, feels like investment
    /// - Range applies to both chest access AND crafting station access
    /// - Station memory only at Tier 3+: Early game stays grounded, endgame gets convenience
    /// </summary>
    public static class ProgressionTier
    {
        // Range in pixels (1 tile = 16 pixels)
        public const int Tier0Range = 50 * 16;      // 50 tiles - starter range before first unlock
        public const int Tier1Range = 100 * 16;     // 100 tiles
        public const int Tier2Range = 500 * 16;     // 500 tiles
        public const int Tier3Range = 1000 * 16;    // 1000 tiles
        public const int Tier4Range = int.MaxValue; // Entire world

        // Terraria Item IDs for tier unlocks
        public const int ShadowScaleId = 86;
        public const int TissueSampleId = 1329;
        public const int HellstoneBarId = 175;
        public const int HallowedBarId = 1225;
        public const int LuminiteBarId = 3467;

        // Amount required for each tier
        public const int Tier1UnlockCount = 5;   // Shadow Scale / Tissue Sample
        public const int Tier2UnlockCount = 10;  // Hellstone Bar
        public const int Tier3UnlockCount = 10;  // Hallowed Bar
        public const int Tier4UnlockCount = 10;  // Luminite Bar

        /// <summary>
        /// Get the range for a given tier (in pixels).
        /// </summary>
        public static int GetRange(int tier)
        {
            return tier switch
            {
                0 => Tier0Range,
                1 => Tier1Range,
                2 => Tier2Range,
                3 => Tier3Range,
                4 => Tier4Range,
                _ => tier < 0 ? Tier0Range : Tier4Range
            };
        }

        /// <summary>
        /// Whether station memory feature is available at this tier.
        /// Station memory means crafting stations are remembered forever after visiting.
        /// Pre-Tier 3: Must be physically near stations.
        /// Tier 3+: Stations can be "remembered" forever (if enabled).
        /// Note: Use IsStationMemoryActive() to check if both tier AND toggle allow it.
        /// </summary>
        public static bool HasStationMemory(int tier) => tier >= 3;

        /// <summary>
        /// Get unlock requirements for the next tier.
        /// Returns null if already at max tier.
        /// </summary>
        public static TierUnlockRequirement GetNextTierRequirement(int currentTier)
        {
            return currentTier switch
            {
                0 => new TierUnlockRequirement(1, new[] { ShadowScaleId, TissueSampleId }, Tier1UnlockCount, "Shadow Scale or Tissue Sample"),
                1 => new TierUnlockRequirement(2, new[] { HellstoneBarId }, Tier2UnlockCount, "Hellstone Bar"),
                2 => new TierUnlockRequirement(3, new[] { HallowedBarId }, Tier3UnlockCount, "Hallowed Bar"),
                3 => new TierUnlockRequirement(4, new[] { LuminiteBarId }, Tier4UnlockCount, "Luminite Bar"),
                _ => null
            };
        }

        /// <summary>
        /// Get display name for a tier.
        /// </summary>
        public static string GetTierName(int tier)
        {
            return tier switch
            {
                0 => "Starter",
                1 => "Corruption/Crimson",
                2 => "Hellstone",
                3 => "Hallowed",
                4 => "Lunar",
                _ => "Unknown"
            };
        }
    }

    /// <summary>
    /// Represents the requirements to unlock a tier.
    /// </summary>
    public class TierUnlockRequirement
    {
        public int TargetTier { get; }
        public int[] AcceptedItemIds { get; }
        public int RequiredCount { get; }
        public string DisplayName { get; }

        public TierUnlockRequirement(int targetTier, int[] acceptedItemIds, int requiredCount, string displayName)
        {
            TargetTier = targetTier;
            AcceptedItemIds = acceptedItemIds;
            RequiredCount = requiredCount;
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// Special unlock definitions for non-tile crafting conditions.
    /// These require consumable items because they can't be "visited" like stations.
    /// </summary>
    public static class SpecialUnlocks
    {
        // Liquids
        public const int BottledWaterId = 126;
        public const int BottledHoneyId = 1134;
        public const int ObsidianId = 173;
        public const int AetherBlockId = 5349;  // ShimmerBlock (Aetherium Block), NOT 5364 which is BottomlessShimmerBucket

        // Biomes
        public const int IceBlockId = 664;

        // All tombstone types for graveyard unlock
        public const int TombstoneId = 321;
        public const int GraveMarkerId = 1173;
        public const int CrossGraveMarkerId = 1174;
        public const int HeadstoneId = 1175;
        public const int GravestoneId = 1176;
        public const int ObeliskId = 1177;
        public const int GoldenTombstoneId = 3229;
        public const int GoldenCrossGraveMarkerId = 3230;
        public const int GoldenHeadstoneId = 3231;
        public const int GoldenGravestoneId = 3232;
        public const int GoldenObeliskId = 3233;

        public static readonly int[] AllTombstoneIds = new[]
        {
            TombstoneId, GraveMarkerId, CrossGraveMarkerId, HeadstoneId,
            GravestoneId, ObeliskId, GoldenTombstoneId, GoldenCrossGraveMarkerId,
            GoldenHeadstoneId, GoldenGravestoneId, GoldenObeliskId
        };

        // Altars (use boss materials as proxy)
        // Shadow Scale for Demon Altar, Tissue Sample for Crimson Altar

        public static readonly Dictionary<string, SpecialUnlockDefinition> Definitions = new Dictionary<string, SpecialUnlockDefinition>
        {
            // Liquids
            ["water"] = new SpecialUnlockDefinition("Water Crafting", BottledWaterId, 20, "Allows crafting recipes that require water"),
            ["honey"] = new SpecialUnlockDefinition("Honey Crafting", BottledHoneyId, 20, "Allows crafting recipes that require honey"),
            ["lava"] = new SpecialUnlockDefinition("Lava Crafting", ObsidianId, 20, "Allows crafting recipes that require lava"),

            // Shimmer (unified: crafting + decrafting)
            ["shimmer"] = new SpecialUnlockDefinition("Shimmer", AetherBlockId, 10, "Enables shimmer crafting recipes AND the Shimmer decrafting tab"),

            // Biomes
            ["snow"] = new SpecialUnlockDefinition("Snow Biome (Ice)", IceBlockId, 50, "Allows crafting recipes that require snow biome"),
            ["graveyard"] = new SpecialUnlockDefinition("Graveyard / Ecto Mist", AllTombstoneIds, 10, "Allows crafting recipes that require graveyard"),

            // Altars
            ["demonAltar"] = new SpecialUnlockDefinition("Demon Altar", ProgressionTier.ShadowScaleId, 10, "Allows crafting at Demon Altar"),
            ["crimsonAltar"] = new SpecialUnlockDefinition("Crimson Altar", ProgressionTier.TissueSampleId, 10, "Allows crafting at Crimson Altar")
        };
    }

    /// <summary>
    /// Defines a special unlock that requires consuming items.
    /// </summary>
    public class SpecialUnlockDefinition
    {
        public string DisplayName { get; }
        public int[] AcceptedItemIds { get; }  // Multiple items can satisfy this unlock
        public int RequiredCount { get; }
        public string Description { get; }

        /// <summary>Primary item ID for display purposes (first in array).</summary>
        public int RequiredItemId => AcceptedItemIds[0];

        public SpecialUnlockDefinition(string displayName, int requiredItemId, int requiredCount, string description)
            : this(displayName, new[] { requiredItemId }, requiredCount, description)
        {
        }

        public SpecialUnlockDefinition(string displayName, int[] acceptedItemIds, int requiredCount, string description)
        {
            DisplayName = displayName;
            AcceptedItemIds = acceptedItemIds;
            RequiredCount = requiredCount;
            Description = description;
        }
    }

    /// <summary>
    /// Relay system constants.
    /// Relays extend range to specific areas without upgrading tier.
    /// </summary>
    public static class RelayConstants
    {
        public const int RelayRadius = 200 * 16;  // 200 tiles in pixels
        public const int MaxRelays = 10;          // Prevents unlimited range cheese
    }
}

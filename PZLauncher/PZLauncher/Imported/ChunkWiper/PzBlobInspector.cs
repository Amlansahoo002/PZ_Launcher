#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PZ_ChunkWiper
{
    internal static class PzBlobInspector
    {
        private const int CommonPrefixLength = 0x1B;
        private static readonly string[] CharacterStatNames =
        {
            "Anger", "Boredom", "Discomfort", "Endurance", "Fatigue", "Fitness",
            "FoodSickness", "Hunger", "Idleness", "Intoxication", "Morale", "NicotineWithdrawal",
            "Pain", "Panic", "Poison", "Sanity", "Sickness", "Stress",
            "Temperature", "Thirst", "Unhappiness", "Wetness", "ZombieFever", "ZombieInfection"
        };
        private static readonly string[] BodyPartNames =
        {
            "Hand_L", "Hand_R", "ForeArm_L", "ForeArm_R", "UpperArm_L", "UpperArm_R",
            "Torso_Upper", "Torso_Lower", "Head", "Neck", "Groin", "UpperLeg_L",
            "UpperLeg_R", "LowerLeg_L", "LowerLeg_R", "Foot_L", "Foot_R"
        };
        private static readonly string[] BloodBodyPartNames =
        {
            "Hand_L", "Hand_R", "ForeArm_L", "ForeArm_R", "UpperArm_L", "UpperArm_R",
            "Torso_Upper", "Torso_Lower", "Head", "Neck", "Groin", "UpperLeg_L",
            "UpperLeg_R", "LowerLeg_L", "LowerLeg_R", "Foot_L", "Foot_R", "Back"
        };
        private static readonly string[] DirectionNames = { "N", "NW", "W", "SW", "S", "SE", "E", "NE" };
        private static readonly object RegistryNameLock = new object();
        private static readonly IReadOnlyDictionary<ushort, string> EmptyRegistryNames = new Dictionary<ushort, string>();
        private static string _cachedRegistryPath = "";
        private static IReadOnlyDictionary<ushort, string> _cachedRegistryNames = EmptyRegistryNames;
        
        private static readonly Dictionary<string, int> KnownWeaponMaxAmmo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Base.Shotgun", 6 }
        };
        private static readonly Dictionary<string, string> ProfessionNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "base:burglar", "Burglar" },
            { "base:burgerflipper", "Burger Flipper" },
            { "base:carpenter", "Carpenter" },
            { "base:chef", "Chef" },
            { "base:constructionworker", "Construction Worker" },
            { "base:doctor", "Doctor" },
            { "base:electrician", "Electrician" },
            { "base:engineer", "Engineer" },
            { "base:farmer", "Farmer" },
            { "base:fireofficer", "Fire Officer" },
            { "base:fisherman", "Fisherman" },
            { "base:fitnessInstructor", "Fitness Instructor" },
            { "base:lumberjack", "Lumberjack" },
            { "base:mechanics", "Mechanic" },
            { "base:metalworker", "Metalworker" },
            { "base:nurse", "Nurse" },
            { "base:parkranger", "Park Ranger" },
            { "base:policeofficer", "Police Officer" },
            { "base:rancher", "Rancher" },
            { "base:repairman", "Repairman" },
            { "base:securityguard", "Security Guard" },
            { "base:smither", "Smither" },
            { "base:tailor", "Tailor" },
            { "base:unemployed", "Unemployed" },
            { "base:veteran", "Veteran" }
        };

        public static string Describe(
            string dbFileName,
            string tableName,
            string columnName,
            byte[] data,
            IReadOnlyDictionary<string, object> rowValues = null,
            string databasePath = null)
        {
            if (data == null || data.Length == 0)
                return "Empty BLOB";

            var registryNames = LoadRegistryNames(databasePath);
            var output = new StringBuilder();
            output.AppendLine($"{dbFileName} / {tableName}.{columnName}");
            output.AppendLine($"Length: {data.Length:N0} bytes");
            output.AppendLine("Byte order: BigEndian / Java ByteBuffer");
            output.AppendLine();

            TryReadKahluaTable(data, CommonPrefixLength, 0, out KahluaTablePreview modDataPreview);
            TryReadPlayerDescriptor(data, out PlayerDescriptorPreview playerPreview);

            AppendRowValues(output, rowValues);
            AppendCommonPrefix(output, data, rowValues);
            AppendDatabaseHints(output, dbFileName, data);
            AppendLuaModDataPreview(output, data, modDataPreview);
            AppendPlayerIdentity(output, dbFileName, playerPreview);
            AppendPlayerVisualAndInventory(output, dbFileName, data, playerPreview, registryNames, rowValues);
            AppendRowMatches(output, data, rowValues);
            AppendNumericExplorer(output, data, rowValues);
            AppendStrings(output, data, playerPreview);

            output.AppendLine();
            output.AppendLine("First bytes:");
            output.AppendLine(BuildHexDump(data, 0, Math.Min(data.Length, 320)));

            return output.ToString();
        }

        public static string DescribeCharacterSheet(
            string dbFileName,
            string tableName,
            string columnName,
            byte[] data,
            IReadOnlyDictionary<string, object> rowValues = null,
            string databasePath = null)
        {
            if (data == null || data.Length == 0)
                return "No character data.";

            if (!dbFileName.Equals("players.db", StringComparison.OrdinalIgnoreCase))
                return "Character sheet is available for players.db BLOB rows.";

            var registryNames = LoadRegistryNames(databasePath);
            TryReadPlayerDescriptor(data, out PlayerDescriptorPreview player);

            HumanVisualPreview humanVisual = null;
            InventoryPreview inventory = null;
            CharacterStatsPreview stats = null;
            BodyDamagePreview bodyDamage = null;
            XpPreview xp = null;
            IsoGameCharacterTailPreview characterTail = null;
            IsoPlayerPreview isoPlayer = null;
            List<InventoryItemPreview> savedItems = new List<InventoryItemPreview>();
            List<ReadBookPreview> readBooks = new List<ReadBookPreview>();

            if (player != null && player.Success && player.HasDescriptor && player.NextOffset > 0 &&
                TryReadHumanVisualPreview(data, player.NextOffset, out humanVisual) &&
                TryReadItemContainerPreview(data, humanVisual.NextOffset, out inventory))
            {
                ApplyRegistryNames(inventory, registryNames);
                savedItems = ExpandSavedInventoryItems(inventory);

                if (TryReadCharacterStatsPreview(data, inventory.NextOffset, out stats))
                    TryReadBodyDamagePreview(data, stats.NextOffset, out bodyDamage);

                TryFindIsoPlayerPreview(data, inventory.NextOffset, savedItems, out isoPlayer);
                int continuationEnd = isoPlayer != null && isoPlayer.Success ? isoPlayer.StartOffset : data.Length;
                if (TryFindXpPreview(data, inventory.NextOffset, continuationEnd, out xp))
                    TryReadIsoGameCharacterTailPreview(data, xp.NextOffset, out characterTail);

                readBooks = FindReadBookProgress(data, inventory.NextOffset, continuationEnd).ToList();
            }

            var output = new StringBuilder();
            output.AppendLine("Character sheet");
            output.AppendLine($"{dbFileName} / {tableName}.{columnName} - {data.Length:N0} bytes");
            output.AppendLine();

            AppendCharacterSheetIdentity(output, player, humanVisual, xp, rowValues);
            AppendCharacterSheetPosition(output, data, rowValues);
            AppendCharacterSheetStatus(output, rowValues, inventory, characterTail);
            AppendCharacterSheetStats(output, stats, isoPlayer, xp, readBooks);
            AppendCharacterSheetHealth(output, stats, bodyDamage);
            AppendCharacterSheetEquipment(output, inventory, savedItems, isoPlayer, characterTail);

            if (inventory == null)
            {
                output.AppendLine();
                output.AppendLine("Parser note");
                output.AppendLine("  The player descriptor was found, but the visual/inventory continuation could not be decoded safely yet.");
                output.AppendLine("  Keep the raw BLOB inspector below as the reference for this row.");
            }

            return output.ToString();
        }

        public static CharacterEditorSnapshot CreateCharacterEditorSnapshot(
            string dbFileName,
            string tableName,
            string columnName,
            byte[] data,
            IReadOnlyDictionary<string, object> rowValues = null,
            string databasePath = null)
        {
            var snapshot = new CharacterEditorSnapshot
            {
                Source = dbFileName + " / " + tableName + "." + columnName,
                Supported = dbFileName.Equals("players.db", StringComparison.OrdinalIgnoreCase)
            };

            if (!snapshot.Supported)
            {
                snapshot.Note = "Character editor data is available for players.db BLOB rows.";
                return snapshot;
            }

            if (data == null || data.Length == 0)
            {
                snapshot.Note = "No character data.";
                return snapshot;
            }

            snapshot.BlobLength = data.Length;
            snapshot.X = ReadSingleBE(data, 0x0A);
            snapshot.Y = ReadSingleBE(data, 0x0E);
            snapshot.Z = ReadSingleBE(data, 0x12);
            snapshot.Direction = FormatDirection(ReadInt32BE(data, 0x16));
            snapshot.HasPosition = true;

            if (TryGetBool(rowValues, "isDead", out bool isDead))
            {
                snapshot.HasDeathFlag = true;
                snapshot.IsDead = isDead;
            }

            if (TryGetInt(rowValues, "worldversion", out int worldVersion))
                snapshot.WorldVersion = worldVersion;

            var registryNames = LoadRegistryNames(databasePath);
            TryReadPlayerDescriptor(data, out PlayerDescriptorPreview player);

            if (player != null && player.Success && player.HasDescriptor)
            {
                snapshot.FirstName = player.Forename;
                snapshot.LastName = player.Surname;
                snapshot.Model = player.Torso;
                snapshot.Gender = FormatGender(player.Gender);
                snapshot.ProfessionId = player.Profession;
                snapshot.ProfessionName = FormatProfession(player.Profession);
            }
            else
            {
                snapshot.FullNameFallback = GetRowText(rowValues, "name", "");
            }

            HumanVisualPreview humanVisual = null;
            InventoryPreview inventory = null;
            CharacterStatsPreview stats = null;
            BodyDamagePreview bodyDamage = null;
            XpPreview xp = null;
            IsoGameCharacterTailPreview characterTail = null;
            IsoPlayerPreview isoPlayer = null;
            List<InventoryItemPreview> savedItems = new List<InventoryItemPreview>();

            if (player != null && player.Success && player.HasDescriptor && player.NextOffset > 0 &&
                TryReadHumanVisualPreview(data, player.NextOffset, out humanVisual))
            {
                snapshot.Hair = humanVisual.HairModel;
                snapshot.Beard = humanVisual.BeardModel;

                if (TryReadItemContainerPreview(data, humanVisual.NextOffset, out inventory))
                {
                    ApplyRegistryNames(inventory, registryNames);
                    savedItems = ExpandSavedInventoryItems(inventory);
                    snapshot.InventoryGroups = inventory.Items.Count;
                    snapshot.InventoryItems = inventory.TotalItems;
                    snapshot.HasInventory = true;

                    if (TryReadCharacterStatsPreview(data, inventory.NextOffset, out stats))
                    {
                        snapshot.HasStats = true;
                        foreach (var stat in stats.Stats)
                            snapshot.Stats[stat.Name] = stat.Value;

                        TryReadBodyDamagePreview(data, stats.NextOffset, out bodyDamage);
                    }

                    TryFindIsoPlayerPreview(data, inventory.NextOffset, savedItems, out isoPlayer);
                    int continuationEnd = isoPlayer != null && isoPlayer.Success ? isoPlayer.StartOffset : data.Length;

                    if (TryFindXpPreview(data, inventory.NextOffset, continuationEnd, out xp))
                    {
                        snapshot.HasXp = true;
                        foreach (string trait in xp.Traits)
                            snapshot.Traits.Add(FormatTraitName(trait));
                        foreach (var level in xp.PerkLevels)
                            snapshot.PerkLevels[level.Name] = level.Value;
                        foreach (var entry in xp.XpEntries)
                            snapshot.XpEntries[entry.Name] = entry.Value;

                        if (TryReadIsoGameCharacterTailPreview(data, xp.NextOffset, out characterTail))
                        {
                            snapshot.HasCharacterTail = true;
                            snapshot.OnFire = characterTail.OnFire;
                            snapshot.Sneaking = characterTail.Sneaking;
                            snapshot.DeathDragDown = characterTail.DeathDragDown;
                        }
                    }

                    foreach (var book in FindReadBookProgress(data, inventory.NextOffset, continuationEnd).Take(24))
                        snapshot.ReadBooks.Add(book.FullType + ": page " + book.AlreadyReadPages.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (isoPlayer != null && isoPlayer.Success)
            {
                snapshot.HasIsoPlayer = true;
                snapshot.HoursSurvived = isoPlayer.HoursSurvived;
                snapshot.ZombieKills = isoPlayer.ZombieKills;
                snapshot.SurvivorKills = isoPlayer.SurvivorKills;
                snapshot.PrimaryHand = FormatSavedItemReference(isoPlayer.PrimaryHandIndex, savedItems);
                snapshot.SecondaryHand = FormatSavedItemReference(isoPlayer.SecondaryHandIndex, savedItems);

                if (isoPlayer.Nutrition != null)
                {
                    snapshot.HasNutrition = true;
                    snapshot.Weight = isoPlayer.Nutrition.Weight;
                    snapshot.Calories = isoPlayer.Nutrition.Calories;
                    snapshot.Proteins = isoPlayer.Nutrition.Proteins;
                    snapshot.Lipids = isoPlayer.Nutrition.Lipids;
                    snapshot.Carbohydrates = isoPlayer.Nutrition.Carbohydrates;
                }
            }
            else if (characterTail != null && characterTail.Success)
            {
                snapshot.PrimaryHand = FormatSavedItemReference(characterTail.LeftHandInventoryIndex, savedItems);
                snapshot.SecondaryHand = FormatSavedItemReference(characterTail.RightHandInventoryIndex, savedItems);
            }

            if (bodyDamage != null && bodyDamage.Success)
            {
                snapshot.HasBodyDamage = true;
                foreach (var part in bodyDamage.Parts)
                {
                    snapshot.BodyParts.Add(new CharacterBodyPartSnapshot
                    {
                        Name = part.Name,
                        Health = part.Health,
                        Flags = FormatBodyPartFlags(part),
                        Pain = part.AdditionalPain,
                        Stiffness = part.Stiffness,
                        Wetness = part.Wetness
                    });
                }

                if (bodyDamage.Thermoregulator != null)
                {
                    snapshot.HasThermoregulator = true;
                    snapshot.BodySetPoint = bodyDamage.Thermoregulator.SetPoint;
                    foreach (var node in bodyDamage.Thermoregulator.Nodes)
                    {
                        var bodyPart = snapshot.BodyParts.FirstOrDefault(x => x.Name.Equals(node.BodyPartName, StringComparison.OrdinalIgnoreCase));
                        if (bodyPart == null)
                            continue;

                        bodyPart.CoreTemperature = node.Celsius;
                        bodyPart.SkinTemperature = node.SkinCelsius;
                        bodyPart.HasTemperature = true;
                    }
                }
            }

            snapshot.Note = snapshot.HasInventory ? "Draft editor values loaded." : "Character continuation is not fully decoded for this row.";
            return snapshot;
        }

        private static void AppendCharacterSheetIdentity(
            StringBuilder output,
            PlayerDescriptorPreview player,
            HumanVisualPreview humanVisual,
            XpPreview xp,
            IReadOnlyDictionary<string, object> rowValues)
        {
            output.AppendLine("Identity");
            if (player == null || !player.Success || !player.HasDescriptor)
            {
                output.AppendLine("  Name: " + GetRowText(rowValues, "name", "unknown"));
                output.AppendLine("  SurvivorDesc: not decoded");
                return;
            }

            string fullName = (FirstNonEmpty(player.Forename, "") + " " + FirstNonEmpty(player.Surname, "")).Trim();
            output.AppendLine("  Name: " + FirstNonEmpty(fullName, GetRowText(rowValues, "name", "unknown")));
            output.AppendLine("  Profession: " + FormatProfession(player.Profession) + " (" + FirstNonEmpty(player.Profession, "unknown") + ")");
            output.AppendLine("  Gender: " + FormatGender(player.Gender));
            output.AppendLine("  Model/preset: " + FirstNonEmpty(player.Torso, "unknown"));

            if (humanVisual != null && humanVisual.Success)
            {
                string visual = FormatHumanVisualDetails(humanVisual);
                if (!string.IsNullOrWhiteSpace(visual))
                    output.AppendLine("  Visual: " + visual);
            }

            if (xp != null && xp.Success && xp.Traits.Count > 0)
                output.AppendLine("  Traits: " + string.Join(", ", xp.Traits.Select(FormatTraitName)));
        }

        private static void AppendCharacterSheetPosition(
            StringBuilder output,
            byte[] data,
            IReadOnlyDictionary<string, object> rowValues)
        {
            output.AppendLine();
            output.AppendLine("Position");
            output.AppendLine(
                "  Blob: x=" + FormatSingle(ReadSingleBE(data, 0x0A)) +
                ", y=" + FormatSingle(ReadSingleBE(data, 0x0E)) +
                ", z=" + FormatSingle(ReadSingleBE(data, 0x12)) +
                ", direction=" + FormatDirection(ReadInt32BE(data, 0x16)));

            var rowParts = new List<string>();
            foreach (string key in new[] { "wx", "wy", "x", "y", "z", "worldversion" })
            {
                if (rowValues != null && rowValues.TryGetValue(key, out object value))
                    rowParts.Add(key + "=" + FormatObject(value));
            }

            if (rowParts.Count > 0)
                output.AppendLine("  SQLite row: " + string.Join(", ", rowParts));
        }

        private static void AppendCharacterSheetStatus(
            StringBuilder output,
            IReadOnlyDictionary<string, object> rowValues,
            InventoryPreview inventory,
            IsoGameCharacterTailPreview tail)
        {
            output.AppendLine();
            output.AppendLine("Status");

            bool hasDeathFlag = TryGetBool(rowValues, "isDead", out bool isDead);
            output.AppendLine("  Life state: " + (hasDeathFlag ? isDead ? "Dead" : "Alive" : "Unknown"));

            if (tail != null && tail.Success)
            {
                output.AppendLine("  On fire: " + FormatBool(tail.OnFire));
                output.AppendLine("  Sneaking: " + FormatBool(tail.Sneaking));
                output.AppendLine("  Death drag-down: " + FormatBool(tail.DeathDragDown));

                string cheats = FormatActiveCheats(tail);
                if (!string.IsNullOrEmpty(cheats))
                    output.AppendLine("  Active cheats: " + cheats);
            }

            if (inventory != null)
            {
                output.AppendLine(
                    "  players.db inventory: " +
                    inventory.Items.Count.ToString(CultureInfo.InvariantCulture) + " group(s), " +
                    inventory.TotalItems.ToString(CultureInfo.InvariantCulture) + " saved item(s)");

                if (hasDeathFlag && isDead && inventory.TotalItems == 0)
                    output.AppendLine("  Note: the player inventory is empty here; carried items probably moved to reanimated.bin/world zombie data.");
            }
        }

        private static void AppendCharacterSheetStats(
            StringBuilder output,
            CharacterStatsPreview stats,
            IsoPlayerPreview isoPlayer,
            XpPreview xp,
            IReadOnlyList<ReadBookPreview> readBooks)
        {
            output.AppendLine();
            output.AppendLine("Stats and skills");

            if (isoPlayer != null && isoPlayer.Success)
            {
                output.AppendLine(
                    "  Survival: hours=" + FormatDouble(isoPlayer.HoursSurvived) +
                    ", zombieKills=" + isoPlayer.ZombieKills.ToString(CultureInfo.InvariantCulture) +
                    ", survivorKills=" + isoPlayer.SurvivorKills.ToString(CultureInfo.InvariantCulture));

                if (isoPlayer.Nutrition != null)
                {
                    output.AppendLine(
                        "  Nutrition: weight=" + FormatSingle(isoPlayer.Nutrition.Weight) +
                        ", calories=" + FormatSingle(isoPlayer.Nutrition.Calories) +
                        ", proteins=" + FormatSingle(isoPlayer.Nutrition.Proteins) +
                        ", lipids=" + FormatSingle(isoPlayer.Nutrition.Lipids) +
                        ", carbs=" + FormatSingle(isoPlayer.Nutrition.Carbohydrates));
                }
            }

            if (stats != null)
            {
                output.AppendLine("  Core stats: " + FormatSelectedCharacterStats(stats));
                var moodles = BuildDerivedMoodles(stats, null);
                if (moodles.Count > 0)
                    output.AppendLine("  Moodles: " + string.Join(", ", moodles.Take(12)));
            }
            else
            {
                output.AppendLine("  Core stats: not decoded");
            }

            if (xp != null && xp.Success)
            {
                if (xp.PerkLevels.Count > 0)
                    output.AppendLine("  Perk levels: " + FormatPerkLevelsForSheet(xp.PerkLevels));

                var xpEntries = xp.XpEntries
                    .Where(x => Math.Abs(x.Value) > 0.0001f)
                    .OrderByDescending(x => x.Value)
                    .Take(10)
                    .Select(x => x.Name + "=" + FormatSingle(x.Value))
                    .ToList();
                if (xpEntries.Count > 0)
                    output.AppendLine("  XP: " + string.Join(", ", xpEntries));
            }

            if (readBooks != null && readBooks.Count > 0)
            {
                output.AppendLine("  Read books:");
                foreach (var book in readBooks.Take(8))
                    output.AppendLine("    " + book.FullType + ": page " + book.AlreadyReadPages.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AppendCharacterSheetHealth(
            StringBuilder output,
            CharacterStatsPreview stats,
            BodyDamagePreview bodyDamage)
        {
            output.AppendLine();
            output.AppendLine("Health");

            if (bodyDamage == null || !bodyDamage.Success)
            {
                output.AppendLine("  Body damage: not decoded");
                return;
            }

            output.AppendLine("  Summary: " + FormatBodyDamageSummary(bodyDamage));

            var bodyMoodles = BuildDerivedMoodles(stats, bodyDamage);
            if (bodyMoodles.Count > 0)
                output.AppendLine("  Health moodles: " + string.Join(", ", bodyMoodles.Take(12)));

            var affected = bodyDamage.Parts
                .Where(part => !IsCleanBodyPart(part))
                .Take(12)
                .Select(FormatBodyPartDamage)
                .ToList();
            if (affected.Count == 0)
            {
                output.AppendLine("  Body parts: all clean / 100 health");
            }
            else
            {
                output.AppendLine("  Affected body parts:");
                foreach (string line in affected)
                    output.AppendLine("    " + line);
            }

            var effectLines = bodyDamage.Parts
                .Where(HasBodyPartEffect)
                .Take(12)
                .Select(FormatBodyPartEffect)
                .ToList();
            if (effectLines.Count > 0)
            {
                output.AppendLine("  Pain / strain / wetness:");
                foreach (string line in effectLines)
                    output.AppendLine("    " + line);
            }

            AppendCharacterSheetThermal(output, bodyDamage);
        }

        private static void AppendCharacterSheetEquipment(
            StringBuilder output,
            InventoryPreview inventory,
            IReadOnlyList<InventoryItemPreview> savedItems,
            IsoPlayerPreview isoPlayer,
            IsoGameCharacterTailPreview tail)
        {
            output.AppendLine();
            output.AppendLine("Equipment and inventory");

            if (inventory == null)
            {
                output.AppendLine("  Inventory: not decoded");
                return;
            }

            if (isoPlayer != null && isoPlayer.Success)
            {
                if (isoPlayer.PrimaryHandIndex >= 0 && isoPlayer.PrimaryHandIndex == isoPlayer.SecondaryHandIndex)
                    output.AppendLine("  Hands: both -> " + FormatSavedItemReference(isoPlayer.PrimaryHandIndex, savedItems));
                else
                {
                    output.AppendLine("  Primary: " + FormatSavedItemReference(isoPlayer.PrimaryHandIndex, savedItems));
                    output.AppendLine("  Secondary: " + FormatSavedItemReference(isoPlayer.SecondaryHandIndex, savedItems));
                }

                output.AppendLine("  Worn:");
                foreach (var worn in isoPlayer.WornItems.Take(16))
                    output.AppendLine("    " + worn.Location + " -> " + FormatSavedItemReference(worn.InventoryIndex, savedItems));

                var protectionLines = BuildProtectionLines(isoPlayer.WornItems, savedItems);
                if (protectionLines.Count > 0)
                {
                    output.AppendLine("  Protection:");
                    foreach (string line in protectionLines.Take(18))
                        output.AppendLine("    " + line);
                }
            }
            else if (tail != null && tail.Success)
            {
                output.AppendLine("  Left hand index: " + FormatSavedItemReference(tail.LeftHandInventoryIndex, savedItems));
                output.AppendLine("  Right hand index: " + FormatSavedItemReference(tail.RightHandInventoryIndex, savedItems));
            }

            var attached = BuildAttachedItemLines(savedItems).Take(12).ToList();
            if (attached.Count > 0)
            {
                output.AppendLine("  Attached items:");
                foreach (string line in attached)
                    output.AppendLine("    " + line);
            }

            if (inventory.TotalItems == 0)
            {
                output.AppendLine("  Stored items: none in players.db");
            }
            else
            {
                output.AppendLine("  Stored items:");
                foreach (var item in inventory.Items.Take(18))
                    output.AppendLine("    " + FormatInventoryItemForSheet(item));
            }

            var ammunition = new List<string>();
            CollectAmmunitionLines(inventory, "inventory", ammunition);
            if (ammunition.Count > 0)
            {
                output.AppendLine("  Ammunition:");
                foreach (string line in ammunition.Take(12))
                    output.AppendLine("    " + line.Trim());
            }
        }

        private static void AppendCharacterSheetThermal(StringBuilder output, BodyDamagePreview bodyDamage)
        {
            var thermoregulator = bodyDamage?.Thermoregulator;
            if (thermoregulator == null)
            {
                if (!string.IsNullOrWhiteSpace(bodyDamage?.ThermoregulatorNote))
                    output.AppendLine("  Temperature: " + bodyDamage.ThermoregulatorNote);
                return;
            }

            output.AppendLine(
                "  Temperature: setPoint=" + FormatSingle(thermoregulator.SetPoint) +
                ", metabolic=" + FormatSingle(thermoregulator.MetabolicRateReal) +
                ", bodyHeatDelta=" + FormatSingle(thermoregulator.BodyHeatDelta) +
                ", coreHeatDelta=" + FormatSingle(thermoregulator.CoreHeatDelta));

            var hotNodes = thermoregulator.Nodes
                .Where(node => Math.Abs(node.Celsius - thermoregulator.SetPoint) > 0.05f ||
                               Math.Abs(node.SkinCelsius - 33f) > 0.05f ||
                               Math.Abs(node.HeatDelta) > 0.0001f ||
                               Math.Abs(node.Insulation) > 0.0001f ||
                               Math.Abs(node.BodyWetness) > 0.0001f ||
                               Math.Abs(node.ClothingWetness) > 0.0001f)
                .Take(12)
                .Select(FormatThermalNode)
                .ToList();

            if (hotNodes.Count > 0)
            {
                output.AppendLine("  Body part temperatures:");
                foreach (string line in hotNodes)
                    output.AppendLine("    " + line);
            }
        }

        private static void AppendRowValues(StringBuilder output, IReadOnlyDictionary<string, object> rowValues)
        {
            if (rowValues == null || rowValues.Count == 0)
                return;

            output.AppendLine("SQLite row values:");

            var preferred = new[]
            {
                "id", "name", "wx", "wy", "x", "y", "z", "worldversion", "isDead"
            };

            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in preferred)
            {
                if (rowValues.TryGetValue(key, out object value))
                {
                    output.AppendLine($"  {key}: {FormatObject(value)}");
                    written.Add(key);
                }
            }

            foreach (var item in rowValues.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (written.Contains(item.Key))
                    continue;

                output.AppendLine($"  {item.Key}: {FormatObject(item.Value)}");
            }

            output.AppendLine();
        }

        private static void AppendCommonPrefix(
            StringBuilder output,
            byte[] data,
            IReadOnlyDictionary<string, object> rowValues)
        {
            output.AppendLine("Common object prefix:");
            output.AppendLine("  Source: IsoPlayer/IsoMovingObject/BaseVehicle save path");
            output.AppendLine("  The first two bytes can be read as ushort 257, but PZ also reads them as serialize + class id.");
            output.AppendLine();
            output.AppendLine("  Offset  Type      Field               Value                         Notes");
            output.AppendLine("  ------  --------  ------------------  ----------------------------  ------------------------------");

            var header = ReadUInt16BE(data, 0x00);
            AppendField(output, 0x00, "ushort", "header/version", $"{header} (0x{header:X4})", "same bytes as serialize/classId");
            AppendField(output, 0x00, "byte", "serialize", FormatByte(data, 0x00), "IsoMovingObject.Serialize()");
            AppendField(output, 0x01, "byte", "classId", FormatByte(data, 0x01), "IsoObject factory class id");
            AppendField(output, 0x02, "float", "offsetX", FormatSingle(ReadSingleBE(data, 0x02)), "sprite/world offset");
            AppendField(output, 0x06, "float", "offsetY", FormatSingle(ReadSingleBE(data, 0x06)), "sprite/world offset");
            AppendField(output, 0x0A, "float", "x", FormatSingle(ReadSingleBE(data, 0x0A)), CompareRowSingle(rowValues, "x", ReadSingleBE(data, 0x0A)));
            AppendField(output, 0x0E, "float", "y", FormatSingle(ReadSingleBE(data, 0x0E)), CompareRowSingle(rowValues, "y", ReadSingleBE(data, 0x0E)));
            AppendField(output, 0x12, "float", "z", FormatSingle(ReadSingleBE(data, 0x12)), CompareRowSingle(rowValues, "z", ReadSingleBE(data, 0x12)));

            int direction = ReadInt32BE(data, 0x16);
            AppendField(output, 0x16, "int", "direction", $"{direction} ({FormatDirection(direction)})", "IsoDirections ordinal");
            AppendField(output, 0x1A, "byte", "modData flag", FormatByte(data, 0x1A), "1 = Lua table follows before subtype data");
            output.AppendLine();
        }

        private static void AppendDatabaseHints(StringBuilder output, string dbFileName, byte[] data)
        {
            bool hasModData = ReadByte(data, 0x1A) != 0;
            output.AppendLine("Known continuation:");

            if (dbFileName.Equals("vehicles.db", StringComparison.OrdinalIgnoreCase))
            {
                output.AppendLine("  BaseVehicle writes fixed fields after the common prefix when modData flag is 0.");
                if (hasModData)
                {
                    output.AppendLine("  modData flag is 1, so vehicle offsets after 0x1A are shifted by a Lua table.");
                    output.AppendLine();
                    return;
                }

                AppendVehicleContinuation(output, data);
                output.AppendLine();
                return;
            }

            if (dbFileName.Equals("players.db", StringComparison.OrdinalIgnoreCase))
            {
                output.AppendLine("  IsoPlayer delegates to IsoGameCharacter, then writes player-only values.");
                output.AppendLine("  After 0x1A the data quickly becomes variable-length: descriptor, visual, inventory, stats, body damage, XP.");
                if (hasModData)
                    output.AppendLine("  modData flag is 1, so 0x1B starts a Lua table. The descriptor flag follows after that table.");
                else
                    AppendField(output, CommonPrefixLength, "byte", "descriptor?", FormatByte(data, CommonPrefixLength), "probable SurvivorDesc presence flag");

                output.AppendLine();
                return;
            }

            output.AppendLine("  No database-specific continuation is known yet.");
            output.AppendLine();
        }

        private static void AppendVehicleContinuation(StringBuilder output, byte[] data)
        {
            int offset = CommonPrefixLength;
            output.AppendLine();
            output.AppendLine("  Offset  Type      Field               Value                         Notes");
            output.AppendLine("  ------  --------  ------------------  ----------------------------  ------------------------------");
            AppendField(output, offset + 0x00, "float", "physicsZ", FormatSingle(ReadSingleBE(data, offset + 0x00)), "BaseVehicle.savedPhysicsZ");
            AppendField(output, offset + 0x04, "float", "rot.x", FormatSingle(ReadSingleBE(data, offset + 0x04)), "vehicle rotation quaternion");
            AppendField(output, offset + 0x08, "float", "rot.y", FormatSingle(ReadSingleBE(data, offset + 0x08)), "vehicle rotation quaternion");
            AppendField(output, offset + 0x0C, "float", "rot.z", FormatSingle(ReadSingleBE(data, offset + 0x0C)), "vehicle rotation quaternion");
            AppendField(output, offset + 0x10, "float", "rot.w", FormatSingle(ReadSingleBE(data, offset + 0x10)), "vehicle rotation quaternion");

            if (TryReadPzUtfString(data, offset + 0x14, out string scriptName, out int nextOffset))
            {
                AppendField(output, offset + 0x14, "string", "scriptName", scriptName, "GameWindow.WriteString");
                AppendField(output, nextOffset, "int", "skinIndex", ReadInt32BE(data, nextOffset).ToString(CultureInfo.InvariantCulture), "BaseVehicle.skinIndex");
                AppendField(output, nextOffset + 0x04, "byte", "engineOn", FormatByte(data, nextOffset + 0x04), "1 = engine running");
                AppendField(output, nextOffset + 0x05, "int", "frontDurability", ReadInt32BE(data, nextOffset + 0x05).ToString(CultureInfo.InvariantCulture), "frontEndDurability");
                AppendField(output, nextOffset + 0x09, "int", "rearDurability", ReadInt32BE(data, nextOffset + 0x09).ToString(CultureInfo.InvariantCulture), "rearEndDurability");
                AppendField(output, nextOffset + 0x0D, "int", "curFrontDur", ReadInt32BE(data, nextOffset + 0x0D).ToString(CultureInfo.InvariantCulture), "currentFrontEndDurability");
                AppendField(output, nextOffset + 0x11, "int", "curRearDur", ReadInt32BE(data, nextOffset + 0x11).ToString(CultureInfo.InvariantCulture), "currentRearEndDurability");
                AppendField(output, nextOffset + 0x15, "int", "engineLoudness", ReadInt32BE(data, nextOffset + 0x15).ToString(CultureInfo.InvariantCulture), "BaseVehicle.engineLoudness");
                AppendField(output, nextOffset + 0x19, "int", "engineQuality", ReadInt32BE(data, nextOffset + 0x19).ToString(CultureInfo.InvariantCulture), "BaseVehicle.engineQuality");
                AppendField(output, nextOffset + 0x1D, "int", "keyId", ReadInt32BE(data, nextOffset + 0x1D).ToString(CultureInfo.InvariantCulture), "BaseVehicle.keyId");
            }
            else
            {
                AppendField(output, offset + 0x14, "string", "scriptName", "<not decoded>", "expected GameWindow.WriteString");
            }
        }

        private static void AppendLuaModDataPreview(StringBuilder output, byte[] data, KahluaTablePreview table)
        {
            if (ReadByte(data, 0x1A) == 0)
                return;

            output.AppendLine("Lua modData preview:");

            if (table == null || !table.Success)
            {
                output.AppendLine("  Unable to decode the Kahlua table safely.");
                output.AppendLine();
                return;
            }

            output.AppendLine($"  table @0x001B, entries={table.Count}, next={FormatOffset(table.NextOffset)}");
            output.AppendLine("  Type guess: KahluaTableImpl uses string/double/table values here.");

            foreach (var entry in table.Entries.Take(24))
                output.AppendLine($"  @{FormatOffset(entry.Offset)}  {entry.Key} = {entry.Value}");

            if (table.Entries.Count > 24)
                output.AppendLine($"  ... {table.Entries.Count - 24} more entrie(s)");

            var favoriteCounters = table.Entries
                .Where(x => x.Key.StartsWith("Fav:", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Key.Substring(4) + "=" + x.Value)
                .ToList();
            if (favoriteCounters.Count > 0)
                output.AppendLine("  Favorite weapon counters: " + string.Join(", ", favoriteCounters));

            output.AppendLine("  hotbar is player modData. In this sample it maps slot 1 to Back, and real attached objects should appear there when equipped.");
            output.AppendLine();
        }

        private static void AppendPlayerIdentity(StringBuilder output, string dbFileName, PlayerDescriptorPreview player)
        {
            if (!dbFileName.Equals("players.db", StringComparison.OrdinalIgnoreCase))
                return;

            output.AppendLine("Player identity hints:");

            if (player == null || !player.Success)
            {
                output.AppendLine("  SurvivorDesc was not decoded yet.");
                output.AppendLine();
                return;
            }

            AppendField(output, player.DescriptorFlagOffset, "byte", "descriptor flag", player.HasDescriptor ? "1" : "0", "1 = SurvivorDesc follows");
            if (!player.HasDescriptor)
            {
                output.AppendLine();
                return;
            }

            AppendField(output, player.IdOffset, "int", "descriptor id", player.Id.ToString(CultureInfo.InvariantCulture), "SurvivorDesc.id");
            AppendField(output, player.ForenameOffset, "string", "first name", player.Forename, "SurvivorDesc.forename");
            AppendField(output, player.SurnameOffset, "string", "last name", player.Surname, "SurvivorDesc.surname");
            AppendField(output, player.TorsoOffset, "string", "torso/model", player.Torso, "legacy model/preset value; Bob/Kate may not match current gender");
            AppendField(output, player.GenderOffset, "int", "gender", $"{player.Gender} ({FormatGender(player.Gender)})", "1 = female, otherwise male");
            AppendField(output, player.ProfessionOffset, "string", "profession", $"{player.Profession} ({FormatProfession(player.Profession)})", "CharacterProfession registry id");
            AppendField(output, player.ExtraFlagOffset, "int", "extra flag", player.ExtraFlag.ToString(CultureInfo.InvariantCulture), "1 = extra SurvivorDesc strings follow");

            if (player.Extra.Count > 0)
                AppendField(output, player.ExtraFlagOffset, "strings", "extra", string.Join(", ", player.Extra), "SurvivorDesc.extra");

            AppendField(output, player.XpBoostCountOffset, "int", "XP boosts", player.XpBoosts.Count.ToString(CultureInfo.InvariantCulture), "SurvivorDesc.XPBoostMap");
            foreach (var boost in player.XpBoosts)
                AppendField(output, boost.Offset, "perk", boost.Name, boost.Level.ToString(CultureInfo.InvariantCulture), "profession/trait XP boost");

            if (!string.IsNullOrEmpty(player.VoicePrefix))
            {
                AppendField(output, player.VoicePrefixOffset, "string", "voice prefix", player.VoicePrefix, "SurvivorDesc.voicePrefix");
                AppendField(output, player.VoicePitchOffset, "float", "voice pitch", FormatSingle(player.VoicePitch), "SurvivorDesc.voicePitch");
                AppendField(output, player.VoiceTypeOffset, "int", "voice type", player.VoiceType.ToString(CultureInfo.InvariantCulture), "SurvivorDesc.voiceType");
            }

            output.AppendLine("  Network note: FakeClientManager.writePlayerConnectData writes the same logical player profile fields and shows the Bob/Kate model value should not be trusted as gender.");
            output.AppendLine();
        }

        private static void AppendPlayerVisualAndInventory(
            StringBuilder output,
            string dbFileName,
            byte[] data,
            PlayerDescriptorPreview player,
            IReadOnlyDictionary<ushort, string> registryNames,
            IReadOnlyDictionary<string, object> rowValues)
        {
            if (!dbFileName.Equals("players.db", StringComparison.OrdinalIgnoreCase))
                return;

            output.AppendLine("Player visual and inventory preview:");

            if (player == null || !player.Success || player.NextOffset <= 0)
            {
                output.AppendLine("  Waiting for a stable SurvivorDesc endpoint before parsing HumanVisual and inventory.");
                output.AppendLine();
                return;
            }

            if (!TryReadHumanVisualPreview(data, player.NextOffset, out HumanVisualPreview humanVisual))
            {
                output.AppendLine($"  HumanVisual was not decoded safely at {FormatOffset(player.NextOffset)}.");
                output.AppendLine();
                return;
            }

            output.AppendLine(
                $"  HumanVisual @{FormatOffset(humanVisual.StartOffset)} -> {FormatOffset(humanVisual.NextOffset)}: " +
                $"flags1=0x{humanVisual.Flags1:X2}, flags2=0x{humanVisual.Flags2:X2}, " +
                $"bodyHair={humanVisual.BodyHair}, skinTexture={humanVisual.SkinTexture}, zombieRotStage={humanVisual.ZombieRotStage}");
            string humanVisualDetails = FormatHumanVisualDetails(humanVisual);
            if (!string.IsNullOrEmpty(humanVisualDetails))
                output.AppendLine("  HumanVisual details: " + humanVisualDetails);
            output.AppendLine(
                "  Body arrays: " +
                $"blood {FormatArrayPreview(humanVisual.Blood)}, " +
                $"dirt {FormatArrayPreview(humanVisual.Dirt)}, " +
                $"holes {FormatArrayPreview(humanVisual.Holes)}");

            if (humanVisual.BodyVisuals.Count == 0)
            {
                output.AppendLine("  Worn/body visuals: none");
            }
            else
            {
                output.AppendLine($"  Worn/body visuals: {humanVisual.BodyVisuals.Count}");
                foreach (var visual in humanVisual.BodyVisuals.Take(16))
                    output.AppendLine("    " + FormatItemVisualSummary(visual));

                if (humanVisual.BodyVisuals.Count > 16)
                    output.AppendLine($"    ... {humanVisual.BodyVisuals.Count - 16} more visual(s)");
            }

            if (!TryReadItemContainerPreview(data, humanVisual.NextOffset, out InventoryPreview inventory))
            {
                output.AppendLine($"  Inventory container was not decoded safely at {FormatOffset(humanVisual.NextOffset)}.");
                output.AppendLine("  This usually means a preceding visual block still needs one more field mapped.");
                output.AppendLine();
                return;
            }

            ApplyRegistryNames(inventory, registryNames);

            output.AppendLine(
                $"  Inventory @{FormatOffset(inventory.StartOffset)} -> {FormatOffset(inventory.NextOffset)}: " +
                $"type='{inventory.ContainerType}', explored={FormatBool(inventory.Explored)}, " +
                $"groups={inventory.Items.Count}, totalItems={inventory.TotalItems}, " +
                $"looted={FormatBool(inventory.HasBeenLooted)}, capacity={inventory.Capacity}");

            foreach (var item in inventory.Items.Take(40))
                output.AppendLine("    " + FormatInventoryItemSummary(item));

            if (inventory.Items.Count > 40)
                output.AppendLine($"    ... {inventory.Items.Count - 40} more item group(s)");

            AppendPlayerContinuationPreview(output, data, inventory, rowValues);

            output.AppendLine("  Note: each inventory item is length-prefixed; unsupported subtype payloads are skipped safely.");
            output.AppendLine();
        }

        private static void AppendPlayerContinuationPreview(
            StringBuilder output,
            byte[] data,
            InventoryPreview inventory,
            IReadOnlyDictionary<string, object> rowValues)
        {
            var savedItems = ExpandSavedInventoryItems(inventory);
            CharacterStatsPreview stats = null;
            BodyDamagePreview bodyDamage = null;
            IsoGameCharacterTailPreview characterTail = null;

            if (TryReadCharacterStatsPreview(data, inventory.NextOffset, out stats))
            {
                output.AppendLine(
                    $"  Character state @{FormatOffset(stats.StartOffset)}: " +
                    $"asleep={FormatBool(stats.Asleep)}, forceWakeUp={FormatSingle(stats.ForceWakeUpTime)}, " +
                    FormatSelectedCharacterStats(stats));

                if (TryReadBodyDamagePreview(data, stats.NextOffset, out bodyDamage))
                {
                    output.AppendLine("  " + FormatBodyDamageSummary(bodyDamage));
                    AppendBodyThermalPreview(output, bodyDamage);
                    AppendBodyPartEffects(output, bodyDamage);
                }

                AppendDerivedMoodles(output, stats, bodyDamage);
            }

            TryFindIsoPlayerPreview(data, inventory.NextOffset, savedItems, out IsoPlayerPreview isoPlayer);
            int continuationEnd = isoPlayer != null && isoPlayer.Success ? isoPlayer.StartOffset : data.Length;

            XpPreview xp = null;
            if (TryFindXpPreview(data, inventory.NextOffset, continuationEnd, out xp))
            {
                output.AppendLine(
                    $"  XP/perks @{FormatOffset(xp.StartOffset)}: " +
                    $"traits={FormatStringHints(xp.Traits)}, totalXp={FormatSingle(xp.TotalXp)}, " +
                    $"xp={FormatXpEntries(xp.XpEntries)}, levels={FormatPerkLevels(xp.PerkLevels)}");

                if (TryReadIsoGameCharacterTailPreview(data, xp.NextOffset, out characterTail))
                    output.AppendLine("  " + FormatIsoGameCharacterTail(characterTail));
            }

            var readBooks = FindReadBookProgress(data, inventory.NextOffset, continuationEnd).ToList();
            if (readBooks.Count > 0)
            {
                output.AppendLine("  Read books:");
                foreach (var book in readBooks.Take(12))
                    output.AppendLine($"    @{FormatOffset(book.Offset)} {book.FullType}: page {book.AlreadyReadPages}");
            }

            if (isoPlayer != null && isoPlayer.Success)
            {
                output.AppendLine(
                    $"  IsoPlayer @{FormatOffset(isoPlayer.StartOffset)}: " +
                    $"hoursSurvived={FormatDouble(isoPlayer.HoursSurvived)}, " +
                    $"zombieKills={isoPlayer.ZombieKills}, survivorKills={isoPlayer.SurvivorKills}, " +
                    $"primary={FormatSavedItemReference(isoPlayer.PrimaryHandIndex, savedItems)}, " +
                    $"secondary={FormatSavedItemReference(isoPlayer.SecondaryHandIndex, savedItems)}" +
                    FormatNutritionSuffix(isoPlayer.Nutrition));

                AppendHandLoadout(output, isoPlayer, savedItems);
                AppendAttachedItems(output, savedItems);

                output.AppendLine($"  Worn items: {isoPlayer.WornItems.Count}");
                foreach (var worn in isoPlayer.WornItems.Take(24))
                    output.AppendLine($"    {worn.Location} -> {FormatSavedItemReference(worn.InventoryIndex, savedItems)}");

                AppendProtectionHints(output, isoPlayer.WornItems, savedItems);
            }

            AppendAmmunitionPreview(output, inventory);
            AppendDeathState(output, rowValues, inventory, bodyDamage, characterTail);
        }

        private static void AppendRowMatches(
            StringBuilder output,
            byte[] data,
            IReadOnlyDictionary<string, object> rowValues)
        {
            if (rowValues == null || rowValues.Count == 0)
                return;

            output.AppendLine("Matches against SQLite row:");
            bool wroteAny = false;

            foreach (string key in new[] { "x", "y", "z" })
            {
                if (!TryGetDouble(rowValues, key, out double value))
                    continue;

                if (Math.Abs(value) <= 0.0001 && string.Equals(key, "z", StringComparison.OrdinalIgnoreCase))
                {
                    output.AppendLine($"  row {key}={FormatDouble(value)} -> 0x0012 (known z; zero appears many times)");
                    wroteAny = true;
                    continue;
                }

                var matches = FindFloatOffsets(data, (float)value, 0.005f, 16).ToList();
                output.AppendLine($"  row {key}={FormatDouble(value)} -> {FormatOffsets(matches)}");
                wroteAny = true;
            }

            foreach (string key in new[] { "wx", "wy", "worldversion" })
            {
                if (!TryGetInt(rowValues, key, out int value))
                    continue;

                if (value == 0 || value == 1)
                {
                    output.AppendLine($"  row {key}={value} -> skipped; 0/1 is too common in serialized data");
                    wroteAny = true;
                    continue;
                }

                var matches = FindIntOffsets(data, value, 12).ToList();
                output.AppendLine($"  row {key}={value} -> {FormatOffsets(matches)}");
                wroteAny = true;
            }

            if (!wroteAny)
                output.AppendLine("  no comparable scalar values in this row");

            output.AppendLine();
        }

        private static void AppendNumericExplorer(
            StringBuilder output,
            byte[] data,
            IReadOnlyDictionary<string, object> rowValues)
        {
            output.AppendLine("Plausible float values in first 192 bytes:");
            output.AppendLine("  Offset  Float                         Notes");
            output.AppendLine("  ------  ----------------------------  ------------------------------");

            var hits = new List<NumericHit>();
            int scanEnd = Math.Min(data.Length - 4, 192);
            for (int offset = 0; offset <= scanEnd; offset++)
            {
                float value = ReadSingleBE(data, offset);
                if (!IsInterestingFloat(value))
                    continue;

                string note = NoteFloatMatch(rowValues, value);
                bool known = offset == 0x02 || offset == 0x06 || offset == 0x0A || offset == 0x0E || offset == 0x12;
                if (!known && string.IsNullOrEmpty(note) && Math.Abs(value) < 0.01f)
                    continue;

                hits.Add(new NumericHit(offset, value, known ? KnownFloatName(offset) : note));
            }

            foreach (var hit in hits.Take(80))
                AppendField(output, hit.Offset, "float", "", FormatSingle(hit.Value), hit.Note);

            if (hits.Count == 0)
                output.AppendLine("  none");
            else if (hits.Count > 80)
                output.AppendLine($"  ... {hits.Count - 80} more candidate(s)");

            output.AppendLine();
        }

        private static void AppendStrings(StringBuilder output, byte[] data, PlayerDescriptorPreview player)
        {
            var strings = FindPzUtfStrings(data)
                .GroupBy(x => x.Value)
                .Select(x => x.OrderBy(y => y.Offset).First())
                .OrderBy(x => x.Offset)
                .Take(120)
                .ToList();

            output.AppendLine("Detected PZ UTF strings:");
            output.AppendLine("  Format: 2-byte BigEndian length + UTF-8 bytes (GameWindow.WriteString)");

            if (strings.Count == 0)
            {
                output.AppendLine("  none");
            }
            else
            {
                foreach (var item in strings)
                {
                    string note = DescribeStringHit(item, player);
                    output.AppendLine($"  @{FormatOffset(item.Offset)} len={item.ByteLength:D3}  {item.Value}{note}");
                }
            }

            output.AppendLine();
        }

        private static void AppendField(
            StringBuilder output,
            int offset,
            string type,
            string field,
            string value,
            string notes)
        {
            output.AppendLine(
                $"  {FormatOffset(offset),-6}  {type,-8}  {field,-18}  {value,-28}  {notes}");
        }

        private static bool TryReadPlayerDescriptor(byte[] data, out PlayerDescriptorPreview preview)
        {
            preview = null;
            int descriptorFlagOffset = CommonPrefixLength;

            if (ReadByte(data, 0x1A) != 0 &&
                TryReadKahluaTable(data, CommonPrefixLength, 0, out KahluaTablePreview table) &&
                table.Success)
            {
                descriptorFlagOffset = table.NextOffset;
            }

            if (descriptorFlagOffset < 0 || descriptorFlagOffset >= data.Length)
                return false;

            bool hasDescriptor = ReadByte(data, descriptorFlagOffset) != 0;
            preview = new PlayerDescriptorPreview
            {
                Success = true,
                DescriptorFlagOffset = descriptorFlagOffset,
                HasDescriptor = hasDescriptor
            };

            if (!hasDescriptor)
                return true;

            int offset = descriptorFlagOffset + 1;
            if (offset + 4 > data.Length)
                return false;

            preview.IdOffset = offset;
            preview.Id = ReadInt32BE(data, offset);
            offset += 4;

            preview.ForenameOffset = offset;
            if (!TryReadPzUtfString(data, offset, out string forename, out offset))
                return false;
            preview.Forename = forename;

            preview.SurnameOffset = offset;
            if (!TryReadPzUtfString(data, offset, out string surname, out offset))
                return false;
            preview.Surname = surname;

            preview.TorsoOffset = offset;
            if (!TryReadPzUtfString(data, offset, out string torso, out offset))
                return false;
            preview.Torso = torso;

            if (offset + 4 > data.Length)
                return false;
            preview.GenderOffset = offset;
            preview.Gender = ReadInt32BE(data, offset);
            offset += 4;

            preview.ProfessionOffset = offset;
            if (!TryReadPzUtfString(data, offset, out string profession, out offset))
                return false;
            preview.Profession = profession;

            if (offset + 4 > data.Length)
                return true;

            preview.ExtraFlagOffset = offset;
            preview.ExtraFlag = ReadInt32BE(data, offset);
            offset += 4;
            if (preview.ExtraFlag == 1 && offset + 4 <= data.Length)
            {
                int extraCount = ReadInt32BE(data, offset);
                offset += 4;
                if (extraCount >= 0 && extraCount <= 128)
                {
                    for (int i = 0; i < extraCount; i++)
                    {
                        if (!TryReadPzUtfString(data, offset, out string extra, out offset))
                            break;

                        preview.Extra.Add(extra);
                    }
                }
            }

            if (offset + 4 <= data.Length)
            {
                preview.XpBoostCountOffset = offset;
                int boostCount = ReadInt32BE(data, offset);
                offset += 4;
                if (boostCount >= 0 && boostCount <= 128)
                {
                    for (int i = 0; i < boostCount; i++)
                    {
                        int boostOffset = offset;
                        if (!TryReadPzUtfString(data, offset, out string perkName, out offset) || offset + 4 > data.Length)
                            break;

                        int level = ReadInt32BE(data, offset);
                        offset += 4;
                        preview.XpBoosts.Add(new PerkBoostPreview(boostOffset, perkName, level));
                    }
                }
            }

            if (TryReadPzUtfString(data, offset, out string voicePrefix, out int afterVoicePrefix) &&
                afterVoicePrefix + 8 <= data.Length)
            {
                preview.VoicePrefixOffset = offset;
                preview.VoicePrefix = voicePrefix;
                offset = afterVoicePrefix;
                preview.VoicePitchOffset = offset;
                preview.VoicePitch = ReadSingleBE(data, offset);
                offset += 4;
                preview.VoiceTypeOffset = offset;
                preview.VoiceType = ReadInt32BE(data, offset);
                offset += 4;
            }

            preview.NextOffset = offset;
            return true;
        }

        private static bool TryReadHumanVisualPreview(byte[] data, int offset, out HumanVisualPreview preview)
        {
            preview = new HumanVisualPreview
            {
                StartOffset = offset,
                NextOffset = offset
            };

            if (offset < 0 || offset >= data.Length)
                return false;

            int cursor = offset;
            preview.Flags1 = data[cursor++];

            if ((preview.Flags1 & 4) != 0 && !TrySkip(data, ref cursor, 3))
                return false;
            if ((preview.Flags1 & 2) != 0 && !TrySkip(data, ref cursor, 3))
                return false;
            if ((preview.Flags1 & 8) != 0 && !TrySkip(data, ref cursor, 3))
                return false;

            if (cursor + 3 > data.Length)
                return false;

            preview.BodyHair = data[cursor++];
            preview.SkinTexture = data[cursor++];
            preview.ZombieRotStage = data[cursor++];

            string skinTextureName = "";
            if ((preview.Flags1 & 0x40) != 0 &&
                !TryReadPzUtfString(data, cursor, out skinTextureName, out cursor))
                return false;
            preview.SkinTextureName = skinTextureName;

            string beardModel = "";
            if ((preview.Flags1 & 0x10) != 0 &&
                !TryReadPzUtfString(data, cursor, out beardModel, out cursor))
                return false;
            preview.BeardModel = beardModel;

            string hairModel = "";
            if ((preview.Flags1 & 0x20) != 0 &&
                !TryReadPzUtfString(data, cursor, out hairModel, out cursor))
                return false;
            preview.HairModel = hairModel;

            if (!TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview blood) ||
                !TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview dirt) ||
                !TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview holes))
                return false;

            preview.Blood = blood;
            preview.Dirt = dirt;
            preview.Holes = holes;

            if (cursor >= data.Length)
                return false;

            int bodyVisualCount = data[cursor++];
            if (bodyVisualCount > 96)
                return false;

            for (int i = 0; i < bodyVisualCount; i++)
            {
                if (!TryReadItemVisualPreview(data, cursor, out ItemVisualPreview visual))
                    return false;

                preview.BodyVisuals.Add(visual);
                cursor = visual.NextOffset;
            }

            if (!TryReadPzUtfString(data, cursor, out string nonAttachedHair, out cursor))
                return false;
            preview.NonAttachedHair = nonAttachedHair;

            if (cursor >= data.Length)
                return false;

            preview.Flags2 = data[cursor++];
            if ((preview.Flags2 & 4) != 0 && !TrySkip(data, ref cursor, 3))
                return false;
            if ((preview.Flags2 & 2) != 0 && !TrySkip(data, ref cursor, 3))
                return false;

            preview.NextOffset = cursor;
            preview.Success = true;
            return true;
        }

        private static bool TryReadItemVisualPreview(byte[] data, int offset, out ItemVisualPreview preview)
        {
            preview = new ItemVisualPreview
            {
                StartOffset = offset,
                NextOffset = offset
            };

            if (offset < 0 || offset >= data.Length)
                return false;

            int cursor = offset;
            preview.Flags1 = data[cursor++];

            preview.FullTypeOffset = cursor;
            if (!TryReadPzUtfString(data, cursor, out string fullType, out cursor))
                return false;
            preview.FullType = fullType;

            preview.AlternateModelOffset = cursor;
            if (!TryReadPzUtfString(data, cursor, out string alternateModelName, out cursor))
                return false;
            preview.AlternateModelName = alternateModelName;

            preview.ClothingItemOffset = cursor;
            if (!TryReadPzUtfString(data, cursor, out string clothingItemName, out cursor))
                return false;
            preview.ClothingItemName = clothingItemName;

            if ((preview.Flags1 & 1) != 0 && !TrySkip(data, ref cursor, 3))
                return false;
            if ((preview.Flags1 & 2) != 0 && !TrySkip(data, ref cursor, 1))
                return false;
            if ((preview.Flags1 & 4) != 0 && !TrySkip(data, ref cursor, 1))
                return false;
            if ((preview.Flags1 & 8) != 0 && !TrySkip(data, ref cursor, 4))
                return false;
            string decal = "";
            if ((preview.Flags1 & 0x10) != 0 &&
                !TryReadPzUtfString(data, cursor, out decal, out cursor))
                return false;
            preview.Decal = decal;

            if (!TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview blood) ||
                !TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview dirt) ||
                !TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview holes) ||
                !TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview basicPatches) ||
                !TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview denimPatches) ||
                !TryReadByteArrayPreview(data, ref cursor, out ByteArrayPreview leatherPatches))
                return false;

            preview.Blood = blood;
            preview.Dirt = dirt;
            preview.Holes = holes;
            preview.BasicPatches = basicPatches;
            preview.DenimPatches = denimPatches;
            preview.LeatherPatches = leatherPatches;
            preview.NextOffset = cursor;
            preview.Success = true;
            return true;
        }

        private static bool TryReadItemContainerPreview(byte[] data, int offset, out InventoryPreview preview)
        {
            preview = new InventoryPreview
            {
                StartOffset = offset,
                NextOffset = offset
            };

            int cursor = offset;
            if (!TryReadPzUtfString(data, cursor, out string containerType, out cursor))
                return false;

            if (cursor + 3 > data.Length)
                return false;

            preview.ContainerType = containerType;
            preview.Explored = data[cursor++] != 0;
            preview.GroupCountOffset = cursor;
            int groupCount = ReadInt16BE(data, cursor);
            cursor += 2;

            if (groupCount < 0 || groupCount > 4096)
                return false;

            for (int i = 0; i < groupCount; i++)
            {
                if (!TryReadInventoryItemGroup(data, ref cursor, out InventoryItemPreview item))
                    return false;

                preview.Items.Add(item);
                preview.TotalItems += item.IdenticalCount;
            }

            if (cursor + 5 > data.Length)
                return false;

            preview.HasBeenLootedOffset = cursor;
            preview.HasBeenLooted = data[cursor++] != 0;
            preview.CapacityOffset = cursor;
            preview.Capacity = ReadInt32BE(data, cursor);
            cursor += 4;

            preview.NextOffset = cursor;
            preview.Success = true;
            return true;
        }

        private static bool TryReadInventoryItemGroup(byte[] data, ref int cursor, out InventoryItemPreview item)
        {
            item = null;
            int groupOffset = cursor;

            if (cursor + 8 > data.Length)
                return false;

            int identicalCount = ReadInt32BE(data, cursor);
            cursor += 4;
            if (identicalCount <= 0 || identicalCount > 100000)
                return false;

            int dataLengthOffset = cursor;
            int dataLength = ReadInt32BE(data, cursor);
            cursor += 4;

            int itemStart = cursor;
            int itemEnd = itemStart + dataLength;
            if (dataLength < 8 || itemEnd < itemStart || itemEnd > data.Length)
                return false;

            item = new InventoryItemPreview
            {
                GroupOffset = groupOffset,
                IdenticalCount = identicalCount,
                DataLengthOffset = dataLengthOffset,
                DataLength = dataLength,
                ItemStartOffset = itemStart,
                ItemEndOffset = itemEnd,
                RegistryId = ReadUInt16BE(data, itemStart),
                SaveType = data[itemStart + 2],
                ItemId = ReadInt32BE(data, itemStart + 3),
                HeaderOffset = itemStart + 7,
                Header = data[itemStart + 7]
            };

            int itemCursor = itemStart + 8;
            if ((item.Header & 1) != 0)
            {
                if (itemCursor + 4 > itemEnd)
                    return false;

                item.CurrentUsesOffset = itemCursor;
                item.CurrentUses = ReadInt32BE(data, itemCursor);
                item.HasCurrentUses = true;
                itemCursor += 4;
            }

            if ((item.Header & 4) != 0)
            {
                if (itemCursor >= itemEnd)
                    return false;

                item.ConditionOffset = itemCursor;
                item.Condition = data[itemCursor++];
                item.HasCondition = true;
            }

            if ((item.Header & 8) != 0)
            {
                if (!TryReadItemVisualPreview(data, itemCursor, out ItemVisualPreview visual) ||
                    visual.NextOffset > itemEnd)
                {
                    item.ParseNote = "visual flag set but ItemVisual could not be decoded";
                    itemCursor = itemEnd;
                }
                else
                {
                    item.Visual = visual;
                    itemCursor = visual.NextOffset;
                }
            }

            if ((item.Header & 16) != 0 && !TrySkipWithin(data, ref itemCursor, 4, itemEnd))
                return false;

            if ((item.Header & 32) != 0)
            {
                if (itemCursor + 4 > itemEnd)
                    return false;

                item.ItemCapacityOffset = itemCursor;
                item.ItemCapacity = ReadSingleBE(data, itemCursor);
                item.HasItemCapacity = true;
                itemCursor += 4;
            }

            if ((item.Header & 64) != 0)
            {
                if (itemCursor + 4 > itemEnd)
                    return false;

                item.NestedFlagsOffset = itemCursor;
                item.NestedFlags = ReadUInt32BE(data, itemCursor);
                item.HasNestedFlags = true;
                itemCursor += 4;

                if (!TrySkipInventoryItemNestedPayload(data, ref itemCursor, itemEnd, item.NestedFlags, item))
                {
                    if (string.IsNullOrEmpty(item.ParseNote))
                        AppendParseNote(item, "extra payload not fully skipped");
                    itemCursor = itemEnd;
                }
            }

            item.StringHints.AddRange(FindPzUtfStrings(data, itemStart, itemEnd).Take(12).Select(x => x.Value));

            item.BaseParsedEndOffset = itemCursor;
            item.SubtypeOffset = itemCursor;
            item.SubtypeBytes = Math.Max(0, itemEnd - itemCursor);
            if (item.SubtypeBytes > 0 &&
                TryReadInventoryContainerTailPreview(data, itemCursor, itemEnd, out InventoryContainerTailPreview containerTail))
            {
                item.ContainerTail = containerTail;
            }
            else if (item.SubtypeBytes > 0 &&
                TryReadClothingTailPreview(data, itemCursor, itemEnd, out ClothingTailPreview clothingTail))
            {
                item.ClothingTail = clothingTail;
            }
            else if (item.SubtypeBytes > 0 &&
                TryReadLiteratureTailPreview(data, itemCursor, itemEnd, out LiteratureTailPreview literatureTail))
            {
                item.LiteratureTail = literatureTail;
            }
            else if (item.SubtypeBytes > 0 &&
                TryReadKeyTailPreview(data, itemCursor, itemEnd, item.StringHints, out KeyTailPreview keyTail))
            {
                item.KeyTail = keyTail;
            }
            else if (item.SubtypeBytes > 0 &&
                TryReadHandWeaponTailPreview(data, itemCursor, itemEnd, out HandWeaponTailPreview handWeaponTail))
            {
                item.HandWeaponTail = handWeaponTail;
            }

            cursor = itemEnd;
            if (identicalCount > 1)
            {
                int duplicateIdBytes = (identicalCount - 1) * 4;
                if (duplicateIdBytes < 0 || cursor + duplicateIdBytes > data.Length)
                    return false;

                int duplicateIdCount = Math.Min(identicalCount - 1, 8);
                for (int i = 0; i < duplicateIdCount; i++)
                    item.DuplicateIds.Add(ReadInt32BE(data, cursor + i * 4));

                cursor += duplicateIdBytes;
            }

            return true;
        }

        private static bool TrySkipInventoryItemNestedPayload(
            byte[] data,
            ref int cursor,
            int itemEnd,
            uint flags,
            InventoryItemPreview item)
        {
            if ((flags & 0x00000001) != 0)
            {
                int tableOffset = cursor;
                if (!TryReadKahluaTable(data, cursor, 0, out KahluaTablePreview table) || !table.Success || table.NextOffset > itemEnd)
                    return false;

                item.ModDataOffset = tableOffset;
                item.ModDataEntryCount = table.Count;
                cursor = table.NextOffset;
            }

            if ((flags & 0x00000004) != 0 && !TrySkipWithin(data, ref cursor, 2, itemEnd))
                return false;
            if ((flags & 0x00000008) != 0 && !TryReadPzUtfString(data, cursor, out string displayName, out cursor))
                return false;
            if ((flags & 0x00000010) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;

                int byteDataLength = ReadInt32BE(data, cursor);
                cursor += 4;
                if (byteDataLength < 0 || !TrySkipWithin(data, ref cursor, byteDataLength, itemEnd))
                    return false;
            }
            if ((flags & 0x00000020) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;

                item.ExtraItemsOffset = cursor;
                int extraItemCount = ReadInt32BE(data, cursor);
                cursor += 4;
                if (extraItemCount < 0 || extraItemCount > 10000 || cursor + extraItemCount * 2 > itemEnd)
                    return false;

                for (int i = 0; i < extraItemCount; i++)
                {
                    item.ExtraItemRegistryIds.Add(ReadUInt16BE(data, cursor));
                    cursor += 2;
                }
            }
            if ((flags & 0x00000080) != 0 && !TrySkipWithin(data, ref cursor, 4, itemEnd))
                return false;
            if ((flags & 0x00000100) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;

                item.CommonKeyIdOffset = cursor;
                item.CommonKeyId = ReadInt32BE(data, cursor);
                item.HasCommonKeyId = true;
                cursor += 4;
            }
            if ((flags & 0x00000400) != 0 && !TrySkipWithin(data, ref cursor, 8, itemEnd))
                return false;
            if ((flags & 0x00000800) != 0 && !TrySkipWithin(data, ref cursor, 3, itemEnd))
                return false;
            if ((flags & 0x00001000) != 0 && !TryReadPzUtfString(data, cursor, out string worker, out cursor))
                return false;
            if ((flags & 0x00002000) != 0 && !TrySkipWithin(data, ref cursor, 4, itemEnd))
                return false;
            if ((flags & 0x00008000) != 0 && !TryReadPzUtfString(data, cursor, out string stashMap, out cursor))
                return false;
            if ((flags & 0x00020000) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;

                item.CurrentAmmoCountOffset = cursor;
                item.CurrentAmmoCount = ReadInt32BE(data, cursor);
                item.HasCurrentAmmoCount = true;
                cursor += 4;
            }
            if ((flags & 0x00040000) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;

                item.AttachedSlotOffset = cursor;
                item.AttachedSlot = ReadInt32BE(data, cursor);
                item.HasAttachedSlot = true;
                cursor += 4;
            }
            if ((flags & 0x00080000) != 0)
            {
                item.AttachedSlotTypeOffset = cursor;
                if (!TryReadPzUtfString(data, cursor, out string attachedSlotType, out cursor))
                    return false;

                item.AttachedSlotType = attachedSlotType;
            }
            if ((flags & 0x00100000) != 0)
            {
                item.AttachedToModelOffset = cursor;
                if (!TryReadPzUtfString(data, cursor, out string attachedToModel, out cursor))
                    return false;

                item.AttachedToModel = attachedToModel;
            }
            if ((flags & 0x00200000) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;

                item.MaxCapacityOffset = cursor;
                item.MaxCapacity = ReadInt32BE(data, cursor);
                item.HasMaxCapacity = true;
                cursor += 4;
            }
            if ((flags & 0x00400000) != 0 && !TrySkipWithin(data, ref cursor, 2, itemEnd))
                return false;
            if ((flags & 0x01000000) != 0 && !TrySkipWithin(data, ref cursor, 4, itemEnd))
                return false;
            if ((flags & 0x04000000) != 0)
            {
                if (!TryReadGameEntityPayloadPreview(data, cursor, itemEnd, out EntityPayloadPreview entityPayload))
                {
                    AppendParseNote(item, "entity payload not decoded");
                    return false;
                }

                item.EntityPayload = entityPayload;
                cursor = entityPayload.NextOffset;
            }
            if ((flags & 0x08000000) != 0)
            {
                AppendParseNote(item, "animal tracks payload not decoded");
                return false;
            }
            if ((flags & 0x10000000) != 0 && !TryReadPzUtfString(data, cursor, out string texture, out cursor))
                return false;
            if ((flags & 0x20000000) != 0 && !TrySkipWithin(data, ref cursor, 4, itemEnd))
                return false;
            if ((flags & 0x40000000) != 0 && !TrySkipWithin(data, ref cursor, 12, itemEnd))
                return false;

            return cursor <= itemEnd;
        }

        private static bool TryReadInventoryContainerTailPreview(byte[] data, int offset, int itemEnd, out InventoryContainerTailPreview preview)
        {
            preview = null;
            if (offset < 0 || offset + 8 >= itemEnd)
                return false;

            int cursor = offset;
            var result = new InventoryContainerTailPreview
            {
                StartOffset = offset,
                ContainerId = ReadInt32BE(data, cursor)
            };
            cursor += 4;

            result.WeightReduction = ReadInt32BE(data, cursor);
            cursor += 4;

            if (result.WeightReduction < -1 || result.WeightReduction > 100)
                return false;

            if (!TryReadItemContainerPreview(data, cursor, out InventoryPreview nestedInventory) ||
                nestedInventory.NextOffset != itemEnd)
            {
                return false;
            }

            result.NestedInventory = nestedInventory;
            result.NextOffset = itemEnd;
            preview = result;
            return true;
        }

        private static bool TryReadKeyTailPreview(
            byte[] data,
            int offset,
            int itemEnd,
            IReadOnlyList<string> hints,
            out KeyTailPreview preview)
        {
            preview = null;
            if (offset < 0 || offset + 5 != itemEnd || !LooksLikeKeyItem(hints))
                return false;

            int keyId = ReadInt32BE(data, offset);
            int numberOfKey = data[offset + 4];
            if (keyId < -1 || numberOfKey < 0 || numberOfKey > 127)
                return false;

            preview = new KeyTailPreview
            {
                StartOffset = offset,
                NextOffset = itemEnd,
                KeyId = keyId,
                NumberOfKey = numberOfKey
            };
            return true;
        }

        private static bool TryReadHandWeaponTailPreview(
            byte[] data,
            int offset,
            int itemEnd,
            out HandWeaponTailPreview preview)
        {
            preview = null;
            const uint knownFlags = 0x01FF1FFF;

            if (offset < 0 || offset + 4 > itemEnd)
                return false;

            int cursor = offset;
            uint flags = ReadUInt32BE(data, cursor);
            cursor += 4;
            if (flags == 0 || (flags & ~knownFlags) != 0)
                return false;

            var result = new HandWeaponTailPreview
            {
                StartOffset = offset,
                Flags = flags,
                ContainsClip = (flags & 0x00080000) != 0,
                RoundChambered = (flags & 0x00100000) != 0,
                IsJammed = (flags & 0x00200000) != 0
            };

            if ((flags & 0x00000001) != 0)
            {
                if (!TryReadPlausibleSingle(data, ref cursor, itemEnd, -1f, 1000f, out float maxRange))
                    return false;

                result.MaxRange = maxRange;
                result.HasMaxRange = true;
            }

            if ((flags & 0x00000002) != 0)
            {
                if (!TryReadPlausibleSingle(data, ref cursor, itemEnd, -1f, 1000f, out float minRangeRanged))
                    return false;

                result.MinRangeRanged = minRangeRanged;
                result.HasMinRangeRanged = true;
            }

            if ((flags & 0x00000004) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;

                result.ClipSize = ReadInt32BE(data, cursor);
                result.HasClipSize = true;
                cursor += 4;
                if (result.ClipSize < 0 || result.ClipSize > 1000)
                    return false;
            }

            if ((flags & 0x00000008) != 0)
            {
                if (!TryReadPlausibleSingle(data, ref cursor, itemEnd, 0f, 1000f, out float minDamage))
                    return false;

                result.MinDamage = minDamage;
                result.HasMinDamage = true;
            }

            if ((flags & 0x00000010) != 0)
            {
                if (!TryReadPlausibleSingle(data, ref cursor, itemEnd, 0f, 1000f, out float maxDamage))
                    return false;

                result.MaxDamage = maxDamage;
                result.HasMaxDamage = true;
            }

            if ((flags & 0x00000020) != 0)
            {
                if (!TryReadPlausibleInt(data, ref cursor, itemEnd, 0, 100000, out int recoilDelay))
                    return false;

                result.RecoilDelay = recoilDelay;
                result.HasRecoilDelay = true;
            }

            if ((flags & 0x00000040) != 0)
            {
                if (!TryReadPlausibleInt(data, ref cursor, itemEnd, 0, 100000, out int aimingTime))
                    return false;

                result.AimingTime = aimingTime;
                result.HasAimingTime = true;
            }

            if ((flags & 0x00000080) != 0)
            {
                if (!TryReadPlausibleInt(data, ref cursor, itemEnd, 0, 100000, out int reloadTime))
                    return false;

                result.ReloadTime = reloadTime;
                result.HasReloadTime = true;
            }

            if ((flags & 0x00000100) != 0)
            {
                if (!TryReadPlausibleInt(data, ref cursor, itemEnd, -1000, 100000, out int hitChance))
                    return false;

                result.HitChance = hitChance;
                result.HasHitChance = true;
            }

            if ((flags & 0x00000200) != 0)
            {
                if (!TryReadPlausibleSingle(data, ref cursor, itemEnd, -100f, 100f, out float minAngle))
                    return false;

                result.MinAngle = minAngle;
                result.HasMinAngle = true;
            }

            if ((flags & 0x00000400) != 0)
            {
                if (cursor >= itemEnd)
                    return false;

                result.AttachmentCount = data[cursor];
                result.HasAttachmentCount = true;
                if (result.AttachmentCount > 32)
                    return false;
            }

            if (!result.HasClipSize &&
                !result.HasAttachmentCount &&
                !result.ContainsClip &&
                !result.RoundChambered &&
                !result.IsJammed)
            {
                return false;
            }

            result.NextOffset = cursor;
            preview = result;
            return true;
        }

        private static bool TryReadPlausibleSingle(
            byte[] data,
            ref int cursor,
            int itemEnd,
            float min,
            float max,
            out float value)
        {
            value = 0f;
            if (cursor + 4 > itemEnd)
                return false;

            value = ReadSingleBE(data, cursor);
            cursor += 4;
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }

        private static bool TryReadPlausibleInt(
            byte[] data,
            ref int cursor,
            int itemEnd,
            int min,
            int max,
            out int value)
        {
            value = 0;
            if (cursor + 4 > itemEnd)
                return false;

            value = ReadInt32BE(data, cursor);
            cursor += 4;
            return value >= min && value <= max;
        }

        private static bool TryReadGameEntityPayloadPreview(
            byte[] data,
            int offset,
            int itemEnd,
            out EntityPayloadPreview preview)
        {
            preview = null;
            if (offset < 0 || offset >= itemEnd)
                return false;

            int cursor = offset;
            int componentCount = data[cursor++];
            if (componentCount < 0 || componentCount > 64)
                return false;

            var result = new EntityPayloadPreview
            {
                StartOffset = offset,
                ComponentCount = componentCount
            };

            for (int i = 0; i < componentCount; i++)
            {
                if (cursor + 6 > itemEnd)
                    return false;

                int blockLength = ReadInt32BE(data, cursor);
                cursor += 4;
                int blockStart = cursor;
                int blockEnd = blockStart + blockLength;
                if (blockLength < 2 || blockEnd < blockStart || blockEnd > itemEnd)
                    return false;

                short componentId = ReadInt16BE(data, cursor);
                cursor += 2;
                result.ComponentIds.Add(componentId);

                if (componentId == 2 &&
                    TryReadFluidContainerPreview(data, cursor, blockEnd, out FluidContainerPreview fluidContainer))
                {
                    result.FluidContainer = fluidContainer;
                }

                cursor = blockEnd;
            }

            result.NextOffset = cursor;
            preview = result;
            return true;
        }

        private static bool TryReadFluidContainerPreview(byte[] data, int offset, int blockEnd, out FluidContainerPreview preview)
        {
            preview = null;
            if (offset < 0 || offset + 2 > blockEnd)
                return false;

            int cursor = offset;
            ushort flags = ReadUInt16BE(data, cursor);
            cursor += 2;
            if ((flags & 0xFC00) != 0)
                return false;

            var result = new FluidContainerPreview
            {
                StartOffset = offset,
                Flags = flags,
                Capacity = 1f
            };

            if ((flags & 1) != 0)
            {
                if (cursor + 4 > blockEnd)
                    return false;

                result.Capacity = ReadSingleBE(data, cursor);
                cursor += 4;
                if (float.IsNaN(result.Capacity) || float.IsInfinity(result.Capacity) || result.Capacity <= 0f || result.Capacity > 10000f)
                    return false;
            }

            if ((flags & 2) != 0)
            {
                if ((flags & 4) != 0)
                {
                    if (!TryReadFluidInstancePreview(data, ref cursor, blockEnd, out FluidInstancePreview fluid))
                        return false;

                    result.Fluids.Add(fluid);
                }
                else
                {
                    if (cursor >= blockEnd)
                        return false;

                    int fluidCount = data[cursor++];
                    if (fluidCount < 0 || fluidCount > 32)
                        return false;

                    for (int i = 0; i < fluidCount; i++)
                    {
                        if (!TryReadFluidInstancePreview(data, ref cursor, blockEnd, out FluidInstancePreview fluid))
                            return false;

                        result.Fluids.Add(fluid);
                    }
                }
            }

            if ((flags & 8) != 0 || (flags & 16) != 0)
                return false;

            result.InputLocked = (flags & 32) != 0;
            result.CanPlayerEmpty = (flags & 64) != 0;

            if ((flags & 128) != 0)
            {
                if (!TryReadPzUtfString(data, cursor, out string containerName, out cursor))
                    return false;

                result.ContainerName = containerName;
            }

            result.HiddenAmount = (flags & 256) != 0;

            if ((flags & 512) != 0)
            {
                if (cursor + 4 > blockEnd)
                    return false;

                result.RainCatcher = ReadSingleBE(data, cursor);
                result.HasRainCatcher = true;
                cursor += 4;
            }

            if (cursor != blockEnd)
                return false;

            result.NextOffset = cursor;
            preview = result;
            return true;
        }

        private static bool TryReadFluidInstancePreview(
            byte[] data,
            ref int cursor,
            int blockEnd,
            out FluidInstancePreview preview)
        {
            preview = null;
            if (cursor >= blockEnd)
                return false;

            byte flags = data[cursor++];
            if ((flags & 0xF8) != 0)
                return false;

            var result = new FluidInstancePreview { Flags = flags };
            if ((flags & 1) != 0)
            {
                if (cursor >= blockEnd)
                    return false;

                result.FluidTypeId = unchecked((sbyte)data[cursor++]);
                result.FluidType = FormatFluidType(result.FluidTypeId);
            }
            else if ((flags & 2) != 0)
            {
                if (!TryReadPzUtfString(data, cursor, out string fluidType, out cursor))
                    return false;

                result.FluidType = fluidType;
            }
            else
            {
                result.FluidType = "None";
            }

            if ((flags & 4) != 0 && !TrySkipWithin(data, ref cursor, 3, blockEnd))
                return false;

            if (cursor + 4 > blockEnd)
                return false;

            result.Amount = ReadSingleBE(data, cursor);
            cursor += 4;
            if (float.IsNaN(result.Amount) || float.IsInfinity(result.Amount) || result.Amount < 0f || result.Amount > 100000f)
                return false;

            preview = result;
            return true;
        }

        private static bool TryReadClothingTailPreview(byte[] data, int offset, int itemEnd, out ClothingTailPreview preview)
        {
            preview = null;
            if (offset < 0 || offset >= itemEnd)
                return false;

            int cursor = offset;
            byte flags = data[cursor++];
            if ((flags & 0xC0) != 0)
                return false;

            var result = new ClothingTailPreview
            {
                StartOffset = offset,
                Flags = flags
            };

            if ((flags & 1) != 0)
            {
                result.SpriteNameOffset = cursor;
                if (!TryReadPzUtfString(data, cursor, out string spriteName, out cursor))
                    return false;
                result.SpriteName = spriteName;
            }

            if ((flags & 2) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;
                result.Dirtyness = ReadSingleBE(data, cursor);
                result.HasDirtyness = true;
                cursor += 4;
            }

            if ((flags & 4) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;
                result.BloodLevel = ReadSingleBE(data, cursor);
                result.HasBloodLevel = true;
                cursor += 4;
            }

            if ((flags & 8) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;
                result.Wetness = ReadSingleBE(data, cursor);
                result.HasWetness = true;
                cursor += 4;
            }

            if ((flags & 16) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;
                result.LastWetnessUpdate = ReadSingleBE(data, cursor);
                result.HasLastWetnessUpdate = true;
                cursor += 4;
            }

            if ((flags & 32) != 0)
            {
                if (cursor >= itemEnd)
                    return false;

                int patchCount = data[cursor++];
                if (patchCount > 128)
                    return false;

                for (int i = 0; i < patchCount; i++)
                {
                    if (cursor + 8 > itemEnd)
                        return false;

                    var patch = new ClothingPatchPreview
                    {
                        PartIndex = data[cursor++],
                        TailorLevel = data[cursor++],
                        FabricType = data[cursor++],
                        ScratchDefense = data[cursor++],
                        BiteDefense = data[cursor++],
                        HasHole = data[cursor++] != 0,
                        ConditionGain = ReadInt16BE(data, cursor)
                    };
                    cursor += 2;
                    result.Patches.Add(patch);
                }
            }

            if (cursor != itemEnd)
                return false;

            result.NextOffset = cursor;
            preview = result;
            return true;
        }

        private static bool TryReadLiteratureTailPreview(byte[] data, int offset, int itemEnd, out LiteratureTailPreview preview)
        {
            preview = null;
            if (offset < 0 || offset + 2 > itemEnd)
                return false;

            int cursor = offset;
            ushort flags = ReadUInt16BE(data, cursor);
            cursor += 2;
            if ((flags & 0xFF00) != 0)
                return false;

            int numberPageType = 0;
            var result = new LiteratureTailPreview
            {
                StartOffset = offset,
                Flags = flags
            };

            if ((flags & 1) != 0)
            {
                if ((flags & 2) != 0)
                {
                    numberPageType = 1;
                    if (cursor + 2 > itemEnd)
                        return false;
                    result.NumberOfPages = ReadUInt16BE(data, cursor);
                    cursor += 2;
                }
                else if ((flags & 4) != 0)
                {
                    numberPageType = 2;
                    if (cursor + 4 > itemEnd)
                        return false;
                    result.NumberOfPages = ReadInt32BE(data, cursor);
                    cursor += 4;
                }
                else
                {
                    if (cursor >= itemEnd)
                        return false;
                    result.NumberOfPages = data[cursor++];
                }

                result.HasNumberOfPages = true;
            }

            if ((flags & 8) != 0)
            {
                if (numberPageType == 1)
                {
                    if (cursor + 2 > itemEnd)
                        return false;
                    result.AlreadyReadPages = ReadUInt16BE(data, cursor);
                    cursor += 2;
                }
                else if (numberPageType == 2)
                {
                    if (cursor + 4 > itemEnd)
                        return false;
                    result.AlreadyReadPages = ReadInt32BE(data, cursor);
                    cursor += 4;
                }
                else
                {
                    if (cursor >= itemEnd)
                        return false;
                    result.AlreadyReadPages = data[cursor++];
                }

                result.HasAlreadyReadPages = true;
            }

            if ((flags & 32) != 0)
            {
                if (cursor + 4 > itemEnd)
                    return false;

                int pageCount = ReadInt32BE(data, cursor);
                cursor += 4;
                if (pageCount < 0 || pageCount > 1000)
                    return false;

                for (int i = 0; i < pageCount; i++)
                {
                    if (!TryReadPzUtfString(data, cursor, out string page, out cursor))
                        return false;
                }
            }

            if ((flags & 64) != 0 && !TryReadPzUtfString(data, cursor, out string lockedBy, out cursor))
                return false;

            if ((flags & 128) != 0)
            {
                if (cursor + 2 > itemEnd)
                    return false;

                int recipeCount = ReadUInt16BE(data, cursor);
                cursor += 2;
                if (recipeCount < 0 || recipeCount > 512)
                    return false;

                for (int i = 0; i < recipeCount; i++)
                {
                    if (!TryReadPzUtfString(data, cursor, out string recipe, out cursor))
                        return false;
                    result.LearnedRecipes.Add(recipe);
                }
            }

            if (cursor != itemEnd)
                return false;

            result.NextOffset = cursor;
            preview = result;
            return true;
        }

        private static bool TryReadCharacterStatsPreview(byte[] data, int offset, out CharacterStatsPreview preview)
        {
            preview = null;
            const int statBytes = 24 * 4;
            if (offset < 0 || offset + 1 + 4 + statBytes > data.Length)
                return false;

            int cursor = offset;
            var result = new CharacterStatsPreview
            {
                StartOffset = offset,
                Asleep = data[cursor++] != 0,
                ForceWakeUpTime = ReadSingleBE(data, cursor),
                StatsOffset = cursor + 4
            };
            cursor += 4;

            for (int i = 0; i < CharacterStatNames.Length; i++)
            {
                result.Stats.Add(new NamedFloat(CharacterStatNames[i], ReadSingleBE(data, cursor)));
                cursor += 4;
            }

            result.NextOffset = cursor;
            preview = result;
            return true;
        }

        private static bool TryReadBodyDamagePreview(byte[] data, int offset, out BodyDamagePreview preview)
        {
            preview = null;
            if (offset < 0 || offset >= data.Length)
                return false;

            int cursor = offset;
            var result = new BodyDamagePreview { StartOffset = offset };
            for (int i = 0; i < BodyPartNames.Length; i++)
            {
                if (!TryReadBodyPartDamagePreview(data, ref cursor, BodyPartNames[i], out BodyPartDamagePreview part))
                    return false;

                result.Parts.Add(part);
            }

            if (cursor + 39 > data.Length)
                return false;

            result.CatchCold = ReadSingleBE(data, cursor);
            cursor += 4;
            if (!TryReadBooleanByte(data, ref cursor, out bool hasCold))
                return false;
            result.HasCold = hasCold;
            result.ColdStrength = ReadSingleBE(data, cursor);
            cursor += 4;
            result.TimeToSneezeOrCough = ReadInt32BE(data, cursor);
            cursor += 4;
            if (!TryReadBooleanByte(data, ref cursor, out bool reduceFakeInfection))
                return false;
            result.ReduceFakeInfection = reduceFakeInfection;
            result.HealthFromFoodTimer = ReadSingleBE(data, cursor);
            cursor += 4;
            result.PainReduction = ReadSingleBE(data, cursor);
            cursor += 4;
            result.ColdReduction = ReadSingleBE(data, cursor);
            cursor += 4;
            result.InfectionTime = ReadSingleBE(data, cursor);
            cursor += 4;
            result.InfectionMortalityDuration = ReadSingleBE(data, cursor);
            cursor += 4;
            result.ColdDamageStage = ReadSingleBE(data, cursor);
            cursor += 4;
            if (!TryReadBooleanByte(data, ref cursor, out bool hasThermoregulator))
                return false;
            result.HasThermoregulator = hasThermoregulator;
            if (hasThermoregulator)
            {
                if (TryReadThermoregulatorPreview(data, cursor, out ThermoregulatorPreview thermoregulator))
                {
                    result.Thermoregulator = thermoregulator;
                    cursor = thermoregulator.NextOffset;
                }
                else
                {
                    result.ThermoregulatorNote = "thermoregulator payload not decoded";
                }
            }

            result.NextOffset = cursor;
            result.Success = true;
            preview = result;
            return true;
        }

        private static bool TryReadThermoregulatorPreview(byte[] data, int offset, out ThermoregulatorPreview preview)
        {
            preview = null;
            if (offset < 0 || offset + 36 > data.Length)
                return false;

            int cursor = offset;
            var result = new ThermoregulatorPreview
            {
                StartOffset = offset,
                SetPoint = ReadSingleBE(data, cursor)
            };
            cursor += 4;
            result.MetabolicRate = ReadSingleBE(data, cursor);
            cursor += 4;
            result.MetabolicRateReal = ReadSingleBE(data, cursor);
            cursor += 4;
            result.MetabolicTarget = ReadSingleBE(data, cursor);
            cursor += 4;
            result.BodyHeatDelta = ReadSingleBE(data, cursor);
            cursor += 4;
            result.CoreHeatDelta = ReadSingleBE(data, cursor);
            cursor += 4;
            result.ThermalDamage = ReadSingleBE(data, cursor);
            cursor += 4;
            result.DamageCounter = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!IsPlausibleTemperature(result.SetPoint) ||
                float.IsNaN(result.MetabolicRate) || float.IsInfinity(result.MetabolicRate))
            {
                return false;
            }

            int count = ReadInt32BE(data, cursor);
            cursor += 4;
            if (count <= 0 || count > 64)
                return false;

            for (int i = 0; i < count; i++)
            {
                if (cursor + 40 > data.Length)
                    return false;

                int partIndex = ReadInt32BE(data, cursor);
                cursor += 4;
                if (partIndex < 0 || partIndex >= BodyPartNames.Length)
                    return false;

                var node = new ThermalNodePreview
                {
                    BodyPartIndex = partIndex,
                    BodyPartName = BodyPartNames[partIndex],
                    Celsius = ReadSingleBE(data, cursor)
                };
                cursor += 4;
                node.SkinCelsius = ReadSingleBE(data, cursor);
                cursor += 4;
                node.HeatDelta = ReadSingleBE(data, cursor);
                cursor += 4;
                node.PrimaryDelta = ReadSingleBE(data, cursor);
                cursor += 4;
                node.SecondaryDelta = ReadSingleBE(data, cursor);
                cursor += 4;
                node.Insulation = ReadSingleBE(data, cursor);
                cursor += 4;
                node.WindResist = ReadSingleBE(data, cursor);
                cursor += 4;
                node.BodyWetness = ReadSingleBE(data, cursor);
                cursor += 4;
                node.ClothingWetness = ReadSingleBE(data, cursor);
                cursor += 4;

                if (!IsPlausibleTemperature(node.Celsius) ||
                    !IsPlausibleTemperature(node.SkinCelsius))
                {
                    return false;
                }

                result.Nodes.Add(node);
            }

            result.NextOffset = cursor;
            preview = result;
            return true;
        }

        private static bool TryReadBodyPartDamagePreview(
            byte[] data,
            ref int cursor,
            string partName,
            out BodyPartDamagePreview preview)
        {
            preview = null;
            var result = new BodyPartDamagePreview { Name = partName, StartOffset = cursor };

            if (!TryReadBooleanByte(data, ref cursor, out bool cut) ||
                !TryReadBooleanByte(data, ref cursor, out bool bitten) ||
                !TryReadBooleanByte(data, ref cursor, out bool scratched) ||
                !TryReadBooleanByte(data, ref cursor, out bool bandaged) ||
                !TryReadBooleanByte(data, ref cursor, out bool bleeding) ||
                !TryReadBooleanByte(data, ref cursor, out bool deepWounded) ||
                !TryReadBooleanByte(data, ref cursor, out bool fakeInfected) ||
                !TryReadBooleanByte(data, ref cursor, out bool infected))
            {
                return false;
            }

            result.Cut = cut;
            result.Bitten = bitten;
            result.Scratched = scratched;
            result.Bandaged = bandaged;
            result.Bleeding = bleeding;
            result.DeepWounded = deepWounded;
            result.FakeInfected = fakeInfected;
            result.Infected = infected;

            if (cursor + 4 > data.Length)
                return false;
            result.Health = ReadSingleBE(data, cursor);
            cursor += 4;
            if (float.IsNaN(result.Health) || float.IsInfinity(result.Health) || result.Health < -100f || result.Health > 1000f)
                return false;

            if (bandaged && !TrySkipWithin(data, ref cursor, 4, data.Length))
                return false;

            if (!TryReadBooleanByte(data, ref cursor, out bool infectedWound))
                return false;
            result.InfectedWound = infectedWound;
            if (infectedWound && !TrySkipWithin(data, ref cursor, 4, data.Length))
                return false;

            if (cursor + 28 > data.Length)
                return false;
            result.CutTime = ReadSingleBE(data, cursor);
            cursor += 4;
            result.BiteTime = ReadSingleBE(data, cursor);
            cursor += 4;
            result.ScratchTime = ReadSingleBE(data, cursor);
            cursor += 4;
            result.BleedingTime = ReadSingleBE(data, cursor);
            cursor += 4;
            result.AlcoholLevel = ReadSingleBE(data, cursor);
            cursor += 4;
            result.AdditionalPain = ReadSingleBE(data, cursor);
            cursor += 4;
            result.DeepWoundTime = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!TryReadBooleanByte(data, ref cursor, out bool haveGlass) ||
                !TryReadBooleanByte(data, ref cursor, out bool getBandageXp) ||
                !TryReadBooleanByte(data, ref cursor, out bool stitched))
            {
                return false;
            }
            result.HaveGlass = haveGlass;
            result.GetBandageXp = getBandageXp;
            result.Stitched = stitched;

            if (cursor + 4 > data.Length)
                return false;
            result.StitchTime = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!TryReadBooleanByte(data, ref cursor, out bool getStitchXp) ||
                !TryReadBooleanByte(data, ref cursor, out bool getSplintXp))
            {
                return false;
            }
            result.GetStitchXp = getStitchXp;
            result.GetSplintXp = getSplintXp;

            if (cursor + 4 > data.Length)
                return false;
            result.FractureTime = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!TryReadBooleanByte(data, ref cursor, out bool splint))
                return false;
            result.Splint = splint;
            if (splint && !TrySkipWithin(data, ref cursor, 4, data.Length))
                return false;

            if (!TryReadBooleanByte(data, ref cursor, out bool haveBullet))
                return false;
            result.HaveBullet = haveBullet;

            if (cursor + 4 > data.Length)
                return false;
            result.BurnTime = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!TryReadBooleanByte(data, ref cursor, out bool needBurnWash))
                return false;
            result.NeedBurnWash = needBurnWash;

            if (cursor + 4 > data.Length)
                return false;
            result.LastTimeBurnWash = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!TryReadPzUtfString(data, cursor, out string splintItem, out cursor) ||
                !TryReadPzUtfString(data, cursor, out string bandageType, out cursor))
            {
                return false;
            }
            result.SplintItem = splintItem;
            result.BandageType = bandageType;

            if (cursor + 24 > data.Length)
                return false;
            cursor += 4; 
            result.Wetness = ReadSingleBE(data, cursor);
            cursor += 4;
            result.Stiffness = ReadSingleBE(data, cursor);
            cursor += 4;
            result.ComfreyFactor = ReadSingleBE(data, cursor);
            cursor += 4;
            result.GarlicFactor = ReadSingleBE(data, cursor);
            cursor += 4;
            result.PlantainFactor = ReadSingleBE(data, cursor);
            cursor += 4;

            result.NextOffset = cursor;
            preview = result;
            return true;
        }

        private static bool TryFindXpPreview(byte[] data, int startOffset, int endOffset, out XpPreview preview)
        {
            preview = null;
            int scanEnd = Math.Min(data.Length, endOffset);
            for (int offset = Math.Max(0, startOffset); offset + 20 < scanEnd; offset++)
            {
                int cursor = offset;
                int traitCount = ReadInt32BE(data, cursor);
                cursor += 4;
                if (traitCount < 0 || traitCount > 32)
                    continue;

                var result = new XpPreview { StartOffset = offset };
                bool traitsOk = true;
                for (int i = 0; i < traitCount; i++)
                {
                    if (!TryReadPzUtfString(data, cursor, out string trait, out cursor) ||
                        !trait.StartsWith("base:", StringComparison.OrdinalIgnoreCase))
                    {
                        traitsOk = false;
                        break;
                    }

                    result.Traits.Add(trait);
                }

                if (!traitsOk || cursor + 16 > scanEnd)
                    continue;

                result.TotalXp = ReadSingleBE(data, cursor);
                cursor += 4;
                result.Level = ReadInt32BE(data, cursor);
                cursor += 4;
                result.LastLevel = ReadInt32BE(data, cursor);
                cursor += 4;

                int xpCount = ReadInt32BE(data, cursor);
                cursor += 4;
                if (xpCount < 0 || xpCount > 128)
                    continue;

                bool xpOk = true;
                for (int i = 0; i < xpCount; i++)
                {
                    if (!TryReadPzUtfString(data, cursor, out string perkName, out cursor) ||
                        cursor + 4 > scanEnd)
                    {
                        xpOk = false;
                        break;
                    }

                    float xp = ReadSingleBE(data, cursor);
                    cursor += 4;
                    if (!IsInterestingFloat(xp) && Math.Abs(xp) > 0.0001f)
                    {
                        xpOk = false;
                        break;
                    }

                    result.XpEntries.Add(new NamedFloat(perkName, xp));
                }

                if (!xpOk || cursor + 4 > scanEnd)
                    continue;

                int perkLevelCount = ReadInt32BE(data, cursor);
                cursor += 4;
                if (perkLevelCount < 0 || perkLevelCount > 128)
                    continue;

                for (int i = 0; i < perkLevelCount; i++)
                {
                    if (!TryReadPzUtfString(data, cursor, out string perkName, out cursor) ||
                        cursor + 4 > scanEnd)
                    {
                        xpOk = false;
                        break;
                    }

                    int level = ReadInt32BE(data, cursor);
                    cursor += 4;
                    if (level < 0 || level > 10)
                    {
                        xpOk = false;
                        break;
                    }

                    result.PerkLevels.Add(new NamedInt(perkName, level));
                }

                if (!xpOk || cursor + 4 > scanEnd)
                    continue;

                int multiplierCount = ReadInt32BE(data, cursor);
                cursor += 4;
                if (multiplierCount < 0 || multiplierCount > 128)
                    continue;

                for (int i = 0; i < multiplierCount; i++)
                {
                    if (!TryReadPzUtfString(data, cursor, out string perkName, out cursor) ||
                        cursor + 6 > scanEnd)
                    {
                        xpOk = false;
                        break;
                    }

                    float multiplier = ReadSingleBE(data, cursor);
                    cursor += 4;
                    byte minLevel = data[cursor++];
                    byte maxLevel = data[cursor++];
                    if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f || multiplier > 1000f ||
                        minLevel > 10 || maxLevel > 10)
                    {
                        xpOk = false;
                        break;
                    }

                    result.XpMultipliers.Add(new XpMultiplierPreview(perkName, multiplier, minLevel, maxLevel));
                }

                if (!xpOk)
                    continue;

                bool hasUsefulPerk = result.XpEntries.Any(x => IsKnownPerkName(x.Name)) ||
                                     result.PerkLevels.Any(x => IsKnownPerkName(x.Name));
                if (!hasUsefulPerk)
                    continue;

                result.NextOffset = cursor;
                result.Success = true;
                preview = result;
                return true;
            }

            return false;
        }

        private static bool TryReadIsoGameCharacterTailPreview(
            byte[] data,
            int offset,
            out IsoGameCharacterTailPreview preview)
        {
            preview = null;
            if (offset < 0 || offset + 45 > data.Length)
                return false;

            int cursor = offset;
            var result = new IsoGameCharacterTailPreview
            {
                StartOffset = offset,
                LeftHandInventoryIndex = ReadInt32BE(data, cursor),
                RightHandInventoryIndex = ReadInt32BE(data, cursor + 4)
            };
            cursor += 8;

            if (result.LeftHandInventoryIndex < -1 || result.RightHandInventoryIndex < -1)
                return false;

            if (!TryReadBooleanByte(data, ref cursor, out bool onFire))
                return false;
            result.OnFire = onFire;

            if (cursor + 32 > data.Length)
                return false;

            result.DepressEffect = ReadSingleBE(data, cursor);
            cursor += 4;
            result.DepressFirstTakeTime = ReadSingleBE(data, cursor);
            cursor += 4;
            result.BetaEffect = ReadSingleBE(data, cursor);
            cursor += 4;
            result.BetaDelta = ReadSingleBE(data, cursor);
            cursor += 4;
            result.PainEffect = ReadSingleBE(data, cursor);
            cursor += 4;
            result.PainDelta = ReadSingleBE(data, cursor);
            cursor += 4;
            result.SleepingTabletEffect = ReadSingleBE(data, cursor);
            cursor += 4;
            result.SleepingTabletDelta = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!TrySkipIsoGameCharacterReadBooks(data, ref cursor, out int readBookCount))
                return false;
            result.ReadBookCount = readBookCount;

            if (cursor + 4 > data.Length)
                return false;
            result.ReduceInfectionPower = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!TrySkipPzStringList(data, ref cursor, out int knownRecipeCount))
                return false;
            result.KnownRecipeCount = knownRecipeCount;

            if (cursor + 16 > data.Length)
                return false;
            result.LastHourSleeped = ReadInt32BE(data, cursor);
            cursor += 4;
            result.TimeSinceLastSmoke = ReadSingleBE(data, cursor);
            cursor += 4;
            result.BeardGrowTiming = ReadSingleBE(data, cursor);
            cursor += 4;
            result.HairGrowTiming = ReadSingleBE(data, cursor);
            cursor += 4;

            if (!TryReadBooleanByte(data, ref cursor, out bool unlimitedCarry) ||
                !TryReadBooleanByte(data, ref cursor, out bool buildCheat) ||
                !TryReadBooleanByte(data, ref cursor, out bool healthCheat) ||
                !TryReadBooleanByte(data, ref cursor, out bool mechanicsCheat) ||
                !TryReadBooleanByte(data, ref cursor, out bool movablesCheat) ||
                !TryReadBooleanByte(data, ref cursor, out bool farmingCheat) ||
                !TryReadBooleanByte(data, ref cursor, out bool fishingCheat) ||
                !TryReadBooleanByte(data, ref cursor, out bool canUseBrushTool) ||
                !TryReadBooleanByte(data, ref cursor, out bool fastMoveCheat) ||
                !TryReadBooleanByte(data, ref cursor, out bool timedActionInstantCheat) ||
                !TryReadBooleanByte(data, ref cursor, out bool unlimitedEndurance) ||
                !TryReadBooleanByte(data, ref cursor, out bool unlimitedAmmo) ||
                !TryReadBooleanByte(data, ref cursor, out bool knowAllRecipes) ||
                !TryReadBooleanByte(data, ref cursor, out bool sneaking) ||
                !TryReadBooleanByte(data, ref cursor, out bool deathDragDown))
            {
                return false;
            }

            result.UnlimitedCarry = unlimitedCarry;
            result.BuildCheat = buildCheat;
            result.HealthCheat = healthCheat;
            result.MechanicsCheat = mechanicsCheat;
            result.MovablesCheat = movablesCheat;
            result.FarmingCheat = farmingCheat;
            result.FishingCheat = fishingCheat;
            result.CanUseBrushTool = canUseBrushTool;
            result.FastMoveCheat = fastMoveCheat;
            result.TimedActionInstantCheat = timedActionInstantCheat;
            result.UnlimitedEndurance = unlimitedEndurance;
            result.UnlimitedAmmo = unlimitedAmmo;
            result.KnowAllRecipes = knowAllRecipes;
            result.Sneaking = sneaking;
            result.DeathDragDown = deathDragDown;
            result.NextOffset = cursor;
            result.Success = true;
            preview = result;
            return true;
        }

        private static bool TrySkipIsoGameCharacterReadBooks(byte[] data, ref int cursor, out int count)
        {
            count = 0;
            if (cursor + 4 > data.Length)
                return false;

            count = ReadInt32BE(data, cursor);
            cursor += 4;
            if (count < 0 || count > 2048)
                return false;

            for (int i = 0; i < count; i++)
            {
                if (!TryReadPzUtfString(data, cursor, out string _, out cursor) ||
                    !TrySkipWithin(data, ref cursor, 4, data.Length))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TrySkipPzStringList(byte[] data, ref int cursor, out int count)
        {
            count = 0;
            if (cursor + 4 > data.Length)
                return false;

            count = ReadInt32BE(data, cursor);
            cursor += 4;
            if (count < 0 || count > 4096)
                return false;

            for (int i = 0; i < count; i++)
            {
                if (!TryReadPzUtfString(data, cursor, out string _, out cursor))
                    return false;
            }

            return true;
        }

        private static bool TryFindIsoPlayerPreview(
            byte[] data,
            int startOffset,
            IReadOnlyList<InventoryItemPreview> savedItems,
            out IsoPlayerPreview preview)
        {
            preview = null;
            for (int countOffset = Math.Max(startOffset + 12, 0); countOffset < data.Length - 16; countOffset++)
            {
                int wornCount = data[countOffset];
                if (wornCount <= 0 || wornCount > 64)
                    continue;

                int cursor = countOffset + 1;
                var wornItems = new List<WornItemPreview>();
                bool ok = true;
                for (int i = 0; i < wornCount; i++)
                {
                    if (!TryReadPzUtfString(data, cursor, out string location, out cursor) ||
                        !location.StartsWith("base:", StringComparison.OrdinalIgnoreCase) ||
                        cursor + 2 > data.Length)
                    {
                        ok = false;
                        break;
                    }

                    int inventoryIndex = ReadInt16BE(data, cursor);
                    cursor += 2;
                    if (inventoryIndex < -1 || inventoryIndex >= Math.Max(savedItems.Count, 1))
                    {
                        ok = false;
                        break;
                    }

                    wornItems.Add(new WornItemPreview(location, inventoryIndex));
                }

                if (!ok || wornItems.Count < 3 || cursor + 8 > data.Length)
                    continue;

                int start = countOffset - 12;
                double hoursSurvived = ReadDoubleBE(data, start);
                int zombieKills = ReadInt32BE(data, start + 8);
                if (double.IsNaN(hoursSurvived) || double.IsInfinity(hoursSurvived) ||
                    hoursSurvived < 0 || hoursSurvived > 1000000 ||
                    zombieKills < 0 || zombieKills > 100000000)
                    continue;

                int primary = ReadInt16BE(data, cursor);
                cursor += 2;
                int secondary = ReadInt16BE(data, cursor);
                cursor += 2;
                int survivorKills = ReadInt32BE(data, cursor);
                cursor += 4;
                if (cursor + 20 > data.Length)
                    continue;

                var nutrition = new NutritionPreview
                {
                    Calories = ReadSingleBE(data, cursor),
                    Proteins = ReadSingleBE(data, cursor + 4),
                    Lipids = ReadSingleBE(data, cursor + 8),
                    Carbohydrates = ReadSingleBE(data, cursor + 12),
                    Weight = ReadSingleBE(data, cursor + 16)
                };
                if (!IsPlausibleNutrition(nutrition))
                    continue;
                cursor += 20;

                preview = new IsoPlayerPreview
                {
                    Success = true,
                    StartOffset = start,
                    WornCountOffset = countOffset,
                    NextOffset = cursor,
                    HoursSurvived = hoursSurvived,
                    ZombieKills = zombieKills,
                    PrimaryHandIndex = primary,
                    SecondaryHandIndex = secondary,
                    SurvivorKills = survivorKills,
                    Nutrition = nutrition
                };
                preview.WornItems.AddRange(wornItems);
                return true;
            }

            return false;
        }

        private static IEnumerable<ReadBookPreview> FindReadBookProgress(byte[] data, int startOffset, int endOffset)
        {
            int scanEnd = Math.Min(data.Length, endOffset);
            foreach (var item in FindPzUtfStrings(data, startOffset, scanEnd))
            {
                if (!item.Value.StartsWith("Base.Book", StringComparison.OrdinalIgnoreCase))
                    continue;

                int nextOffset = item.Offset + 2 + item.ByteLength;
                if (nextOffset + 4 > scanEnd)
                    continue;

                int pages = ReadInt32BE(data, nextOffset);
                if (pages < 0 || pages > 10000)
                    continue;

                yield return new ReadBookPreview(item.Offset, item.Value, pages);
            }
        }

        private static List<InventoryItemPreview> ExpandSavedInventoryItems(InventoryPreview inventory)
        {
            var items = new List<InventoryItemPreview>();
            foreach (var item in inventory.Items)
            {
                int repeat = Math.Max(1, item.IdenticalCount);
                for (int i = 0; i < repeat; i++)
                    items.Add(item);
            }

            return items;
        }

        private static void ApplyRegistryNames(
            InventoryPreview inventory,
            IReadOnlyDictionary<ushort, string> registryNames)
        {
            if (inventory == null || registryNames == null || registryNames.Count == 0)
                return;

            foreach (var item in EnumerateInventoryItems(inventory))
            {
                if (registryNames.TryGetValue(item.RegistryId, out string fullType))
                    item.RegistryFullType = fullType;

                item.ExtraItemFullTypes.Clear();
                foreach (ushort registryId in item.ExtraItemRegistryIds)
                {
                    item.ExtraItemFullTypes.Add(
                        registryNames.TryGetValue(registryId, out string extraFullType)
                            ? extraFullType
                            : "registry=" + registryId.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private static IEnumerable<InventoryItemPreview> EnumerateInventoryItems(InventoryPreview inventory)
        {
            if (inventory == null)
                yield break;

            foreach (var item in inventory.Items)
            {
                yield return item;

                if (item.ContainerTail?.NestedInventory == null)
                    continue;

                foreach (var nestedItem in EnumerateInventoryItems(item.ContainerTail.NestedInventory))
                    yield return nestedItem;
            }
        }

        private static IReadOnlyDictionary<ushort, string> LoadRegistryNames(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                return EmptyRegistryNames;

            string dictionaryPath;
            try
            {
                string databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
                if (string.IsNullOrWhiteSpace(databaseDirectory))
                    return EmptyRegistryNames;

                dictionaryPath = Path.Combine(databaseDirectory, "WorldDictionaryReadable.lua");
            }
            catch
            {
                return EmptyRegistryNames;
            }

            if (!File.Exists(dictionaryPath))
                return EmptyRegistryNames;

            lock (RegistryNameLock)
            {
                if (string.Equals(_cachedRegistryPath, dictionaryPath, StringComparison.OrdinalIgnoreCase))
                    return _cachedRegistryNames;

                _cachedRegistryNames = ParseWorldDictionaryReadable(dictionaryPath);
                _cachedRegistryPath = dictionaryPath;
                return _cachedRegistryNames;
            }
        }

        private static IReadOnlyDictionary<ushort, string> ParseWorldDictionaryReadable(string dictionaryPath)
        {
            var items = new Dictionary<ushort, string>();
            bool inItems = false;
            ushort? currentRegistryId = null;

            try
            {
                foreach (string rawLine in File.ReadLines(dictionaryPath))
                {
                    string line = rawLine.Trim();
                    if (!inItems)
                    {
                        if (line.Equals("items = {", StringComparison.Ordinal))
                            inItems = true;
                        continue;
                    }

                    if (line.Equals("entities = {", StringComparison.Ordinal))
                        break;

                    if (TryParseLuaRegistryId(line, out ushort registryId))
                    {
                        currentRegistryId = registryId;
                        continue;
                    }

                    if (currentRegistryId.HasValue &&
                        TryParseLuaStringField(line, "fulltype", out string fullType) &&
                        !items.ContainsKey(currentRegistryId.Value))
                    {
                        items.Add(currentRegistryId.Value, fullType);
                        currentRegistryId = null;
                    }
                }
            }
            catch
            {
                return EmptyRegistryNames;
            }

            return items.Count == 0 ? EmptyRegistryNames : items;
        }

        private static bool TryParseLuaRegistryId(string line, out ushort registryId)
        {
            registryId = 0;
            const string prefix = "registryID = ";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            int start = prefix.Length;
            int end = line.IndexOf(',', start);
            if (end < 0)
                end = line.Length;

            return ushort.TryParse(line.Substring(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out registryId);
        }

        private static bool TryParseLuaStringField(string line, string fieldName, out string value)
        {
            value = "";
            string prefix = fieldName + " = \"";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            int start = prefix.Length;
            int end = line.IndexOf('"', start);
            if (end < start)
                return false;

            value = line.Substring(start, end - start);
            return true;
        }

        private static void AppendParseNote(InventoryItemPreview item, string note)
        {
            if (string.IsNullOrEmpty(item.ParseNote))
                item.ParseNote = note;
            else
                item.ParseNote += "; " + note;
        }

        private static bool TryReadByteArrayPreview(byte[] data, ref int cursor, out ByteArrayPreview preview)
        {
            preview = null;
            if (cursor < 0 || cursor >= data.Length)
                return false;

            int start = cursor;
            int length = data[cursor++];
            if (length < 0 || cursor + length > data.Length)
                return false;

            int nonZero = 0;
            int max = 0;
            for (int i = 0; i < length; i++)
            {
                int value = data[cursor + i];
                if (value != 0)
                    nonZero++;
                if (value > max)
                    max = value;
            }

            preview = new ByteArrayPreview(start, length, nonZero, max);
            cursor += length;
            return true;
        }

        private static bool TryReadKahluaTable(byte[] data, int offset, int depth, out KahluaTablePreview table)
        {
            table = new KahluaTablePreview
            {
                StartOffset = offset,
                NextOffset = offset
            };

            if (offset < 0 || offset + 4 > data.Length)
                return false;

            int count = ReadInt32BE(data, offset);
            if (count < 0 || count > 2048)
                return false;

            table.Count = count;
            int cursor = offset + 4;
            for (int index = 0; index < count; index++)
            {
                int entryOffset = cursor;
                if (!TryReadKahluaValue(data, ref cursor, depth, out string key))
                {
                    table.NextOffset = cursor;
                    table.Error = "Failed to read key " + index.ToString(CultureInfo.InvariantCulture);
                    return false;
                }

                if (!TryReadKahluaValue(data, ref cursor, depth, out string value))
                {
                    table.NextOffset = cursor;
                    table.Error = "Failed to read value " + index.ToString(CultureInfo.InvariantCulture);
                    return false;
                }

                table.Entries.Add(new KahluaEntryPreview(entryOffset, key, value));
            }

            table.Success = true;
            table.NextOffset = cursor;
            return true;
        }

        private static bool TryReadKahluaValue(byte[] data, ref int offset, int depth, out string value)
        {
            value = "";
            if (offset >= data.Length)
                return false;

            byte type = data[offset++];
            switch (type)
            {
                case 0:
                    if (!TryReadPzUtfString(data, offset, out string text, out offset))
                        return false;
                    value = text;
                    return true;

                case 1:
                    if (offset + 8 > data.Length)
                        return false;
                    value = FormatDouble(ReadDoubleBE(data, offset));
                    offset += 8;
                    return true;

                case 2:
                    if (!TryReadKahluaTable(data, offset, depth + 1, out KahluaTablePreview nested) || !nested.Success)
                        return false;
                    offset = nested.NextOffset;
                    value = FormatKahluaTableInline(nested, depth);
                    return true;

                case 3:
                    if (offset >= data.Length)
                        return false;
                    value = data[offset++] != 0 ? "true" : "false";
                    return true;

                default:
                    return false;
            }
        }

        private static string FormatKahluaTableInline(KahluaTablePreview table, int depth)
        {
            if (table == null || !table.Success)
                return "table(?)";

            if (depth >= 1)
                return $"table({table.Count} entrie(s))";

            var parts = table.Entries
                .Take(8)
                .Select(x => "[" + x.Key + "]=" + x.Value)
                .ToList();

            string suffix = table.Entries.Count > 8 ? ", ..." : "";
            return "table{" + string.Join(", ", parts) + suffix + "}";
        }

        private static string DescribeStringHit(PzStringHit item, PlayerDescriptorPreview player)
        {
            if (player != null && player.Success)
            {
                if (item.Offset == player.ForenameOffset)
                    return "  [first name]";
                if (item.Offset == player.SurnameOffset)
                    return "  [last name]";
                if (item.Offset == player.TorsoOffset)
                    return "  [torso/model preset; legacy Bob/Kate naming]";
                if (item.Offset == player.ProfessionOffset)
                    return "  [profession: " + FormatProfession(player.Profession) + "]";
            }

            if (item.Value.Equals("hotbar", StringComparison.OrdinalIgnoreCase))
                return "  [Lua modData key: hotbar attachment table]";

            if (item.Value.Equals("Back", StringComparison.OrdinalIgnoreCase))
                return "  [hotbar/body attachment slot]";

            if (item.Value.StartsWith("base:", StringComparison.OrdinalIgnoreCase) &&
                ProfessionNames.ContainsKey(item.Value))
                return "  [profession: " + FormatProfession(item.Value) + "]";

            if (item.Value.Equals("Bob", StringComparison.OrdinalIgnoreCase) ||
                item.Value.Equals("Kate", StringComparison.OrdinalIgnoreCase))
                return "  [legacy character model/preset]";

            return "";
        }

        private static string FormatItemVisualSummary(ItemVisualPreview visual)
        {
            string name = FirstNonEmpty(visual.FullType, visual.ClothingItemName, visual.AlternateModelName, "<unnamed visual>");
            var details = new List<string>
            {
                $"@{FormatOffset(visual.StartOffset)}",
                $"flags=0x{visual.Flags1:X2}",
                name
            };

            if (!string.IsNullOrWhiteSpace(visual.ClothingItemName) &&
                !visual.ClothingItemName.Equals(name, StringComparison.Ordinal))
                details.Add("clothing=" + visual.ClothingItemName);

            string arrays = FormatVisualArrays(visual);
            if (!string.IsNullOrEmpty(arrays))
                details.Add(arrays);

            return string.Join(" | ", details);
        }

        private static string FormatInventoryItemSummary(InventoryItemPreview item)
        {
            var parts = new List<string>
            {
                $"@{FormatOffset(item.ItemStartOffset)}",
                $"x{item.IdenticalCount}",
                $"registry={item.RegistryId}",
                $"itemId={item.ItemId}",
                $"len={item.DataLength}",
                $"saveType={FormatSaveType(item.SaveType)}",
                $"header=0x{item.Header:X2} ({FormatItemHeaderFlags(item.Header)})"
            };

            if (!string.IsNullOrWhiteSpace(item.RegistryFullType))
                parts.Add("type=" + item.RegistryFullType);

            if (item.HasCurrentUses)
                parts.Add("uses=" + item.CurrentUses.ToString(CultureInfo.InvariantCulture));
            else
                parts.Add("uses=1/default");

            if (item.HasCondition)
                parts.Add("condition=" + item.Condition.ToString(CultureInfo.InvariantCulture));
            else
                parts.Add("condition=default/max");

            if (item.HasItemCapacity)
                parts.Add("itemCapacity=" + FormatSingle(item.ItemCapacity));

            if (item.Visual != null && item.Visual.Success)
                parts.Add("visual=" + FirstNonEmpty(item.Visual.FullType, item.Visual.ClothingItemName, item.Visual.AlternateModelName, "<unnamed>"));
            else
                parts.Add("hint=" + FormatStringHints(item.StringHints));

            if (item.Visual != null && item.Visual.Success)
            {
                string arrays = FormatVisualArrays(item.Visual);
                if (!string.IsNullOrEmpty(arrays))
                    parts.Add(arrays);
            }

            if (item.HasNestedFlags)
                parts.Add($"extra=0x{item.NestedFlags:X8} ({FormatItemNestedFlags(item.NestedFlags)})");

            if (item.ModDataEntryCount > 0)
                parts.Add("modDataEntries=" + item.ModDataEntryCount.ToString(CultureInfo.InvariantCulture));

            if (item.HasCommonKeyId)
                parts.Add("keyId=" + item.CommonKeyId.ToString(CultureInfo.InvariantCulture));

            if (item.ExtraItemRegistryIds.Count > 0)
                parts.Add("extraItems=[" + FormatRegistryItemList(item.ExtraItemRegistryIds, item.ExtraItemFullTypes) + "]");

            if (item.HasCurrentAmmoCount)
                parts.Add("ammo=" + FormatAmmoState(item));

            if (item.HasAttachedSlot)
                parts.Add("attachedSlot=" + item.AttachedSlot.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(item.AttachedSlotType))
                parts.Add("attachedSlotType=" + item.AttachedSlotType);

            if (!string.IsNullOrWhiteSpace(item.AttachedToModel))
                parts.Add("attachedToModel=" + item.AttachedToModel);

            if (item.HasMaxCapacity)
                parts.Add("maxCapacity=" + item.MaxCapacity.ToString(CultureInfo.InvariantCulture));

            if (item.EntityPayload?.FluidContainer != null)
                parts.Add(FormatFluidContainer(item.EntityPayload.FluidContainer));

            if (item.ContainerTail != null)
                parts.Add(FormatInventoryContainerTail(item.ContainerTail));
            else if (item.ClothingTail != null)
                parts.Add(FormatClothingTail(item.ClothingTail));
            else if (item.LiteratureTail != null)
                parts.Add(FormatLiteratureTail(item.LiteratureTail));
            else if (item.KeyTail != null)
                parts.Add(FormatKeyTail(item.KeyTail));
            else if (item.HandWeaponTail != null)
                parts.Add(FormatHandWeaponTail(item));
            else if (item.SubtypeBytes > 0)
                parts.Add("subtypeBytes=" + item.SubtypeBytes.ToString(CultureInfo.InvariantCulture));

            if (item.DuplicateIds.Count > 0)
                parts.Add("duplicateIds=" + string.Join(", ", item.DuplicateIds));

            if (!string.IsNullOrEmpty(item.ParseNote))
                parts.Add(item.ParseNote);

            return string.Join(" | ", parts);
        }

        private static string FormatSelectedCharacterStats(CharacterStatsPreview stats)
        {
            string[] selected =
            {
                "Endurance", "Fatigue", "Fitness", "Hunger", "Panic", "Stress", "Temperature", "Thirst", "Wetness"
            };

            var values = stats.Stats
                .Where(x => selected.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
                .Select(x => x.Name + "=" + FormatSingle(x.Value));

            return string.Join(", ", values);
        }

        private static string FormatXpEntries(IReadOnlyList<NamedFloat> entries)
        {
            if (entries == null || entries.Count == 0)
                return "none";

            return string.Join(", ", entries.Take(8).Select(x => x.Name + "=" + FormatSingle(x.Value)));
        }

        private static string FormatPerkLevels(IReadOnlyList<NamedInt> levels)
        {
            if (levels == null || levels.Count == 0)
                return "none";

            return string.Join(", ", levels.Take(8).Select(x => x.Name + "=" + x.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private static string FormatPerkLevelsForSheet(IReadOnlyList<NamedInt> levels)
        {
            if (levels == null || levels.Count == 0)
                return "none";

            return string.Join(
                ", ",
                levels
                    .Where(x => x.Value > 0)
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .Select(x => x.Name + "=" + x.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private static string FormatTraitName(string trait)
        {
            if (string.IsNullOrWhiteSpace(trait))
                return "unknown";

            return trait.StartsWith("base:", StringComparison.OrdinalIgnoreCase)
                ? trait.Substring("base:".Length)
                : trait;
        }

        private static IEnumerable<string> BuildAttachedItemLines(IReadOnlyList<InventoryItemPreview> savedItems)
        {
            if (savedItems == null)
                yield break;

            for (int i = 0; i < savedItems.Count; i++)
            {
                var item = savedItems[i];
                if (!HasAttachedItemState(item))
                    continue;

                string state = FormatInventoryItemCompactState(item);
                yield return "#" + i.ToString(CultureInfo.InvariantCulture) + " " +
                             FormatInventoryItemName(item) +
                             (string.IsNullOrEmpty(state) ? "" : " (" + state + ")");
            }
        }

        private static string FormatInventoryItemForSheet(InventoryItemPreview item)
        {
            string prefix = item.IdenticalCount > 1
                ? "x" + item.IdenticalCount.ToString(CultureInfo.InvariantCulture) + " "
                : "";
            string state = FormatInventoryItemCompactState(item);
            string text = prefix + FormatInventoryItemName(item) +
                          (string.IsNullOrEmpty(state) ? "" : " (" + state + ")");

            var nested = item.ContainerTail?.NestedInventory;
            if (nested != null && nested.Items.Count > 0)
            {
                var contents = nested.Items
                    .Take(5)
                    .Select(FormatInventoryContainerContent)
                    .ToList();
                if (nested.Items.Count > 5)
                    contents.Add("...");

                text += " -> " + string.Join("; ", contents);
            }

            return text;
        }

        private static string FormatSavedItemReference(int index, IReadOnlyList<InventoryItemPreview> savedItems)
        {
            if (index < 0)
                return "<none>";

            if (savedItems == null || index >= savedItems.Count)
                return "<missing item>";

            var item = savedItems[index];
            string state = FormatInventoryItemCompactState(item);
            return "#" + index.ToString(CultureInfo.InvariantCulture) + " " + FormatInventoryItemName(item) +
                   (string.IsNullOrEmpty(state) ? "" : " (" + state + ")");
        }

        private static void AppendHandLoadout(
            StringBuilder output,
            IsoPlayerPreview isoPlayer,
            IReadOnlyList<InventoryItemPreview> savedItems)
        {
            if (isoPlayer == null || savedItems == null)
                return;

            output.AppendLine("  Hand loadout:");
            if (isoPlayer.PrimaryHandIndex >= 0 &&
                isoPlayer.PrimaryHandIndex == isoPlayer.SecondaryHandIndex)
            {
                output.AppendLine("    both hands -> " + FormatSavedItemReference(isoPlayer.PrimaryHandIndex, savedItems));
                return;
            }

            output.AppendLine("    primary -> " + FormatSavedItemReference(isoPlayer.PrimaryHandIndex, savedItems));
            output.AppendLine("    secondary -> " + FormatSavedItemReference(isoPlayer.SecondaryHandIndex, savedItems));
        }

        private static void AppendAttachedItems(
            StringBuilder output,
            IReadOnlyList<InventoryItemPreview> savedItems)
        {
            if (savedItems == null || savedItems.Count == 0)
                return;

            var lines = new List<string>();
            for (int i = 0; i < savedItems.Count; i++)
            {
                var item = savedItems[i];
                if (!HasAttachedItemState(item))
                    continue;

                string state = FormatInventoryItemCompactState(item);
                lines.Add(
                    "    #" + i.ToString(CultureInfo.InvariantCulture) + " " +
                    FormatInventoryItemName(item) +
                    (string.IsNullOrEmpty(state) ? "" : " (" + state + ")"));
            }

            if (lines.Count == 0)
                return;

            output.AppendLine("  Attached items:");
            foreach (string line in lines.Take(16))
                output.AppendLine(line);
        }

        private static void AppendAmmunitionPreview(StringBuilder output, InventoryPreview inventory)
        {
            var lines = new List<string>();
            CollectAmmunitionLines(inventory, "inventory", lines);
            if (lines.Count == 0)
                return;

            output.AppendLine("  Ammunition:");
            foreach (string line in lines.Take(20))
                output.AppendLine(line);

            if (lines.Count > 20)
                output.AppendLine($"    ... {lines.Count - 20} more ammunition item(s)");
        }

        private static void CollectAmmunitionLines(
            InventoryPreview inventory,
            string location,
            List<string> lines)
        {
            if (inventory == null)
                return;

            foreach (var item in inventory.Items)
            {
                if (IsAmmunitionItem(item))
                {
                    string state = FormatInventoryItemCompactState(item);
                    string prefix = item.IdenticalCount > 1
                        ? "x" + item.IdenticalCount.ToString(CultureInfo.InvariantCulture) + " "
                        : "";
                    lines.Add(
                        "    " + location + ": " + prefix + FormatInventoryItemName(item) +
                        (string.IsNullOrEmpty(state) ? "" : " (" + state + ")"));
                }

                if (item.ContainerTail?.NestedInventory != null)
                    CollectAmmunitionLines(
                        item.ContainerTail.NestedInventory,
                        "inside " + FormatInventoryItemName(item),
                        lines);
            }
        }

        private static bool HasAttachedItemState(InventoryItemPreview item)
            => item != null &&
               (item.HasAttachedSlot ||
                !string.IsNullOrWhiteSpace(item.AttachedSlotType) ||
                !string.IsNullOrWhiteSpace(item.AttachedToModel));

        private static bool IsAmmunitionItem(InventoryItemPreview item)
        {
            if (item == null)
                return false;

            string identity = string.Join(" ", new[]
            {
                FormatInventoryItemName(item),
                item.RegistryFullType,
                string.Join(" ", item.StringHints ?? new List<string>())
            });

            return identity.IndexOf("Ammo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Bullets", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Cartridge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Round", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identity.IndexOf("Clip", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatInventoryItemName(InventoryItemPreview item)
        {
            if (item == null)
                return "<null>";

            if (item.Visual != null && item.Visual.Success)
                return FirstNonEmpty(item.Visual.FullType, item.Visual.ClothingItemName, item.Visual.AlternateModelName, "<unnamed>");

            string hint = SelectBestItemHint(item.StringHints);
            if (!string.IsNullOrWhiteSpace(item.RegistryFullType) && IsGenericItemHint(hint))
                return item.RegistryFullType;

            return FirstNonEmpty(hint, item.RegistryFullType, "registry=" + item.RegistryId.ToString(CultureInfo.InvariantCulture));
        }

        private static string SelectBestItemHint(IReadOnlyList<string> hints)
        {
            if (hints == null || hints.Count == 0)
                return "";

            var useful = hints
                .Where(IsMeaningfulItemHint)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (useful.Count == 0)
                return "";

            string keyRing = useful.FirstOrDefault(x => x.IndexOf("Key Ring", StringComparison.OrdinalIgnoreCase) >= 0) ??
                             useful.FirstOrDefault(x => x.Equals("KeyRing", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(keyRing))
                return keyRing;

            string waterBottle = useful.FirstOrDefault(x => x.IndexOf("Water Bottle", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!string.IsNullOrEmpty(waterBottle))
                return waterBottle;

            string namedKey = useful.FirstOrDefault(x => x.StartsWith("Key - ", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(namedKey))
                return namedKey;

            string baseType = useful.FirstOrDefault(x => x.StartsWith("Base.", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(baseType))
                return baseType;

            return useful[0];
        }

        private static bool IsGenericItemHint(string hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
                return true;

            return hint.Equals("Single", StringComparison.OrdinalIgnoreCase) ||
                   hint.Equals("Back", StringComparison.OrdinalIgnoreCase) ||
                   hint.IndexOf(" On Back", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   hint.StartsWith("Tooltip", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMeaningfulItemHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.Equals("customName", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Tooltip", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Tooltip_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool LooksLikeKeyItem(IReadOnlyList<string> hints)
        {
            return hints != null && hints.Any(x =>
                !string.IsNullOrWhiteSpace(x) &&
                (x.IndexOf("Key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 x.StartsWith("Base.Key", StringComparison.OrdinalIgnoreCase)));
        }

        private static string FormatHumanVisualDetails(HumanVisualPreview visual)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(visual.HairModel))
                parts.Add("hair=" + visual.HairModel);
            if (!string.IsNullOrWhiteSpace(visual.BeardModel))
                parts.Add("beard=" + visual.BeardModel);
            if (!string.IsNullOrWhiteSpace(visual.SkinTextureName))
                parts.Add("skinTextureName=" + visual.SkinTextureName);
            if (!string.IsNullOrWhiteSpace(visual.NonAttachedHair))
                parts.Add("nonAttachedHair=" + visual.NonAttachedHair);
            return string.Join(", ", parts);
        }

        private static string FormatNutritionSuffix(NutritionPreview nutrition)
        {
            if (nutrition == null)
                return "";

            return ", nutrition=" +
                   $"weight={FormatSingle(nutrition.Weight)}, calories={FormatSingle(nutrition.Calories)}, " +
                   $"proteins={FormatSingle(nutrition.Proteins)}, lipids={FormatSingle(nutrition.Lipids)}, " +
                   $"carbs={FormatSingle(nutrition.Carbohydrates)}";
        }

        private static string FormatIsoGameCharacterTail(IsoGameCharacterTailPreview tail)
        {
            var parts = new List<string>
            {
                $"IsoGameCharacter tail @{FormatOffset(tail.StartOffset)}",
                "leftHandInventoryIndex=" + tail.LeftHandInventoryIndex.ToString(CultureInfo.InvariantCulture),
                "rightHandInventoryIndex=" + tail.RightHandInventoryIndex.ToString(CultureInfo.InvariantCulture),
                "onFire=" + FormatBool(tail.OnFire),
                "deathDragDown=" + FormatBool(tail.DeathDragDown),
                "sneaking=" + FormatBool(tail.Sneaking),
                "readBooks=" + tail.ReadBookCount.ToString(CultureInfo.InvariantCulture),
                "knownRecipes=" + tail.KnownRecipeCount.ToString(CultureInfo.InvariantCulture)
            };

            if (!IsNearlyZero(tail.PainEffect) || !IsNearlyZero(tail.PainDelta))
                parts.Add("painEffect=" + FormatSingle(tail.PainEffect) + "/" + FormatSingle(tail.PainDelta));
            if (!IsNearlyZero(tail.ReduceInfectionPower))
                parts.Add("reduceInfectionPower=" + FormatSingle(tail.ReduceInfectionPower));

            string cheats = FormatActiveCheats(tail);
            if (!string.IsNullOrEmpty(cheats))
                parts.Add("activeCheats=" + cheats);

            return string.Join(", ", parts);
        }

        private static string FormatActiveCheats(IsoGameCharacterTailPreview tail)
        {
            var active = new List<string>();
            if (tail.UnlimitedCarry) active.Add("unlimitedCarry");
            if (tail.BuildCheat) active.Add("build");
            if (tail.HealthCheat) active.Add("health");
            if (tail.MechanicsCheat) active.Add("mechanics");
            if (tail.MovablesCheat) active.Add("movables");
            if (tail.FarmingCheat) active.Add("farming");
            if (tail.FishingCheat) active.Add("fishing");
            if (tail.CanUseBrushTool) active.Add("brushTool");
            if (tail.FastMoveCheat) active.Add("fastMove");
            if (tail.TimedActionInstantCheat) active.Add("instantActions");
            if (tail.UnlimitedEndurance) active.Add("unlimitedEndurance");
            if (tail.UnlimitedAmmo) active.Add("unlimitedAmmo");
            if (tail.KnowAllRecipes) active.Add("knowAllRecipes");
            return string.Join("+", active);
        }

        private static void AppendDeathState(
            StringBuilder output,
            IReadOnlyDictionary<string, object> rowValues,
            InventoryPreview inventory,
            BodyDamagePreview bodyDamage,
            IsoGameCharacterTailPreview tail)
        {
            bool rowHasDeath = TryGetBool(rowValues, "isDead", out bool isDead);
            bool hasDeathEvidence = rowHasDeath && isDead ||
                                    tail?.DeathDragDown == true ||
                                    bodyDamage != null && bodyDamage.Parts.Any(x => x.Bitten || x.Bleeding || x.InfectedWound);
            if (!hasDeathEvidence)
                return;

            var lines = new List<string>();
            if (rowHasDeath)
                lines.Add("SQLite localPlayers.isDead=" + FormatBool(isDead));
            if (inventory != null && inventory.TotalItems == 0)
            {
                lines.Add("serialized inventory is empty");
                if (rowHasDeath && isDead)
                    lines.Add("items likely moved to the world zombie/corpse object");
            }
            if (tail != null)
            {
                lines.Add("left/right hand indexes=" +
                          tail.LeftHandInventoryIndex.ToString(CultureInfo.InvariantCulture) + "/" +
                          tail.RightHandInventoryIndex.ToString(CultureInfo.InvariantCulture));
                lines.Add("deathDragDown=" + FormatBool(tail.DeathDragDown));
            }

            if (bodyDamage != null)
            {
                int bitten = bodyDamage.Parts.Count(x => x.Bitten);
                int bleeding = bodyDamage.Parts.Count(x => x.Bleeding || x.BleedingTime > 0.0001f);
                int infectedWounds = bodyDamage.Parts.Count(x => x.InfectedWound);
                if (bitten > 0)
                    lines.Add("bittenParts=" + bitten.ToString(CultureInfo.InvariantCulture));
                if (bleeding > 0)
                    lines.Add("bleedingParts=" + bleeding.ToString(CultureInfo.InvariantCulture));
                if (infectedWounds > 0)
                    lines.Add("infectedWounds=" + infectedWounds.ToString(CultureInfo.InvariantCulture));
            }

            output.AppendLine("  Death/save state: " + string.Join(", ", lines));
        }

        private static bool IsPlausibleNutrition(NutritionPreview nutrition)
        {
            if (nutrition == null)
                return false;

            float[] values =
            {
                nutrition.Calories, nutrition.Proteins, nutrition.Lipids, nutrition.Carbohydrates, nutrition.Weight
            };
            if (values.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                return false;

            return nutrition.Weight >= 20f &&
                   nutrition.Weight <= 300f &&
                   nutrition.Calories >= -100000f &&
                   nutrition.Calories <= 1000000f;
        }

        private static List<string> BuildDerivedMoodles(CharacterStatsPreview stats, BodyDamagePreview bodyDamage)
        {
            var moodles = new List<string>();

            AddInverseMoodle(moodles, stats, "Endurance", "Endurance", 0.75f, 0.5f, 0.25f, 0.1f);
            AddForwardMoodle(moodles, stats, "Tired", "Fatigue", 0.6f, 0.7f, 0.8f, 0.9f);
            AddForwardMoodle(moodles, stats, "Hungry", "Hunger", 0.15f, 0.25f, 0.45f, 0.7f);
            AddForwardMoodle(moodles, stats, "Panic", "Panic", 6f, 30f, 65f, 80f);
            AddForwardMoodle(moodles, stats, "Bored", "Boredom", 25f, 50f, 75f, 90f);
            AddForwardMoodle(moodles, stats, "Unhappy", "Unhappiness", 20f, 45f, 60f, 80f);
            AddForwardMoodle(moodles, stats, "Stress", "Stress", 0.25f, 0.5f, 0.75f, 0.9f);
            AddForwardMoodle(moodles, stats, "Thirst", "Thirst", 0.12f, 0.25f, 0.7f, 0.84f);
            AddForwardMoodle(moodles, stats, "Pain", "Pain", 10f, 20f, 50f, 75f);
            AddForwardMoodle(moodles, stats, "Wet", "Wetness", 15f, 40f, 70f, 90f);
            AddForwardMoodle(moodles, stats, "Uncomfortable", "Discomfort", 20f, 40f, 60f, 80f);

            if (TryGetStat(stats, "Temperature", out float temperature))
            {
                if (temperature > 37.5f)
                    AddMoodle(moodles, "Hyperthermia", "Temperature", temperature, MoodleLevelFromForward(temperature, 37.5f, 39f, 40f, 41f));
                else if (temperature < 36.5f)
                    AddMoodle(moodles, "Hypothermia", "Temperature", temperature, MoodleLevelFromReverse(temperature, 36.5f, 35f, 30f, 25f));
            }

            if (bodyDamage != null)
            {
                int bleedingParts = bodyDamage.Parts.Count(x => x.Bleeding || x.BleedingTime > 0.0001f);
                if (bleedingParts > 0)
                    moodles.Add("Bleeding=" + FormatMoodleLevel(Math.Min(4, bleedingParts)) + " (parts=" + bleedingParts.ToString(CultureInfo.InvariantCulture) + ")");

                int coldLevel = MoodleLevelFromForward(bodyDamage.ColdStrength, 20f, 40f, 60f, 75f);
                if (coldLevel > 0)
                    AddMoodle(moodles, "HasCold", "ColdStrength", bodyDamage.ColdStrength, coldLevel);
            }

            return moodles;
        }

        private static void AppendDerivedMoodles(
            StringBuilder output,
            CharacterStatsPreview stats,
            BodyDamagePreview bodyDamage)
        {
            var moodles = BuildDerivedMoodles(stats, bodyDamage);
            if (moodles.Count == 0)
                return;

            output.AppendLine("  Derived moodles: " + string.Join(", ", moodles.Take(12)));
        }

        private static void AddForwardMoodle(
            List<string> moodles,
            CharacterStatsPreview stats,
            string moodleName,
            string statName,
            float low,
            float moderate,
            float high,
            float max)
        {
            if (!TryGetStat(stats, statName, out float value))
                return;

            AddMoodle(moodles, moodleName, statName, value, MoodleLevelFromForward(value, low, moderate, high, max));
        }

        private static void AddInverseMoodle(
            List<string> moodles,
            CharacterStatsPreview stats,
            string moodleName,
            string statName,
            float low,
            float moderate,
            float high,
            float max)
        {
            if (!TryGetStat(stats, statName, out float value))
                return;

            AddMoodle(moodles, moodleName, statName, value, MoodleLevelFromInverse(value, low, moderate, high, max));
        }

        private static void AddMoodle(List<string> moodles, string moodleName, string statName, float value, int level)
        {
            if (level <= 0)
                return;

            moodles.Add($"{moodleName}={FormatMoodleLevel(level)} ({statName}={FormatSingle(value)})");
        }

        private static int MoodleLevelFromForward(float value, float low, float moderate, float high, float max)
        {
            if (value > max) return 4;
            if (value > high) return 3;
            if (value > moderate) return 2;
            if (value > low) return 1;
            return 0;
        }

        private static int MoodleLevelFromReverse(float value, float low, float moderate, float high, float max)
        {
            if (value < max) return 4;
            if (value < high) return 3;
            if (value < moderate) return 2;
            if (value < low) return 1;
            return 0;
        }

        private static int MoodleLevelFromInverse(float value, float low, float moderate, float high, float max)
        {
            if (value <= max) return 4;
            if (value <= high) return 3;
            if (value <= moderate) return 2;
            if (value <= low) return 1;
            return 0;
        }

        private static string FormatMoodleLevel(int level)
        {
            switch (level)
            {
                case 1: return "Low";
                case 2: return "Moderate";
                case 3: return "High";
                case 4: return "Max";
                default: return "None";
            }
        }

        private static bool TryGetStat(CharacterStatsPreview stats, string name, out float value)
        {
            value = 0f;
            if (stats == null)
                return false;

            var match = stats.Stats.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(match.Name))
                return false;

            value = match.Value;
            return true;
        }

        private static string FormatBodyDamageSummary(BodyDamagePreview bodyDamage)
        {
            if (bodyDamage == null || !bodyDamage.Success)
                return "Body health: not decoded";

            bool allPartsClean = bodyDamage.Parts.Count == BodyPartNames.Length &&
                                 bodyDamage.Parts.All(IsCleanBodyPart);
            bool mainFieldsQuiet = IsNearlyZero(bodyDamage.CatchCold) &&
                                   !bodyDamage.HasCold &&
                                   IsNearlyZero(bodyDamage.ColdStrength) &&
                                   IsNearlyZero(bodyDamage.HealthFromFoodTimer) &&
                                   IsNearlyZero(bodyDamage.PainReduction) &&
                                   IsNearlyZero(bodyDamage.ColdReduction) &&
                                   IsQuietInfectionTimer(bodyDamage.InfectionTime) &&
                                   IsQuietInfectionTimer(bodyDamage.InfectionMortalityDuration) &&
                                   IsNearlyZero(bodyDamage.ColdDamageStage);

            if (allPartsClean && mainFieldsQuiet)
            {
                return $"Body health @{FormatOffset(bodyDamage.StartOffset)}: all {bodyDamage.Parts.Count} body parts at 100, no wounds";
            }

            var parts = bodyDamage.Parts
                .Where(part => !IsCleanBodyPart(part))
                .Take(8)
                .Select(FormatBodyPartDamage)
                .ToList();
            if (parts.Count == 0)
                parts.Add("all body parts have health=100/no wounds");

            if (bodyDamage.HasCold || !IsNearlyZero(bodyDamage.CatchCold) || !IsNearlyZero(bodyDamage.ColdStrength))
                parts.Add($"cold catch={FormatSingle(bodyDamage.CatchCold)} strength={FormatSingle(bodyDamage.ColdStrength)}");
            if (!IsQuietInfectionTimer(bodyDamage.InfectionTime) || !IsQuietInfectionTimer(bodyDamage.InfectionMortalityDuration))
                parts.Add($"infection time={FormatSingle(bodyDamage.InfectionTime)} mortality={FormatSingle(bodyDamage.InfectionMortalityDuration)}");

            return $"Body health @{FormatOffset(bodyDamage.StartOffset)}: " + string.Join("; ", parts);
        }

        private static void AppendBodyThermalPreview(StringBuilder output, BodyDamagePreview bodyDamage)
        {
            var thermoregulator = bodyDamage?.Thermoregulator;
            if (thermoregulator == null)
            {
                if (!string.IsNullOrWhiteSpace(bodyDamage?.ThermoregulatorNote))
                    output.AppendLine("  Body temperature: " + bodyDamage.ThermoregulatorNote);
                return;
            }

            output.AppendLine(
                $"  Body temperature @{FormatOffset(thermoregulator.StartOffset)}: " +
                $"setPoint={FormatSingle(thermoregulator.SetPoint)}, " +
                $"metabolic={FormatSingle(thermoregulator.MetabolicRateReal)}, " +
                $"bodyHeatDelta={FormatSingle(thermoregulator.BodyHeatDelta)}, " +
                $"coreHeatDelta={FormatSingle(thermoregulator.CoreHeatDelta)}, " +
                $"thermalDamage={FormatSingle(thermoregulator.ThermalDamage)}");

            var warmNodes = thermoregulator.Nodes
                .Where(node => Math.Abs(node.Celsius - thermoregulator.SetPoint) > 0.05f ||
                               Math.Abs(node.SkinCelsius - 33f) > 0.05f ||
                               Math.Abs(node.HeatDelta) > 0.0001f ||
                               Math.Abs(node.Insulation) > 0.0001f ||
                               Math.Abs(node.BodyWetness) > 0.0001f ||
                               Math.Abs(node.ClothingWetness) > 0.0001f)
                .Take(18)
                .Select(FormatThermalNode)
                .ToList();

            if (warmNodes.Count > 0)
            {
                output.AppendLine("  Body part temperatures:");
                foreach (string line in warmNodes)
                    output.AppendLine("    " + line);
            }
        }

        private static string FormatThermalNode(ThermalNodePreview node)
        {
            var parts = new List<string>
            {
                node.BodyPartName,
                "core=" + FormatSingle(node.Celsius),
                "skin=" + FormatSingle(node.SkinCelsius)
            };

            if (!IsNearlyZero(node.HeatDelta))
                parts.Add("heatDelta=" + FormatSingle(node.HeatDelta));
            if (!IsNearlyZero(node.PrimaryDelta))
                parts.Add("primaryDelta=" + FormatSingle(node.PrimaryDelta));
            if (!IsNearlyZero(node.SecondaryDelta))
                parts.Add("secondaryDelta=" + FormatSingle(node.SecondaryDelta));
            if (!IsNearlyZero(node.Insulation))
                parts.Add("insulation=" + FormatSingle(node.Insulation));
            if (!IsNearlyZero(node.WindResist))
                parts.Add("windResist=" + FormatSingle(node.WindResist));
            if (!IsNearlyZero(node.BodyWetness))
                parts.Add("bodyWetness=" + FormatSingle(node.BodyWetness));
            if (!IsNearlyZero(node.ClothingWetness))
                parts.Add("clothingWetness=" + FormatSingle(node.ClothingWetness));

            return string.Join(", ", parts);
        }

        private static void AppendBodyPartEffects(StringBuilder output, BodyDamagePreview bodyDamage)
        {
            if (bodyDamage == null)
                return;

            var affectedParts = bodyDamage.Parts
                .Where(HasBodyPartEffect)
                .Take(18)
                .Select(FormatBodyPartEffect)
                .ToList();

            if (affectedParts.Count == 0)
                return;

            output.AppendLine("  Body part effects:");
            foreach (string line in affectedParts)
                output.AppendLine("    " + line);
        }

        private static bool HasBodyPartEffect(BodyPartDamagePreview part)
        {
            return !IsNearlyZero(part.AdditionalPain) ||
                   !IsNearlyZero(part.Stiffness) ||
                   !IsNearlyZero(part.Wetness) ||
                   !IsNearlyZero(part.AlcoholLevel) ||
                   !IsNearlyZero(part.ComfreyFactor) ||
                   !IsNearlyZero(part.GarlicFactor) ||
                   !IsNearlyZero(part.PlantainFactor);
        }

        private static string FormatBodyPartEffect(BodyPartDamagePreview part)
        {
            var values = new List<string> { part.Name };
            if (!IsNearlyZero(part.AdditionalPain))
                values.Add("pain=" + FormatSingle(part.AdditionalPain));
            if (!IsNearlyZero(part.Stiffness))
                values.Add("muscleStrain=" + FormatSingle(part.Stiffness));
            if (!IsNearlyZero(part.Wetness))
                values.Add("wetness=" + FormatSingle(part.Wetness));
            if (!IsNearlyZero(part.AlcoholLevel))
                values.Add("alcohol=" + FormatSingle(part.AlcoholLevel));
            if (!IsNearlyZero(part.ComfreyFactor))
                values.Add("comfrey=" + FormatSingle(part.ComfreyFactor));
            if (!IsNearlyZero(part.GarlicFactor))
                values.Add("garlic=" + FormatSingle(part.GarlicFactor));
            if (!IsNearlyZero(part.PlantainFactor))
                values.Add("plantain=" + FormatSingle(part.PlantainFactor));

            return string.Join(", ", values);
        }

        private static string FormatBodyPartDamage(BodyPartDamagePreview part)
        {
            var flags = new List<string>();
            if (part.Cut) flags.Add("cut");
            if (part.Bitten) flags.Add("bitten");
            if (part.Scratched) flags.Add("scratched");
            if (part.Bandaged) flags.Add("bandaged");
            if (part.Bleeding) flags.Add("bleeding");
            if (part.DeepWounded) flags.Add("deepWounded");
            if (part.InfectedWound) flags.Add("infectedWound");
            if (part.HaveGlass) flags.Add("glass");
            if (part.HaveBullet) flags.Add("bullet");
            if (part.Splint) flags.Add("splint");
            if (part.BurnTime > 0.0001f) flags.Add("burn=" + FormatSingle(part.BurnTime));
            if (!IsNearlyZero(part.AdditionalPain)) flags.Add("pain=" + FormatSingle(part.AdditionalPain));
            if (!IsNearlyZero(part.Stiffness)) flags.Add("muscleStrain=" + FormatSingle(part.Stiffness));
            if (!IsNearlyZero(part.Wetness)) flags.Add("wetness=" + FormatSingle(part.Wetness));
            return $"{part.Name} health={FormatSingle(part.Health)}" +
                   (flags.Count > 0 ? " " + string.Join(",", flags) : "");
        }

        private static string FormatBodyPartFlags(BodyPartDamagePreview part)
        {
            var flags = new List<string>();
            if (part.Cut) flags.Add("cut");
            if (part.Bitten) flags.Add("bitten");
            if (part.Scratched) flags.Add("scratched");
            if (part.Bandaged) flags.Add("bandaged");
            if (part.Bleeding) flags.Add("bleeding");
            if (part.DeepWounded) flags.Add("deepWounded");
            if (part.InfectedWound) flags.Add("infectedWound");
            if (part.HaveGlass) flags.Add("glass");
            if (part.HaveBullet) flags.Add("bullet");
            if (part.Splint) flags.Add("splint");
            return flags.Count == 0 ? "clean" : string.Join(", ", flags);
        }

        private static bool IsCleanBodyPart(BodyPartDamagePreview part)
        {
            return Math.Abs(part.Health - 100f) <= 0.01f &&
                   !part.Cut &&
                   !part.Bitten &&
                   !part.Scratched &&
                   !part.Bandaged &&
                   !part.Bleeding &&
                   !part.DeepWounded &&
                   !part.FakeInfected &&
                   !part.Infected &&
                   !part.InfectedWound &&
                   !part.HaveGlass &&
                   !part.Stitched &&
                   !part.Splint &&
                   !part.HaveBullet &&
                   !part.NeedBurnWash &&
                   IsNearlyZero(part.CutTime) &&
                   IsNearlyZero(part.BiteTime) &&
                   IsNearlyZero(part.ScratchTime) &&
                   IsNearlyZero(part.BleedingTime) &&
                   IsNearlyZero(part.DeepWoundTime) &&
                   IsNearlyZero(part.FractureTime) &&
                   IsNearlyZero(part.BurnTime) &&
                   IsNearlyZero(part.AdditionalPain) &&
                   IsNearlyZero(part.Stiffness) &&
                   IsNearlyZero(part.Wetness) &&
                   IsNearlyZero(part.AlcoholLevel) &&
                   IsNearlyZero(part.ComfreyFactor) &&
                   IsNearlyZero(part.GarlicFactor) &&
                   IsNearlyZero(part.PlantainFactor);
        }

        private static bool IsNearlyZero(float value)
            => Math.Abs(value) <= 0.0001f;

        private static bool IsPlausibleTemperature(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && value >= -60f && value <= 80f;

        private static bool IsQuietInfectionTimer(float value)
            => IsNearlyZero(value) || Math.Abs(value + 1f) <= 0.0001f;

        private static string FormatRegistryItemList(
            IReadOnlyList<ushort> registryIds,
            IReadOnlyList<string> fullTypes)
        {
            var values = new List<string>();
            for (int i = 0; i < registryIds.Count; i++)
            {
                if (fullTypes != null && i < fullTypes.Count && !string.IsNullOrWhiteSpace(fullTypes[i]))
                    values.Add(fullTypes[i]);
                else
                    values.Add("registry=" + registryIds[i].ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", values.Take(8)) +
                   (values.Count > 8 ? ", ..." : "");
        }

        private static string FormatAmmoState(InventoryItemPreview item)
        {
            if (item == null || !item.HasCurrentAmmoCount)
                return "";

            string count = item.CurrentAmmoCount.ToString(CultureInfo.InvariantCulture);
            if (TryGetWeaponMaxAmmo(item, out int maxAmmo) && maxAmmo > 0)
                return count + "/" + maxAmmo.ToString(CultureInfo.InvariantCulture);

            return count;
        }

        private static bool TryGetWeaponMaxAmmo(InventoryItemPreview item, out int maxAmmo)
        {
            maxAmmo = 0;
            if (item?.HandWeaponTail?.HasClipSize == true && item.HandWeaponTail.ClipSize > 0)
            {
                maxAmmo = item.HandWeaponTail.ClipSize;
                return true;
            }

            string fullType = FirstNonEmpty(item?.RegistryFullType, SelectBestItemHint(item?.StringHints));
            if (!string.IsNullOrWhiteSpace(fullType) && KnownWeaponMaxAmmo.TryGetValue(fullType, out maxAmmo))
                return true;

            return false;
        }

        private static string FormatInventoryItemCompactState(InventoryItemPreview item)
        {
            if (item == null)
                return "";

            var parts = new List<string>();

            if (item.HasCurrentAmmoCount)
                parts.Add("ammo=" + FormatAmmoState(item));
            else if (item.HandWeaponTail?.HasClipSize == true)
                parts.Add("clipSize=" + item.HandWeaponTail.ClipSize.ToString(CultureInfo.InvariantCulture));
            else if (item.HandWeaponTail != null && TryGetWeaponMaxAmmo(item, out int maxAmmo))
                parts.Add("maxAmmo=" + maxAmmo.ToString(CultureInfo.InvariantCulture));

            if (item.HandWeaponTail != null)
            {
                if (item.HandWeaponTail.ContainsClip)
                    parts.Add("containsClip=yes");
                if (item.HandWeaponTail.RoundChambered)
                    parts.Add("chambered=yes");
                if (item.HandWeaponTail.IsJammed)
                    parts.Add("jammed=yes");
                if (item.HandWeaponTail.HasAttachmentCount)
                    parts.Add("attachments=" + item.HandWeaponTail.AttachmentCount.ToString(CultureInfo.InvariantCulture));
            }

            if (item.HasAttachedSlot)
                parts.Add("slot=" + item.AttachedSlot.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(item.AttachedSlotType))
                parts.Add("slotType=" + item.AttachedSlotType);
            if (!string.IsNullOrWhiteSpace(item.AttachedToModel))
                parts.Add("model=" + item.AttachedToModel);

            if (item.ContainerTail?.NestedInventory != null)
                parts.Add("items=" + item.ContainerTail.NestedInventory.TotalItems.ToString(CultureInfo.InvariantCulture));

            string fluid = FormatFluidContainerCompact(item.EntityPayload?.FluidContainer);
            if (!string.IsNullOrEmpty(fluid))
                parts.Add(fluid);

            return string.Join(", ", parts);
        }

        private static string FormatHandWeaponTail(InventoryItemPreview item)
        {
            var tail = item.HandWeaponTail;
            if (tail == null)
                return "";

            var parts = new List<string>
            {
                $"handWeapon=0x{tail.Flags:X8}"
            };

            if (tail.HasClipSize)
                parts.Add("clipSize=" + tail.ClipSize.ToString(CultureInfo.InvariantCulture));
            if (TryGetWeaponMaxAmmo(item, out int maxAmmo) && (!tail.HasClipSize || tail.ClipSize != maxAmmo))
                parts.Add("maxAmmo=" + maxAmmo.ToString(CultureInfo.InvariantCulture));
            if (tail.ContainsClip)
                parts.Add("containsClip=yes");
            if (tail.RoundChambered)
                parts.Add("chambered=yes");
            if (tail.IsJammed)
                parts.Add("jammed=yes");
            if (tail.HasAttachmentCount)
                parts.Add("attachments=" + tail.AttachmentCount.ToString(CultureInfo.InvariantCulture));
            if (tail.HasMaxRange)
                parts.Add("maxRange=" + FormatSingle(tail.MaxRange));
            if (tail.HasMinRangeRanged)
                parts.Add("minRange=" + FormatSingle(tail.MinRangeRanged));
            if (tail.HasMinDamage && tail.HasMaxDamage)
                parts.Add("damage=" + FormatSingle(tail.MinDamage) + "-" + FormatSingle(tail.MaxDamage));
            if (tail.HasHitChance)
                parts.Add("hitChance=" + tail.HitChance.ToString(CultureInfo.InvariantCulture));

            return string.Join(" ", parts);
        }

        private static string FormatFluidContainerCompact(FluidContainerPreview fluidContainer)
        {
            if (fluidContainer == null || fluidContainer.Fluids.Count == 0)
                return "";

            float totalAmount = fluidContainer.Fluids.Sum(x => x.Amount);
            if (fluidContainer.Capacity > 0f)
                return "filled=" + FormatPercent(totalAmount / fluidContainer.Capacity);

            return "amount=" + FormatSingle(totalAmount);
        }

        private static string FormatInventoryContainerTail(InventoryContainerTailPreview tail)
        {
            var nested = tail.NestedInventory;
            var parts = new List<string>
            {
                "container=" + FirstNonEmpty(nested?.ContainerType, "<unknown>"),
                "containerId=" + tail.ContainerId.ToString(CultureInfo.InvariantCulture),
                "weightReduction=" + tail.WeightReduction.ToString(CultureInfo.InvariantCulture)
            };

            if (nested != null)
            {
                parts.Add("items=" + nested.TotalItems.ToString(CultureInfo.InvariantCulture));
                if (nested.Items.Count > 0)
                {
                    var contents = nested.Items
                        .Take(6)
                        .Select(item =>
                        {
                            string prefix = item.IdenticalCount > 1
                                ? "x" + item.IdenticalCount.ToString(CultureInfo.InvariantCulture) + " "
                                : "";
                            return prefix + FormatInventoryContainerContent(item);
                        });
                    parts.Add("contents=[" + string.Join("; ", contents) + "]");
                }
            }

            return string.Join(" ", parts);
        }

        private static string FormatInventoryContainerContent(InventoryItemPreview item)
        {
            string name = FormatInventoryItemName(item);
            string state = FormatInventoryItemCompactState(item);
            string stateSuffix = string.IsNullOrEmpty(state) ? "" : " (" + state + ")";
            if (item.KeyTail != null)
                return name + " (" + FormatKeyTail(item.KeyTail) + ")";
            if (item.HasCommonKeyId)
                return name + " (keyId=" + item.CommonKeyId.ToString(CultureInfo.InvariantCulture) + ")";
            if (item.ContainerTail != null)
                return name + " (" + FirstNonEmpty(item.ContainerTail.NestedInventory?.ContainerType, "container") +
                       (string.IsNullOrEmpty(state) ? "" : ", " + state) + ")";
            return name + stateSuffix;
        }

        private static string FormatKeyTail(KeyTailPreview tail)
            => $"keyId={tail.KeyId} keyCount={tail.NumberOfKey}";

        private static string FormatFluidContainer(FluidContainerPreview fluidContainer)
        {
            var parts = new List<string>
            {
                $"fluidContainer=0x{fluidContainer.Flags:X4}",
                "capacity=" + FormatSingle(fluidContainer.Capacity)
            };

            if (!string.IsNullOrWhiteSpace(fluidContainer.ContainerName))
                parts.Add("name=" + fluidContainer.ContainerName);

            if (fluidContainer.Fluids.Count == 0)
            {
                parts.Add("empty");
            }
            else
            {
                float totalAmount = fluidContainer.Fluids.Sum(x => x.Amount);
                string fluids = string.Join(", ", fluidContainer.Fluids.Take(4).Select(FormatFluidInstance));
                parts.Add("fluids=[" + fluids + "]");
                parts.Add("amount=" + FormatSingle(totalAmount));
                if (fluidContainer.Capacity > 0f)
                    parts.Add("filled=" + FormatPercent(totalAmount / fluidContainer.Capacity));
            }

            if (fluidContainer.CanPlayerEmpty)
                parts.Add("canEmpty=yes");
            if (fluidContainer.HiddenAmount)
                parts.Add("hiddenAmount=yes");
            if (fluidContainer.HasRainCatcher)
                parts.Add("rainCatcher=" + FormatSingle(fluidContainer.RainCatcher));

            return string.Join(" ", parts);
        }

        private static string FormatFluidInstance(FluidInstancePreview fluid)
            => FirstNonEmpty(fluid.FluidType, "fluidId=" + fluid.FluidTypeId.ToString(CultureInfo.InvariantCulture)) +
               ":" + FormatSingle(fluid.Amount);

        private static string FormatFluidType(int id)
        {
            switch (id)
            {
                case 1: return "Water";
                case 2: return "Petrol";
                case 3: return "RubbingAlcohol";
                case 4: return "TaintedWater";
                case 5: return "Beer";
                case 6: return "Whiskey";
                case 7: return "SodaPop";
                case 8: return "Coffee";
                case 9: return "Tea";
                case 10: return "Wine";
                case 11: return "Bleach";
                case 12: return "Blood";
                case 14: return "Honey";
                case 15: return "Mead";
                case 16: return "Acid";
                case 17: return "SpiffoJuice";
                case 18: return "SecretFlavoring";
                case 19: return "CarbonatedWater";
                case 20: return "CowMilk";
                case 21: return "SheepMilk";
                case 22: return "CleaningLiquid";
                case 23: return "AnimalBlood";
                case 24: return "AnimalGrease";
                case 64: return "Dye";
                case 65: return "HairDye";
                case 66: return "Paint";
                case 70: return "PoisonWeak";
                case 71: return "PoisonNormal";
                case 72: return "PoisonStrong";
                case 73: return "PoisonPotent";
                case 74: return "AnimalMilk";
                case 127: return "Modded";
                case -1: return "None";
                default: return "fluidId=" + id.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void AppendProtectionHints(
            StringBuilder output,
            IReadOnlyList<WornItemPreview> wornItems,
            IReadOnlyList<InventoryItemPreview> savedItems)
        {
            var lines = BuildProtectionLines(wornItems, savedItems);
            if (lines.Count == 0)
                return;

            output.AppendLine("  Protection hints (script-derived from worn items):");
            foreach (string line in lines.Take(24))
                output.AppendLine("    " + line);
        }

        private static List<string> BuildProtectionLines(
            IReadOnlyList<WornItemPreview> wornItems,
            IReadOnlyList<InventoryItemPreview> savedItems)
        {
            var protectionByPart = new Dictionary<string, List<ProtectionContributionPreview>>(StringComparer.OrdinalIgnoreCase);
            if (wornItems == null || savedItems == null)
                return new List<string>();

            foreach (var worn in wornItems)
            {
                if (worn.InventoryIndex < 0 || savedItems == null || worn.InventoryIndex >= savedItems.Count)
                    continue;

                var item = savedItems[worn.InventoryIndex];
                string identity = GetInventoryItemIdentity(item);
                string source = FormatInventoryItemName(item);

                foreach (var contribution in EnumerateKnownProtectionContributions(identity, source))
                    AddProtectionContribution(protectionByPart, contribution);

                if (item.ClothingTail != null)
                {
                    foreach (var patch in item.ClothingTail.Patches)
                    {
                        string partName = FormatBloodBodyPartName(patch.PartIndex);
                        string patchSource = source + " patch";
                        AddProtectionContribution(
                            protectionByPart,
                            new ProtectionContributionPreview(partName, patch.BiteDefense, patch.ScratchDefense, patchSource));
                    }
                }
            }

            if (protectionByPart.Count == 0)
                return new List<string>();

            return protectionByPart
                .OrderBy(x => BodyPartProtectionSortKey(x.Key))
                .Select(part => FormatProtectionLine(part.Key, part.Value))
                .ToList();
        }

        private static IEnumerable<ProtectionContributionPreview> EnumerateKnownProtectionContributions(string identity, string source)
        {
            if (identity.IndexOf("Gloves_LeatherGloves", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield return new ProtectionContributionPreview("Hand_L", 15, 30, source);
                yield return new ProtectionContributionPreview("Hand_R", 15, 30, source);
            }

            if (identity.IndexOf("Trousers_Fireman", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("Fireman", StringComparison.OrdinalIgnoreCase) >= 0 && identity.IndexOf("Trousers", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield return new ProtectionContributionPreview("Groin", 20, 30, source);
                yield return new ProtectionContributionPreview("UpperLeg_L", 20, 30, source);
                yield return new ProtectionContributionPreview("UpperLeg_R", 20, 30, source);
                yield return new ProtectionContributionPreview("LowerLeg_L", 20, 30, source);
                yield return new ProtectionContributionPreview("LowerLeg_R", 20, 30, source);
            }

            if (identity.IndexOf("ArmyBoots", StringComparison.OrdinalIgnoreCase) >= 0 ||
                identity.IndexOf("Shoes_ArmyBoots", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield return new ProtectionContributionPreview("Foot_L", 100, 100, source);
                yield return new ProtectionContributionPreview("Foot_R", 100, 100, source);
            }
        }

        private static void AddProtectionContribution(
            Dictionary<string, List<ProtectionContributionPreview>> protectionByPart,
            ProtectionContributionPreview contribution)
        {
            if (string.IsNullOrWhiteSpace(contribution.BodyPart) ||
                contribution.Bite <= 0 && contribution.Scratch <= 0)
            {
                return;
            }

            if (!protectionByPart.TryGetValue(contribution.BodyPart, out var values))
            {
                values = new List<ProtectionContributionPreview>();
                protectionByPart[contribution.BodyPart] = values;
            }

            values.Add(contribution);
        }

        private static string FormatProtectionLine(string bodyPart, IReadOnlyList<ProtectionContributionPreview> contributions)
        {
            float bite = Math.Min(100f, contributions.Sum(x => x.Bite));
            float scratch = Math.Min(100f, contributions.Sum(x => x.Scratch));
            string sources = string.Join(" + ", contributions.Select(x => x.Source).Distinct().Take(4));
            return $"{bodyPart}: bite={FormatSingle(bite)}%, scratch={FormatSingle(scratch)}% from {sources}";
        }

        private static int BodyPartProtectionSortKey(string bodyPart)
        {
            int index = Array.FindIndex(BloodBodyPartNames, x => x.Equals(bodyPart, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? 1000 : index;
        }

        private static string FormatBloodBodyPartName(int partIndex)
        {
            if (partIndex >= 0 && partIndex < BloodBodyPartNames.Length)
                return BloodBodyPartNames[partIndex];

            return "part=" + partIndex.ToString(CultureInfo.InvariantCulture);
        }

        private static string GetInventoryItemIdentity(InventoryItemPreview item)
        {
            var parts = new List<string>();
            if (item?.Visual != null)
            {
                parts.Add(item.Visual.FullType);
                parts.Add(item.Visual.ClothingItemName);
                parts.Add(item.Visual.AlternateModelName);
            }

            if (item != null)
                parts.AddRange(item.StringHints);

            return string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static bool IsKnownPerkName(string name)
        {
            switch (name)
            {
                case "Fitness":
                case "Strength":
                case "Axe":
                case "Sprinting":
                case "Lightfoot":
                case "Nimble":
                case "Sneak":
                case "Electricity":
                case "Woodwork":
                case "Cooking":
                case "Mechanics":
                case "Tailoring":
                    return true;
                default:
                    return false;
            }
        }

        private static string FormatLiteratureTail(LiteratureTailPreview tail)
        {
            var parts = new List<string>
            {
                $"literature=0x{tail.Flags:X4}"
            };

            if (tail.HasAlreadyReadPages && tail.HasNumberOfPages)
                parts.Add($"pages={tail.AlreadyReadPages}/{tail.NumberOfPages}");
            else if (tail.HasAlreadyReadPages)
                parts.Add("pagesRead=" + tail.AlreadyReadPages.ToString(CultureInfo.InvariantCulture));
            else if (tail.HasNumberOfPages)
                parts.Add("pages=" + tail.NumberOfPages.ToString(CultureInfo.InvariantCulture));

            if (tail.LearnedRecipes.Count > 0)
                parts.Add("recipes=" + string.Join(", ", tail.LearnedRecipes.Take(4)));

            return string.Join(" ", parts);
        }

        private static string FormatClothingTail(ClothingTailPreview tail)
        {
            var parts = new List<string>
            {
                $"clothingTail=0x{tail.Flags:X2}"
            };

            if (!string.IsNullOrWhiteSpace(tail.SpriteName))
                parts.Add("sprite=" + tail.SpriteName);
            if (tail.HasDirtyness)
                parts.Add("dirty=" + FormatSingle(tail.Dirtyness));
            if (tail.HasBloodLevel)
                parts.Add("bloodLevel=" + FormatSingle(tail.BloodLevel));
            if (tail.HasWetness)
                parts.Add("wetness=" + FormatSingle(tail.Wetness));
            if (tail.HasLastWetnessUpdate)
                parts.Add("lastWetnessUpdate=" + FormatSingle(tail.LastWetnessUpdate));
            if (tail.Patches.Count > 0)
                parts.Add("patches=" + string.Join("; ", tail.Patches.Take(6).Select(FormatClothingPatch)));
            if (tail.Patches.Count > 6)
                parts.Add("+" + (tail.Patches.Count - 6).ToString(CultureInfo.InvariantCulture) + " patches");

            return string.Join(" ", parts);
        }

        private static string FormatClothingPatch(ClothingPatchPreview patch)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "part {0} {1} tailor={2} scratch={3} bite={4} hole={5} gain={6}",
                patch.PartIndex,
                FormatFabricType(patch.FabricType),
                patch.TailorLevel,
                patch.ScratchDefense,
                patch.BiteDefense,
                FormatBool(patch.HasHole),
                patch.ConditionGain);
        }

        private static string FormatFabricType(int fabricType)
        {
            switch (fabricType)
            {
                case 1: return "Cotton";
                case 2: return "Denim";
                case 3: return "Leather";
                default: return "fabric=" + fabricType.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string FormatVisualArrays(ItemVisualPreview visual)
        {
            var parts = new List<string>();
            AddArrayPart(parts, "blood", visual.Blood, false);
            AddArrayPart(parts, "dirt", visual.Dirt, false);
            AddArrayPart(parts, "holes", visual.Holes, true);
            AddArrayPart(parts, "basicPatch", visual.BasicPatches, true);
            AddArrayPart(parts, "denimPatch", visual.DenimPatches, true);
            AddArrayPart(parts, "leatherPatch", visual.LeatherPatches, true);
            return string.Join(", ", parts);
        }

        private static void AddArrayPart(List<string> parts, string name, ByteArrayPreview preview, bool includeWhenZero)
        {
            if (preview == null || preview.Length == 0)
                return;

            if (!includeWhenZero && preview.NonZeroCount == 0)
                return;

            parts.Add($"{name}={preview.NonZeroCount}/{preview.Length} nz max={preview.MaxValue}");
        }

        private static string FormatArrayPreview(ByteArrayPreview preview)
        {
            if (preview == null)
                return "not decoded";

            return $"{preview.Length} bytes, {preview.NonZeroCount} non-zero, max={preview.MaxValue}";
        }

        private static string FormatItemHeaderFlags(byte header)
        {
            var flags = new List<string>();
            if ((header & 1) != 0) flags.Add("uses");
            if ((header & 4) != 0) flags.Add("condition");
            if ((header & 8) != 0) flags.Add("visual");
            if ((header & 16) != 0) flags.Add("customColor");
            if ((header & 32) != 0) flags.Add("itemCapacity");
            if ((header & 64) != 0) flags.Add("extra");
            return flags.Count == 0 ? "default" : string.Join(", ", flags);
        }

        private static string FormatItemNestedFlags(uint flags)
        {
            if (flags == 0)
                return "none";

            var names = new List<string>();
            AddFlagName(names, flags, 0x00000001, "modData");
            AddFlagName(names, flags, 0x00000002, "activated");
            AddFlagName(names, flags, 0x00000004, "repaired");
            AddFlagName(names, flags, 0x00000008, "displayName");
            AddFlagName(names, flags, 0x00000010, "byteData");
            AddFlagName(names, flags, 0x00000020, "extraItems");
            AddFlagName(names, flags, 0x00000040, "customName");
            AddFlagName(names, flags, 0x00000080, "customWeight");
            AddFlagName(names, flags, 0x00000100, "keyId");
            AddFlagName(names, flags, 0x00000400, "remoteControl");
            AddFlagName(names, flags, 0x00000800, "rgb");
            AddFlagName(names, flags, 0x00001000, "worker");
            AddFlagName(names, flags, 0x00002000, "wetCooldown");
            AddFlagName(names, flags, 0x00004000, "favorite");
            AddFlagName(names, flags, 0x00008000, "stashMap");
            AddFlagName(names, flags, 0x00010000, "infected");
            AddFlagName(names, flags, 0x00020000, "ammo");
            AddFlagName(names, flags, 0x00040000, "attachedSlot");
            AddFlagName(names, flags, 0x00080000, "attachedSlotType");
            AddFlagName(names, flags, 0x00100000, "attachedToModel");
            AddFlagName(names, flags, 0x00200000, "maxCapacity");
            AddFlagName(names, flags, 0x00400000, "recordedMedia");
            AddFlagName(names, flags, 0x01000000, "worldScale");
            AddFlagName(names, flags, 0x02000000, "initialised");
            AddFlagName(names, flags, 0x04000000, "entity");
            AddFlagName(names, flags, 0x08000000, "animalTracks");
            AddFlagName(names, flags, 0x10000000, "texture");
            AddFlagName(names, flags, 0x20000000, "modelIndex");
            AddFlagName(names, flags, 0x40000000, "worldRotation");
            return names.Count == 0 ? "unknown" : string.Join(", ", names);
        }

        private static void AddFlagName(List<string> names, uint flags, uint mask, string name)
        {
            if ((flags & mask) != 0)
                names.Add(name);
        }

        private static string FormatSaveType(byte saveType)
            => saveType == 0xFF ? "-1" : saveType.ToString(CultureInfo.InvariantCulture);

        private static string FormatBool(bool value)
            => value ? "yes" : "no";

        private static string FormatStringHints(IReadOnlyList<string> hints)
        {
            if (hints == null || hints.Count == 0)
                return "<no strings>";

            return string.Join(", ", hints.Distinct().Take(4));
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return "";
        }

        private static string GetRowText(IReadOnlyDictionary<string, object> rowValues, string key, string fallback)
        {
            if (rowValues == null ||
                !rowValues.TryGetValue(key, out object value) ||
                value == null ||
                value == DBNull.Value)
            {
                return fallback;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private static string FormatGender(int gender)
            => gender == 1 ? "Female" : "Male";

        private static string FormatProfession(string profession)
        {
            if (string.IsNullOrWhiteSpace(profession))
                return "unknown";

            return ProfessionNames.TryGetValue(profession, out string name) ? name : profession;
        }

        private static string CompareRowSingle(IReadOnlyDictionary<string, object> rowValues, string key, float blobValue)
        {
            if (!TryGetDouble(rowValues, key, out double rowValue))
                return "no row value";

            double delta = Math.Abs(rowValue - blobValue);
            return delta <= 0.005
                ? $"matches row {key}"
                : $"row {key}={FormatDouble(rowValue)}, delta={FormatDouble(delta)}";
        }

        private static string NoteFloatMatch(IReadOnlyDictionary<string, object> rowValues, float value)
        {
            if (rowValues == null)
                return "";

            foreach (string key in new[] { "x", "y", "z" })
            {
                if (!TryGetDouble(rowValues, key, out double rowValue))
                    continue;

                if (Math.Abs(rowValue - value) <= 0.005)
                    return "matches row " + key;
            }

            return "";
        }

        private static string KnownFloatName(int offset)
        {
            switch (offset)
            {
                case 0x02: return "offsetX";
                case 0x06: return "offsetY";
                case 0x0A: return "x";
                case 0x0E: return "y";
                case 0x12: return "z";
                default: return "";
            }
        }

        private static IEnumerable<int> FindFloatOffsets(byte[] data, float target, float tolerance, int maxResults)
        {
            if (float.IsNaN(target) || float.IsInfinity(target))
                yield break;

            int count = 0;
            for (int offset = 0; offset + 4 <= data.Length; offset++)
            {
                float value = ReadSingleBE(data, offset);
                if (float.IsNaN(value) || float.IsInfinity(value))
                    continue;

                if (Math.Abs(value - target) > tolerance)
                    continue;

                yield return offset;
                count++;
                if (count >= maxResults)
                    yield break;
            }
        }

        private static IEnumerable<int> FindIntOffsets(byte[] data, int target, int maxResults)
        {
            int count = 0;
            for (int offset = 0; offset + 4 <= data.Length; offset++)
            {
                if (ReadInt32BE(data, offset) != target)
                    continue;

                yield return offset;
                count++;
                if (count >= maxResults)
                    yield break;
            }
        }

        private static string FormatOffsets(IReadOnlyList<int> offsets)
        {
            if (offsets == null || offsets.Count == 0)
                return "no match";

            return string.Join(", ", offsets.Select(FormatOffset));
        }

        private static bool IsInterestingFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;

            double abs = Math.Abs(value);
            if (abs < 0.0001)
                return false;

            return abs < 10000000.0;
        }

        private static string FormatDirection(int direction)
        {
            string name = DirectionNames[direction & 7];
            return name;
        }

        private static string FormatOffset(int offset)
            => "0x" + offset.ToString("X4", CultureInfo.InvariantCulture);

        private static string FormatByte(byte[] data, int offset)
        {
            byte value = ReadByte(data, offset);
            return $"{value} (0x{value:X2})";
        }

        private static string FormatSingle(float value)
            => value.ToString("G9", CultureInfo.InvariantCulture);

        private static string FormatDouble(double value)
            => value.ToString("G9", CultureInfo.InvariantCulture);

        private static string FormatPercent(float value)
            => (value * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "%";

        private static string FormatObject(object value)
        {
            if (value == null || value == DBNull.Value)
                return "<null>";

            if (value is byte[] bytes)
                return $"[BLOB {bytes.Length:N0} bytes]";

            if (value is float floatValue)
                return FormatSingle(floatValue);

            if (value is double doubleValue)
                return FormatDouble(doubleValue);

            if (value is decimal decimalValue)
                return decimalValue.ToString(CultureInfo.InvariantCulture);

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString();
        }

        private static bool TryGetDouble(IReadOnlyDictionary<string, object> rowValues, string key, out double value)
        {
            value = 0;
            if (rowValues == null || !rowValues.TryGetValue(key, out object raw) || raw == null || raw == DBNull.Value)
                return false;

            try
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetInt(IReadOnlyDictionary<string, object> rowValues, string key, out int value)
        {
            value = 0;
            if (rowValues == null || !rowValues.TryGetValue(key, out object raw) || raw == null || raw == DBNull.Value)
                return false;

            try
            {
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetBool(IReadOnlyDictionary<string, object> rowValues, string key, out bool value)
        {
            value = false;
            if (rowValues == null || !rowValues.TryGetValue(key, out object raw) || raw == null || raw == DBNull.Value)
                return false;

            try
            {
                value = Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                if (raw is string text)
                {
                    if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "1", StringComparison.OrdinalIgnoreCase))
                    {
                        value = true;
                        return true;
                    }

                    if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        value = false;
                        return true;
                    }
                }

                return false;
            }
        }

        private static ushort ReadUInt16BE(byte[] data, int offset)
        {
            if (offset + 2 > data.Length) return 0;
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static short ReadInt16BE(byte[] data, int offset)
            => unchecked((short)ReadUInt16BE(data, offset));

        private static byte ReadByte(byte[] data, int offset)
            => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

        private static int ReadInt32BE(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (data[offset] << 24) |
                   (data[offset + 1] << 16) |
                   (data[offset + 2] << 8) |
                   data[offset + 3];
        }

        private static uint ReadUInt32BE(byte[] data, int offset)
            => unchecked((uint)ReadInt32BE(data, offset));

        private static float ReadSingleBE(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0f;
            var bytes = new[] { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(bytes, 0);
        }

        private static double ReadDoubleBE(byte[] data, int offset)
        {
            if (offset + 8 > data.Length) return 0d;
            var bytes = new[]
            {
                data[offset + 7],
                data[offset + 6],
                data[offset + 5],
                data[offset + 4],
                data[offset + 3],
                data[offset + 2],
                data[offset + 1],
                data[offset]
            };
            return BitConverter.ToDouble(bytes, 0);
        }

        private static bool TryReadPzUtfString(byte[] data, int offset, out string value, out int nextOffset)
        {
            value = "";
            nextOffset = offset;

            if (offset + 2 > data.Length)
                return false;

            int len = (data[offset] << 8) | data[offset + 1];
            if (len < 0 || offset + 2 + len > data.Length)
                return false;

            if (len == 0)
            {
                nextOffset = offset + 2;
                return true;
            }

            try
            {
                value = Encoding.UTF8.GetString(data, offset + 2, len);
            }
            catch
            {
                return false;
            }

            if (!IsUsefulString(value))
                return false;

            nextOffset = offset + 2 + len;
            return true;
        }

        private static IEnumerable<PzStringHit> FindPzUtfStrings(byte[] data)
            => FindPzUtfStrings(data, 0, data.Length);

        private static IEnumerable<PzStringHit> FindPzUtfStrings(byte[] data, int start, int end)
        {
            int scanStart = Math.Max(0, start);
            int scanEnd = Math.Min(data.Length, end);
            for (int offset = scanStart; offset + 3 < scanEnd; offset++)
            {
                if (!TryReadPzUtfString(data, offset, out string value, out int nextOffset))
                    continue;

                if (nextOffset > scanEnd)
                    continue;

                int len = nextOffset - offset - 2;
                if (len < 3 || len > 180)
                    continue;

                yield return new PzStringHit(offset, len, value);
            }
        }

        private static bool IsUsefulString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            int printable = 0;
            foreach (char c in value)
            {
                if (c >= 32 && c < 127)
                    printable++;
                else if (c == '\t')
                    printable++;
            }

            if (printable < value.Length)
                return false;

            return value.Any(char.IsLetter) &&
                   value.IndexOf('\0') < 0 &&
                   !value.Contains("???");
        }

        private static bool TryReadBooleanByte(byte[] data, ref int offset, out bool value)
        {
            value = false;
            if (offset < 0 || offset >= data.Length)
                return false;

            byte raw = data[offset++];
            if (raw > 1)
                return false;

            value = raw != 0;
            return true;
        }

        private static bool TrySkip(byte[] data, ref int offset, int count)
        {
            if (count < 0 || offset < 0 || offset + count > data.Length)
                return false;

            offset += count;
            return true;
        }

        private static bool TrySkipWithin(byte[] data, ref int offset, int count, int endOffset)
        {
            if (count < 0 || offset < 0 || offset + count > data.Length || offset + count > endOffset)
                return false;

            offset += count;
            return true;
        }

        private static string BuildHexDump(byte[] data, int start, int count)
        {
            var output = new StringBuilder();
            int end = Math.Min(data.Length, start + count);

            for (int offset = start; offset < end; offset += 16)
            {
                int lineCount = Math.Min(16, end - offset);
                output.Append(offset.ToString("X6", CultureInfo.InvariantCulture));
                output.Append("  ");

                for (int i = 0; i < 16; i++)
                {
                    if (i < lineCount)
                        output.Append(data[offset + i].ToString("X2", CultureInfo.InvariantCulture));
                    else
                        output.Append("  ");

                    output.Append(i == 7 ? "  " : " ");
                }

                output.Append(" ");
                for (int i = 0; i < lineCount; i++)
                {
                    byte b = data[offset + i];
                    output.Append(b >= 32 && b < 127 ? (char)b : '.');
                }

                output.AppendLine();
            }

            return output.ToString();
        }

        private readonly struct NumericHit
        {
            public NumericHit(int offset, float value, string note)
            {
                Offset = offset;
                Value = value;
                Note = note;
            }

            public int Offset { get; }
            public float Value { get; }
            public string Note { get; }
        }

        public sealed class CharacterEditorSnapshot
        {
            public bool Supported { get; set; }
            public string Source { get; set; }
            public string Note { get; set; }
            public int BlobLength { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string FullNameFallback { get; set; }
            public string Gender { get; set; }
            public string Model { get; set; }
            public string ProfessionId { get; set; }
            public string ProfessionName { get; set; }
            public string Hair { get; set; }
            public string Beard { get; set; }
            public bool HasPosition { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public string Direction { get; set; }
            public int WorldVersion { get; set; }
            public bool HasDeathFlag { get; set; }
            public bool IsDead { get; set; }
            public bool HasCharacterTail { get; set; }
            public bool OnFire { get; set; }
            public bool Sneaking { get; set; }
            public bool DeathDragDown { get; set; }
            public bool HasInventory { get; set; }
            public int InventoryGroups { get; set; }
            public int InventoryItems { get; set; }
            public bool HasStats { get; set; }
            public Dictionary<string, float> Stats { get; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public bool HasIsoPlayer { get; set; }
            public double HoursSurvived { get; set; }
            public int ZombieKills { get; set; }
            public int SurvivorKills { get; set; }
            public string PrimaryHand { get; set; }
            public string SecondaryHand { get; set; }
            public bool HasNutrition { get; set; }
            public float Weight { get; set; }
            public float Calories { get; set; }
            public float Proteins { get; set; }
            public float Lipids { get; set; }
            public float Carbohydrates { get; set; }
            public bool HasXp { get; set; }
            public List<string> Traits { get; } = new List<string>();
            public Dictionary<string, int> PerkLevels { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, float> XpEntries { get; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public List<string> ReadBooks { get; } = new List<string>();
            public bool HasBodyDamage { get; set; }
            public List<CharacterBodyPartSnapshot> BodyParts { get; } = new List<CharacterBodyPartSnapshot>();
            public bool HasThermoregulator { get; set; }
            public float BodySetPoint { get; set; }
        }

        public sealed class CharacterBodyPartSnapshot
        {
            public string Name { get; set; }
            public float Health { get; set; }
            public string Flags { get; set; }
            public float Pain { get; set; }
            public float Stiffness { get; set; }
            public float Wetness { get; set; }
            public bool HasTemperature { get; set; }
            public float CoreTemperature { get; set; }
            public float SkinTemperature { get; set; }
        }

        private sealed class ByteArrayPreview
        {
            public ByteArrayPreview(int offset, int length, int nonZeroCount, int maxValue)
            {
                Offset = offset;
                Length = length;
                NonZeroCount = nonZeroCount;
                MaxValue = maxValue;
            }

            public int Offset { get; }
            public int Length { get; }
            public int NonZeroCount { get; }
            public int MaxValue { get; }
        }

        private sealed class ItemVisualPreview
        {
            public bool Success { get; set; }
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public byte Flags1 { get; set; }
            public int FullTypeOffset { get; set; }
            public string FullType { get; set; }
            public int AlternateModelOffset { get; set; }
            public string AlternateModelName { get; set; }
            public int ClothingItemOffset { get; set; }
            public string ClothingItemName { get; set; }
            public string Decal { get; set; }
            public ByteArrayPreview Blood { get; set; }
            public ByteArrayPreview Dirt { get; set; }
            public ByteArrayPreview Holes { get; set; }
            public ByteArrayPreview BasicPatches { get; set; }
            public ByteArrayPreview DenimPatches { get; set; }
            public ByteArrayPreview LeatherPatches { get; set; }
        }

        private sealed class HumanVisualPreview
        {
            public bool Success { get; set; }
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public byte Flags1 { get; set; }
            public byte Flags2 { get; set; }
            public int BodyHair { get; set; }
            public int SkinTexture { get; set; }
            public int ZombieRotStage { get; set; }
            public string SkinTextureName { get; set; }
            public string BeardModel { get; set; }
            public string HairModel { get; set; }
            public string NonAttachedHair { get; set; }
            public ByteArrayPreview Blood { get; set; }
            public ByteArrayPreview Dirt { get; set; }
            public ByteArrayPreview Holes { get; set; }
            public List<ItemVisualPreview> BodyVisuals { get; } = new List<ItemVisualPreview>();
        }

        private sealed class InventoryPreview
        {
            public bool Success { get; set; }
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public string ContainerType { get; set; }
            public bool Explored { get; set; }
            public int GroupCountOffset { get; set; }
            public int TotalItems { get; set; }
            public int HasBeenLootedOffset { get; set; }
            public bool HasBeenLooted { get; set; }
            public int CapacityOffset { get; set; }
            public int Capacity { get; set; }
            public List<InventoryItemPreview> Items { get; } = new List<InventoryItemPreview>();
        }

        private sealed class InventoryItemPreview
        {
            public int GroupOffset { get; set; }
            public int IdenticalCount { get; set; }
            public int DataLengthOffset { get; set; }
            public int DataLength { get; set; }
            public int ItemStartOffset { get; set; }
            public int ItemEndOffset { get; set; }
            public ushort RegistryId { get; set; }
            public byte SaveType { get; set; }
            public int ItemId { get; set; }
            public int HeaderOffset { get; set; }
            public byte Header { get; set; }
            public bool HasCurrentUses { get; set; }
            public int CurrentUsesOffset { get; set; }
            public int CurrentUses { get; set; }
            public bool HasCondition { get; set; }
            public int ConditionOffset { get; set; }
            public int Condition { get; set; }
            public bool HasItemCapacity { get; set; }
            public int ItemCapacityOffset { get; set; }
            public float ItemCapacity { get; set; }
            public bool HasNestedFlags { get; set; }
            public int NestedFlagsOffset { get; set; }
            public uint NestedFlags { get; set; }
            public int BaseParsedEndOffset { get; set; }
            public int ModDataOffset { get; set; }
            public int ModDataEntryCount { get; set; }
            public bool HasCommonKeyId { get; set; }
            public int CommonKeyIdOffset { get; set; }
            public int CommonKeyId { get; set; }
            public int SubtypeOffset { get; set; }
            public int SubtypeBytes { get; set; }
            public ItemVisualPreview Visual { get; set; }
            public EntityPayloadPreview EntityPayload { get; set; }
            public InventoryContainerTailPreview ContainerTail { get; set; }
            public ClothingTailPreview ClothingTail { get; set; }
            public LiteratureTailPreview LiteratureTail { get; set; }
            public KeyTailPreview KeyTail { get; set; }
            public HandWeaponTailPreview HandWeaponTail { get; set; }
            public string ParseNote { get; set; }
            public List<int> DuplicateIds { get; } = new List<int>();
            public List<string> StringHints { get; } = new List<string>();
            public string RegistryFullType { get; set; }
            public int ExtraItemsOffset { get; set; }
            public List<ushort> ExtraItemRegistryIds { get; } = new List<ushort>();
            public List<string> ExtraItemFullTypes { get; } = new List<string>();
            public bool HasCurrentAmmoCount { get; set; }
            public int CurrentAmmoCountOffset { get; set; }
            public int CurrentAmmoCount { get; set; }
            public bool HasAttachedSlot { get; set; }
            public int AttachedSlotOffset { get; set; }
            public int AttachedSlot { get; set; }
            public int AttachedSlotTypeOffset { get; set; }
            public string AttachedSlotType { get; set; }
            public int AttachedToModelOffset { get; set; }
            public string AttachedToModel { get; set; }
            public bool HasMaxCapacity { get; set; }
            public int MaxCapacityOffset { get; set; }
            public int MaxCapacity { get; set; }
        }

        private sealed class CharacterStatsPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public bool Asleep { get; set; }
            public float ForceWakeUpTime { get; set; }
            public int StatsOffset { get; set; }
            public List<NamedFloat> Stats { get; } = new List<NamedFloat>();
        }

        private sealed class XpPreview
        {
            public bool Success { get; set; }
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public float TotalXp { get; set; }
            public int Level { get; set; }
            public int LastLevel { get; set; }
            public List<string> Traits { get; } = new List<string>();
            public List<NamedFloat> XpEntries { get; } = new List<NamedFloat>();
            public List<NamedInt> PerkLevels { get; } = new List<NamedInt>();
            public List<XpMultiplierPreview> XpMultipliers { get; } = new List<XpMultiplierPreview>();
        }

        private sealed class XpMultiplierPreview
        {
            public XpMultiplierPreview(string perkName, float multiplier, int minLevel, int maxLevel)
            {
                PerkName = perkName;
                Multiplier = multiplier;
                MinLevel = minLevel;
                MaxLevel = maxLevel;
            }

            public string PerkName { get; }
            public float Multiplier { get; }
            public int MinLevel { get; }
            public int MaxLevel { get; }
        }

        private sealed class IsoGameCharacterTailPreview
        {
            public bool Success { get; set; }
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public int LeftHandInventoryIndex { get; set; }
            public int RightHandInventoryIndex { get; set; }
            public bool OnFire { get; set; }
            public float DepressEffect { get; set; }
            public float DepressFirstTakeTime { get; set; }
            public float BetaEffect { get; set; }
            public float BetaDelta { get; set; }
            public float PainEffect { get; set; }
            public float PainDelta { get; set; }
            public float SleepingTabletEffect { get; set; }
            public float SleepingTabletDelta { get; set; }
            public int ReadBookCount { get; set; }
            public float ReduceInfectionPower { get; set; }
            public int KnownRecipeCount { get; set; }
            public int LastHourSleeped { get; set; }
            public float TimeSinceLastSmoke { get; set; }
            public float BeardGrowTiming { get; set; }
            public float HairGrowTiming { get; set; }
            public bool UnlimitedCarry { get; set; }
            public bool BuildCheat { get; set; }
            public bool HealthCheat { get; set; }
            public bool MechanicsCheat { get; set; }
            public bool MovablesCheat { get; set; }
            public bool FarmingCheat { get; set; }
            public bool FishingCheat { get; set; }
            public bool CanUseBrushTool { get; set; }
            public bool FastMoveCheat { get; set; }
            public bool TimedActionInstantCheat { get; set; }
            public bool UnlimitedEndurance { get; set; }
            public bool UnlimitedAmmo { get; set; }
            public bool KnowAllRecipes { get; set; }
            public bool Sneaking { get; set; }
            public bool DeathDragDown { get; set; }
        }

        private sealed class IsoPlayerPreview
        {
            public bool Success { get; set; }
            public int StartOffset { get; set; }
            public int WornCountOffset { get; set; }
            public int NextOffset { get; set; }
            public double HoursSurvived { get; set; }
            public int ZombieKills { get; set; }
            public int PrimaryHandIndex { get; set; }
            public int SecondaryHandIndex { get; set; }
            public int SurvivorKills { get; set; }
            public NutritionPreview Nutrition { get; set; }
            public List<WornItemPreview> WornItems { get; } = new List<WornItemPreview>();
        }

        private sealed class NutritionPreview
        {
            public float Calories { get; set; }
            public float Proteins { get; set; }
            public float Lipids { get; set; }
            public float Carbohydrates { get; set; }
            public float Weight { get; set; }
        }

        private sealed class BodyDamagePreview
        {
            public bool Success { get; set; }
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public float CatchCold { get; set; }
            public bool HasCold { get; set; }
            public float ColdStrength { get; set; }
            public int TimeToSneezeOrCough { get; set; }
            public bool ReduceFakeInfection { get; set; }
            public float HealthFromFoodTimer { get; set; }
            public float PainReduction { get; set; }
            public float ColdReduction { get; set; }
            public float InfectionTime { get; set; }
            public float InfectionMortalityDuration { get; set; }
            public float ColdDamageStage { get; set; }
            public bool HasThermoregulator { get; set; }
            public ThermoregulatorPreview Thermoregulator { get; set; }
            public string ThermoregulatorNote { get; set; }
            public List<BodyPartDamagePreview> Parts { get; } = new List<BodyPartDamagePreview>();
        }

        private sealed class ThermoregulatorPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public float SetPoint { get; set; }
            public float MetabolicRate { get; set; }
            public float MetabolicRateReal { get; set; }
            public float MetabolicTarget { get; set; }
            public float BodyHeatDelta { get; set; }
            public float CoreHeatDelta { get; set; }
            public float ThermalDamage { get; set; }
            public float DamageCounter { get; set; }
            public List<ThermalNodePreview> Nodes { get; } = new List<ThermalNodePreview>();
        }

        private sealed class ThermalNodePreview
        {
            public int BodyPartIndex { get; set; }
            public string BodyPartName { get; set; }
            public float Celsius { get; set; }
            public float SkinCelsius { get; set; }
            public float HeatDelta { get; set; }
            public float PrimaryDelta { get; set; }
            public float SecondaryDelta { get; set; }
            public float Insulation { get; set; }
            public float WindResist { get; set; }
            public float BodyWetness { get; set; }
            public float ClothingWetness { get; set; }
        }

        private sealed class BodyPartDamagePreview
        {
            public string Name { get; set; }
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public bool Cut { get; set; }
            public bool Bitten { get; set; }
            public bool Scratched { get; set; }
            public bool Bandaged { get; set; }
            public bool Bleeding { get; set; }
            public bool DeepWounded { get; set; }
            public bool FakeInfected { get; set; }
            public bool Infected { get; set; }
            public float Health { get; set; }
            public bool InfectedWound { get; set; }
            public float CutTime { get; set; }
            public float BiteTime { get; set; }
            public float ScratchTime { get; set; }
            public float BleedingTime { get; set; }
            public float AlcoholLevel { get; set; }
            public float AdditionalPain { get; set; }
            public float DeepWoundTime { get; set; }
            public bool HaveGlass { get; set; }
            public bool GetBandageXp { get; set; }
            public bool Stitched { get; set; }
            public float StitchTime { get; set; }
            public bool GetStitchXp { get; set; }
            public bool GetSplintXp { get; set; }
            public float FractureTime { get; set; }
            public bool Splint { get; set; }
            public bool HaveBullet { get; set; }
            public float BurnTime { get; set; }
            public bool NeedBurnWash { get; set; }
            public float LastTimeBurnWash { get; set; }
            public string SplintItem { get; set; }
            public string BandageType { get; set; }
            public float Wetness { get; set; }
            public float Stiffness { get; set; }
            public float ComfreyFactor { get; set; }
            public float GarlicFactor { get; set; }
            public float PlantainFactor { get; set; }
        }

        private readonly struct WornItemPreview
        {
            public WornItemPreview(string location, int inventoryIndex)
            {
                Location = location;
                InventoryIndex = inventoryIndex;
            }

            public string Location { get; }
            public int InventoryIndex { get; }
        }

        private readonly struct ReadBookPreview
        {
            public ReadBookPreview(int offset, string fullType, int alreadyReadPages)
            {
                Offset = offset;
                FullType = fullType;
                AlreadyReadPages = alreadyReadPages;
            }

            public int Offset { get; }
            public string FullType { get; }
            public int AlreadyReadPages { get; }
        }

        private readonly struct NamedFloat
        {
            public NamedFloat(string name, float value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }
            public float Value { get; }
        }

        private readonly struct NamedInt
        {
            public NamedInt(string name, int value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }
            public int Value { get; }
        }

        private readonly struct ProtectionContributionPreview
        {
            public ProtectionContributionPreview(string bodyPart, float bite, float scratch, string source)
            {
                BodyPart = bodyPart;
                Bite = bite;
                Scratch = scratch;
                Source = source;
            }

            public string BodyPart { get; }
            public float Bite { get; }
            public float Scratch { get; }
            public string Source { get; }
        }

        private sealed class ClothingTailPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public byte Flags { get; set; }
            public int SpriteNameOffset { get; set; }
            public string SpriteName { get; set; }
            public bool HasDirtyness { get; set; }
            public float Dirtyness { get; set; }
            public bool HasBloodLevel { get; set; }
            public float BloodLevel { get; set; }
            public bool HasWetness { get; set; }
            public float Wetness { get; set; }
            public bool HasLastWetnessUpdate { get; set; }
            public float LastWetnessUpdate { get; set; }
            public List<ClothingPatchPreview> Patches { get; } = new List<ClothingPatchPreview>();
        }

        private sealed class ClothingPatchPreview
        {
            public int PartIndex { get; set; }
            public int TailorLevel { get; set; }
            public int FabricType { get; set; }
            public int ScratchDefense { get; set; }
            public int BiteDefense { get; set; }
            public bool HasHole { get; set; }
            public int ConditionGain { get; set; }
        }

        private sealed class LiteratureTailPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public ushort Flags { get; set; }
            public bool HasNumberOfPages { get; set; }
            public int NumberOfPages { get; set; }
            public bool HasAlreadyReadPages { get; set; }
            public int AlreadyReadPages { get; set; }
            public List<string> LearnedRecipes { get; } = new List<string>();
        }

        private sealed class InventoryContainerTailPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public int ContainerId { get; set; }
            public int WeightReduction { get; set; }
            public InventoryPreview NestedInventory { get; set; }
        }

        private sealed class KeyTailPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public int KeyId { get; set; }
            public int NumberOfKey { get; set; }
        }

        private sealed class HandWeaponTailPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public uint Flags { get; set; }
            public bool HasMaxRange { get; set; }
            public float MaxRange { get; set; }
            public bool HasMinRangeRanged { get; set; }
            public float MinRangeRanged { get; set; }
            public bool HasClipSize { get; set; }
            public int ClipSize { get; set; }
            public bool HasMinDamage { get; set; }
            public float MinDamage { get; set; }
            public bool HasMaxDamage { get; set; }
            public float MaxDamage { get; set; }
            public bool HasRecoilDelay { get; set; }
            public int RecoilDelay { get; set; }
            public bool HasAimingTime { get; set; }
            public int AimingTime { get; set; }
            public bool HasReloadTime { get; set; }
            public int ReloadTime { get; set; }
            public bool HasHitChance { get; set; }
            public int HitChance { get; set; }
            public bool HasMinAngle { get; set; }
            public float MinAngle { get; set; }
            public bool HasAttachmentCount { get; set; }
            public int AttachmentCount { get; set; }
            public bool ContainsClip { get; set; }
            public bool RoundChambered { get; set; }
            public bool IsJammed { get; set; }
        }

        private sealed class EntityPayloadPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public int ComponentCount { get; set; }
            public FluidContainerPreview FluidContainer { get; set; }
            public List<short> ComponentIds { get; } = new List<short>();
        }

        private sealed class FluidContainerPreview
        {
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public ushort Flags { get; set; }
            public float Capacity { get; set; }
            public string ContainerName { get; set; }
            public bool InputLocked { get; set; }
            public bool CanPlayerEmpty { get; set; }
            public bool HiddenAmount { get; set; }
            public bool HasRainCatcher { get; set; }
            public float RainCatcher { get; set; }
            public List<FluidInstancePreview> Fluids { get; } = new List<FluidInstancePreview>();
        }

        private sealed class FluidInstancePreview
        {
            public byte Flags { get; set; }
            public int FluidTypeId { get; set; }
            public string FluidType { get; set; }
            public float Amount { get; set; }
        }

        private sealed class KahluaTablePreview
        {
            public bool Success { get; set; }
            public int StartOffset { get; set; }
            public int NextOffset { get; set; }
            public int Count { get; set; }
            public string Error { get; set; }
            public List<KahluaEntryPreview> Entries { get; } = new List<KahluaEntryPreview>();
        }

        private readonly struct KahluaEntryPreview
        {
            public KahluaEntryPreview(int offset, string key, string value)
            {
                Offset = offset;
                Key = key;
                Value = value;
            }

            public int Offset { get; }
            public string Key { get; }
            public string Value { get; }
        }

        private sealed class PlayerDescriptorPreview
        {
            public bool Success { get; set; }
            public bool HasDescriptor { get; set; }
            public int DescriptorFlagOffset { get; set; }
            public int IdOffset { get; set; }
            public int Id { get; set; }
            public int ForenameOffset { get; set; }
            public string Forename { get; set; }
            public int SurnameOffset { get; set; }
            public string Surname { get; set; }
            public int TorsoOffset { get; set; }
            public string Torso { get; set; }
            public int GenderOffset { get; set; }
            public int Gender { get; set; }
            public int ProfessionOffset { get; set; }
            public string Profession { get; set; }
            public int ExtraFlagOffset { get; set; }
            public int ExtraFlag { get; set; }
            public List<string> Extra { get; } = new List<string>();
            public int XpBoostCountOffset { get; set; }
            public List<PerkBoostPreview> XpBoosts { get; } = new List<PerkBoostPreview>();
            public int VoicePrefixOffset { get; set; }
            public string VoicePrefix { get; set; }
            public int VoicePitchOffset { get; set; }
            public float VoicePitch { get; set; }
            public int VoiceTypeOffset { get; set; }
            public int VoiceType { get; set; }
            public int NextOffset { get; set; }
        }

        private readonly struct PerkBoostPreview
        {
            public PerkBoostPreview(int offset, string name, int level)
            {
                Offset = offset;
                Name = name;
                Level = level;
            }

            public int Offset { get; }
            public string Name { get; }
            public int Level { get; }
        }

        private readonly struct PzStringHit
        {
            public PzStringHit(int offset, int byteLength, string value)
            {
                Offset = offset;
                ByteLength = byteLength;
                Value = value;
            }

            public int Offset { get; }
            public int ByteLength { get; }
            public string Value { get; }
        }
    }
}


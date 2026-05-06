using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using LocalizationManager;
using ServerSync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace IceCaveSkills
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class IceCaveSkillsPlugin : BaseUnityPlugin
    {
        internal const string ModName = "IceCaveSkills";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "WackyMole";
        private const string ModGUID = Author + "." + ModName;
        private const string DiscoveryKeyPrefix = "IceCaveSkills_Mural_";
        private const float MaxSkillLevel = 100f;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        private static readonly int ClaimedMessageCooldownHash = "IceCaveSkillsLastClaimedMessage".GetStableHashCode();
        private static int _lastHoverObjectInstanceId;
        private static string? _lastHoveredDiscoveryKey;
        private static readonly string[] SearchableFieldNames = { "m_name", "m_locationName", "m_topic", "m_hoverName", "m_pinName", "m_text" };
        private static readonly Dictionary<int, MuralDetectionCache> MuralDetectionCacheByComponent = new();
        private static readonly string[] FrostCaveTerms =
        {
            "frostcave", "frost_cave", "mountaincave", "mountain_cave", "hildir_cave", "hildircave",
            "dg_cave", "iceendcap", "ice_endcap", "cave_new_ice"
        };

        private static readonly string[] MuralTerms =
        {
            "mural", "vegvisir", "rune", "runestone", "cavepainting", "cave_painting"
        };

        private static readonly Skills.SkillType[] RewardableSkillTypes =
            Enum.GetValues(typeof(Skills.SkillType))
                .Cast<Skills.SkillType>()
                .Where(skillType => skillType is not Skills.SkillType.None and not Skills.SkillType.All)
                .ToArray();

        public static readonly ManualLogSource PieceManagerModTemplateLogger =
            BepInEx.Logging.Logger.CreateLogSource(ModName);

        private static readonly ConfigSync ConfigSync = new(ModGUID)
        { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        public enum Toggle
        {
            On = 1,
            Off = 0
        }

        public enum RewardAmountMode
        {
            Flat = 0,
            PercentageOfMax = 1
        }

        public void Awake()
        {
            Localizer.Load();
            RegisterFallbackTranslations();

            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

            _enableCavePaintingRewards = config("2 - Rewards", "Enable Cave Painting Rewards", Toggle.On,
                "If on, Frost Cave cave paintings can award skills.");
            _rewardAmountMode = config("2 - Rewards", "Skill Reward Mode", RewardAmountMode.Flat,
                "Choose whether the configured reward amount is granted as flat skill levels or as a percentage of the max skill level.");
            _rewardAmount = config("2 - Rewards", "Skill Reward Amount", 10f,
                new ConfigDescription(
                    "Amount awarded by each Frost Cave cave painting. In PercentageOfMax mode, this is treated as a percent of the max skill level.",
                    new AcceptableValueRange<float>(0f, MaxSkillLevel)));

            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                PieceManagerModTemplateLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                PieceManagerModTemplateLogger.LogError($"There was an issue loading your {ConfigFileName}");
                PieceManagerModTemplateLogger.LogError("Please check your config entries for spelling and format!");
            }
        }


        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        private static ConfigEntry<Toggle> _enableCavePaintingRewards = null!;
        private static ConfigEntry<RewardAmountMode> _rewardAmountMode = null!;
        private static ConfigEntry<float> _rewardAmount = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order = null!;
            [UsedImplicitly] public bool? Browsable = null!;
            [UsedImplicitly] public string? Category = null!;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
        }

        private class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() =>
                "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }

        private readonly struct MuralDetectionCache
        {
            public MuralDetectionCache(bool isEligible, string? discoveryKey)
            {
                IsEligible = isEligible;
                DiscoveryKey = discoveryKey;
            }

            public bool IsEligible { get; }
            public string? DiscoveryKey { get; }
        }

        internal static bool TryDiscoverMural(Component component, Humanoid character)
        {
            if (_enableCavePaintingRewards.Value == Toggle.Off || component == null || character is not Player player || !player.IsOwner())
            {
                return false;
            }

            if (!TryGetMuralDetection(component, out string discoveryKey))
            {
                return false;
            }

            return TryDiscoverMural(component, player, discoveryKey);
        }

        private static bool TryDiscoverMural(Component component, Player player, string discoveryKey)
        {
            if (_enableCavePaintingRewards.Value == Toggle.Off)
            {
                return false;
            }

            if (ZoneSystem.instance == null)
            {
                return false;
            }

            if (ZoneSystem.instance.GetGlobalKey(discoveryKey))
            {
                ShowClaimedMessage(player);
                return false;
            }

            Skills? skills = GetPlayerSkills(player);
            if (skills == null)
            {
                PieceManagerModTemplateLogger.LogWarning("Failed to access player skills while processing a Frost Cave mural reward.");
                return false;
            }

            Skills.SkillType[] availableSkills = RewardableSkillTypes.Where(skillType => Skills.IsSkillValid(skillType)).ToArray();
            if (availableSkills.Length == 0)
            {
                PieceManagerModTemplateLogger.LogWarning("No valid skills were available for a Frost Cave mural reward.");
                return false;
            }

            Skills.SkillType rewardedSkill = availableSkills[UnityEngine.Random.Range(0, availableSkills.Length)];
            if (!TryApplySkillReward(skills, rewardedSkill, out float previousLevel, out float newLevel))
            {
                return false;
            }

            ZoneSystem.instance.GlobalKeyAdd(discoveryKey, false);
            ShowRewardMessage(player, rewardedSkill, newLevel - previousLevel, newLevel);
            PieceManagerModTemplateLogger.LogInfo($"Awarded Frost Cave mural reward {rewardedSkill} (+{newLevel - previousLevel:0.#}) for {discoveryKey}.");
            return true;
        }

        private static void RegisterFallbackTranslations()
        {
            const string rewardMessageKey = "icecaveskills_reward_message";
            const string claimedMessageKey = "icecaveskills_claimed_message";
            const string rewardMessageText = "Ancient mural discovered: {0} increased by {1}. New level: {2}.";
            const string claimedMessageText = "This Frost Cave mural has already granted its blessing.";

            Localizer.AddText(rewardMessageKey, rewardMessageText);
            Localizer.AddText(claimedMessageKey, claimedMessageText);

            Localization.instance.AddWord(rewardMessageKey, rewardMessageText);
            Localization.instance.AddWord(claimedMessageKey, claimedMessageText);
        }

        internal static void TryDiscoverHoveredMural(GameObject? hoverObject, Player player)
        {
            if (_enableCavePaintingRewards.Value == Toggle.Off || hoverObject == null)
            {
                ResetHoveredMuralTracking();
                return;
            }

            int hoverObjectInstanceId = hoverObject.GetInstanceID();
            if (hoverObjectInstanceId == _lastHoverObjectInstanceId)
            {
                return;
            }

            _lastHoverObjectInstanceId = hoverObjectInstanceId;

            Component? component = GetMuralComponent(hoverObject);
            if (component == null || !TryGetMuralDetection(component, out string discoveryKey))
            {
                _lastHoveredDiscoveryKey = null;
                return;
            }

            if (string.Equals(_lastHoveredDiscoveryKey, discoveryKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastHoveredDiscoveryKey = discoveryKey;
            TryDiscoverMural(component, player, discoveryKey);
        }

        private static Skills? GetPlayerSkills(Player player)
        {
            return player.GetSkills();
        }

        private static bool TryApplySkillReward(Skills skills, Skills.SkillType rewardedSkill, out float previousLevel, out float newLevel)
        {
            Skills.Skill? skill = skills.GetSkill(rewardedSkill);
            if (skill == null)
            {
                previousLevel = skills.GetSkillLevel(rewardedSkill);
                newLevel = previousLevel;
                return false;
            }

            previousLevel = skill.m_level;
            float rewardAmount = GetRewardAmount(previousLevel);
            if (rewardAmount <= 0f)
            {
                newLevel = previousLevel;
                return false;
            }

            skills.CheatRaiseSkill(rewardedSkill.ToString(), rewardAmount, showMessage: false);
            newLevel = skills.GetSkill(rewardedSkill).m_level;
            return newLevel > previousLevel;
        }

        private static float GetRewardAmount(float currentLevel)
        {
            float configuredAmount = Mathf.Max(0f, _rewardAmount.Value);
            if (configuredAmount <= 0f || currentLevel >= MaxSkillLevel)
            {
                return 0f;
            }

            float rewardAmount = _rewardAmountMode.Value == RewardAmountMode.PercentageOfMax
                ? MaxSkillLevel * (configuredAmount / 100f)
                : configuredAmount;
            return Mathf.Clamp(rewardAmount, 0f, MaxSkillLevel - currentLevel);
        }

        private static Component? GetMuralComponent(GameObject hoverObject)
        {
            Component? component = hoverObject.GetComponentInParent<HoverText>();
            if (component != null)
            {
                return component;
            }

            component = hoverObject.GetComponentInParent<RuneStone>();
            if (component != null)
            {
                return component;
            }

            return hoverObject.GetComponentInParent<Vegvisir>();
        }

        private static bool TryGetMuralDetection(Component component, out string discoveryKey)
        {
            int instanceId = component.GetInstanceID();
            if (MuralDetectionCacheByComponent.TryGetValue(instanceId, out MuralDetectionCache cachedDetection))
            {
                discoveryKey = cachedDetection.DiscoveryKey ?? string.Empty;
                return cachedDetection.IsEligible;
            }

            bool isEligible = IsFrostCaveMural(component);
            discoveryKey = isEligible ? BuildDiscoveryKey(component) : string.Empty;
            MuralDetectionCacheByComponent[instanceId] = new MuralDetectionCache(isEligible, discoveryKey);
            return isEligible;
        }

        private static bool IsFrostCaveMural(Component component)
        {
            bool hasCaveMarker = false;
            bool hasMuralMarker = false;

            if (UpdateMuralMarkers(component.name, ref hasCaveMarker, ref hasMuralMarker)
                || UpdateMuralMarkers(component.gameObject.name, ref hasCaveMarker, ref hasMuralMarker)
                || UpdateMuralMarkers(component.GetType().Name, ref hasCaveMarker, ref hasMuralMarker)
                || UpdateMuralMarkers(component.gameObject.scene.name, ref hasCaveMarker, ref hasMuralMarker))
            {
                return true;
            }

            foreach (Transform current in component.transform.GetComponentsInParent<Transform>(true))
            {
                if (UpdateMuralMarkers(current.name, ref hasCaveMarker, ref hasMuralMarker))
                {
                    return true;
                }
            }

            Type componentType = component.GetType();
            foreach (string fieldName in SearchableFieldNames)
            {
                FieldInfo? field = componentType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(component) is string fieldValue && UpdateMuralMarkers(fieldValue, ref hasCaveMarker, ref hasMuralMarker))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool UpdateMuralMarkers(string? value, ref bool hasCaveMarker, ref bool hasMuralMarker)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!hasCaveMarker && ContainsAny(value, FrostCaveTerms))
            {
                hasCaveMarker = true;
            }

            if (!hasMuralMarker && ContainsAny(value, MuralTerms))
            {
                hasMuralMarker = true;
            }

            return hasCaveMarker && hasMuralMarker;
        }

        private static bool ContainsAny(string value, IEnumerable<string> candidates)
        {
            foreach (string candidate in candidates)
            {
                if (value.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildDiscoveryKey(Component component)
        {
            Vector3 position = component.transform.position;
            string rawKey = $"{DiscoveryKeyPrefix}{component.GetType().Name}_{component.gameObject.scene.name}_{component.name}_{Mathf.RoundToInt(position.x * 10f)}_{Mathf.RoundToInt(position.y * 10f)}_{Mathf.RoundToInt(position.z * 10f)}";
            StringBuilder sanitized = new(rawKey.Length);
            foreach (char character in rawKey)
            {
                sanitized.Append(char.IsLetterOrDigit(character) ? character : '_');
            }

            return sanitized.ToString();
        }

        private static void ShowRewardMessage(Player player, Skills.SkillType rewardedSkill, float grantedLevels, float newLevel)
        {
            string skillName = GetSkillDisplayName(rewardedSkill);
            string format = Localization.instance.Localize("$icecaveskills_reward_message");
            string message = string.Format(format, skillName, grantedLevels.ToString("0.#"), newLevel.ToString("0.#"));
            player.Message(MessageHud.MessageType.Center, message, 0, null);
        }

        private static void ShowClaimedMessage(Player player)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long lastShown = player.m_nview?.GetZDO()?.GetLong(ClaimedMessageCooldownHash, 0L) ?? 0L;
            if (now - lastShown < 3L)
            {
                return;
            }

            player.m_nview?.GetZDO()?.Set(ClaimedMessageCooldownHash, now);
            player.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$icecaveskills_claimed_message"), 0, null);
        }

        private static void ResetHoveredMuralTracking()
        {
            _lastHoverObjectInstanceId = 0;
            _lastHoveredDiscoveryKey = null;
        }

        private static string GetSkillDisplayName(Skills.SkillType skillType)
        {
            string localizationKey = "$skill_" + skillType.ToString().ToLowerInvariant();
            string localized = Localization.instance.Localize(localizationKey);
            if (!string.Equals(localized, localizationKey, StringComparison.Ordinal))
            {
                return localized;
            }

            StringBuilder builder = new();
            string rawName = skillType.ToString();
            for (int index = 0; index < rawName.Length; ++index)
            {
                char character = rawName[index];
                if (index > 0 && char.IsUpper(character) && !char.IsUpper(rawName[index - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        #endregion
    }

    [HarmonyPatch(typeof(RuneStone), nameof(RuneStone.Interact))]
    public static class RuneStoneInteractPatch
    {
        [UsedImplicitly]
        private static void Postfix(RuneStone __instance, Humanoid character, bool hold)
        {
            if (!hold)
            {
                IceCaveSkillsPlugin.TryDiscoverMural(__instance, character);
            }
        }
    }

    [HarmonyPatch(typeof(Vegvisir), nameof(Vegvisir.Interact))]
    public static class VegvisirInteractPatch
    {
        [UsedImplicitly]
        private static void Postfix(Vegvisir __instance, Humanoid character, bool hold, bool __result)
        {
            if (!hold && __result)
            {
                IceCaveSkillsPlugin.TryDiscoverMural(__instance, character);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UpdateHover))]
    public static class PlayerUpdateHoverPatch
    {
        [UsedImplicitly]
        private static void Postfix(Player __instance)
        {
            if (!__instance.IsOwner())
            {
                return;
            }

            IceCaveSkillsPlugin.TryDiscoverHoveredMural(__instance.GetHoverObject(), __instance);
        }
    }

}
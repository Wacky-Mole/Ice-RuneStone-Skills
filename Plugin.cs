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
        internal const string ModName = "IceAndRuneStoneSkills";
        internal const string DisplayName = "Ice&RuneStone Skills";
        internal const string ModVersion = "1.0.1";
        internal const string Author = "WackyMole";
        private const string ModGUID = Author + "." + ModName;
        private const string DiscoveryKeyPrefix = "IceCaveSkills_Mural_";
        private const string DiscoveryZdoKeyPrefix = "icecaveskills_mural_claimed_";
        internal const string ClaimMuralRpcName = "IceCaveSkills_ClaimMural";
        private const string ResetDiscoveriesCommand = "irs_resetdiscoveries";
        private const string DiscoveryResetVersionGlobalKeyPrefix = "icecaveskills_reset_version_";
        private const string RewardSfxPrefabName = "sfx_Potion_health_medium";
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

        private static readonly string[] BossRuneStoneTerms =
        {
            "boss", "forsaken", "vegvisir"
        };

        private static readonly Skills.SkillType[] RewardableSkillTypes =
            Enum.GetValues(typeof(Skills.SkillType))
                .Cast<Skills.SkillType>()
                .Where(skillType => skillType is not Skills.SkillType.None and not Skills.SkillType.All)
                .ToArray();

        public static readonly ManualLogSource PieceManagerModTemplateLogger =
            BepInEx.Logging.Logger.CreateLogSource(DisplayName);

        private static readonly ConfigSync ConfigSync = new(ModGUID)
        { DisplayName = DisplayName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

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

        private enum RewardSource
        {
            CavePainting,
            Runestone
        }

        public void Awake()
        {
            Localizer.Load();

            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

            _enableCavePaintingRewards = config("2 - Rewards", "Enable Cave Painting Rewards", Toggle.On,
                "If on, Frost Cave cave paintings can award skills.");
            _cavePaintingRewardAmountMode = config("2 - Cave Painting Rewards", "Skill Reward Mode", RewardAmountMode.Flat,
                "Choose whether Frost Cave cave painting rewards are granted as flat skill levels or as a percentage of the max skill level.");
            _cavePaintingRewardAmount = config("2 - Cave Painting Rewards", "Skill Reward Amount", 10f,
                new ConfigDescription(
                    "Amount awarded by each Frost Cave cave painting. In PercentageOfMax mode, this is treated as a percent of the max skill level.",
                    new AcceptableValueRange<float>(0f, MaxSkillLevel)));
            _enableRunestoneRewards = config("3 - Runestone Rewards", "Enable Runestone Rewards", Toggle.On,
                "If on, regular runestones can award skills. Boss runestones and Vegvisirs are excluded.");
            _runestoneRewardAmountMode = config("3 - Runestone Rewards", "Skill Reward Mode", RewardAmountMode.Flat,
                "Choose whether regular runestone rewards are granted as flat skill levels or as a percentage of the max skill level.");
            _runestoneRewardAmount = config("3 - Runestone Rewards", "Skill Reward Amount", 3f,
                new ConfigDescription(
                    "Amount awarded by each eligible regular runestone. In PercentageOfMax mode, this is treated as a percent of the max skill level.",
                    new AcceptableValueRange<float>(0f, MaxSkillLevel)));

            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            RegisterConsoleCommand();
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
        private static ConfigEntry<RewardAmountMode> _cavePaintingRewardAmountMode = null!;
        private static ConfigEntry<float> _cavePaintingRewardAmount = null!;
        private static ConfigEntry<Toggle> _enableRunestoneRewards = null!;
        private static ConfigEntry<RewardAmountMode> _runestoneRewardAmountMode = null!;
        private static ConfigEntry<float> _runestoneRewardAmount = null!;

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
         
            ZNetView? zNetView = GetDiscoveryZNetView(component);
            if (zNetView == null || !zNetView.IsValid())
            {
                return false;
            }

            MuralDiscoveryRpcProxy proxy = EnsureDiscoveryRpcProxy(zNetView);
            return proxy.TryClaimMural(player.GetPlayerID(), discoveryKey);
        }

        internal static bool TryDiscoverRunestone(RuneStone runeStone, Humanoid character)
        {
            if (_enableRunestoneRewards.Value == Toggle.Off || runeStone == null || character is not Player player || !player.IsOwner())
            {
                return false;
            }

            if (!TryGetRegularRunestoneDetection(runeStone, out string discoveryKey))
            {
                return false;
            }

            ZNetView? zNetView = GetDiscoveryZNetView(runeStone);
            if (zNetView == null || !zNetView.IsValid())
            {
                return false;
            }

            MuralDiscoveryRpcProxy proxy = EnsureDiscoveryRpcProxy(zNetView);
            return proxy.TryClaimMural(player.GetPlayerID(), discoveryKey);
        }

        internal static bool TryProcessMuralClaim(ZNetView zNetView, long playerId, string discoveryKey)
        {
            Player? player = Player.GetPlayer(playerId);
            if (player == null)
            {
                return false;
            }

            Component? component = GetMuralComponentFromRoot(zNetView.gameObject);
            RewardSource rewardSource;
            string resolvedDiscoveryKey;
            if (component is RuneStone runeStone && TryGetRegularRunestoneDetection(runeStone, out resolvedDiscoveryKey))
            {
                rewardSource = RewardSource.Runestone;
            }
            else if (component != null && TryGetMuralDetection(component, out resolvedDiscoveryKey))
            {
                rewardSource = RewardSource.CavePainting;
            }
            else
            {
                return false;
            }

            if (!string.Equals(resolvedDiscoveryKey, discoveryKey, StringComparison.Ordinal))
            {
                discoveryKey = resolvedDiscoveryKey;
            }

            ZDO? discoveryZdo = zNetView.GetZDO();
            if (discoveryZdo == null)
            {
                return false;
            }

            int discoveryZdoKey = GetDiscoveryZdoKey(discoveryKey);
            long currentResetVersion = GetDiscoveryResetVersion();
            if (discoveryZdo.GetLong(discoveryZdoKey, -1L) == currentResetVersion)
            {
                ShowClaimedMessage(player, rewardSource);
                return false;
            }

            Skills? skills = GetPlayerSkills(player);
            if (skills == null)
            {
                PieceManagerModTemplateLogger.LogWarning($"Failed to access player skills while processing a {rewardSource} reward.");
                return false;
            }

            Skills.SkillType[] availableSkills = RewardableSkillTypes.Where(skillType => Skills.IsSkillValid(skillType)).ToArray();
            if (availableSkills.Length == 0)
            {
                PieceManagerModTemplateLogger.LogWarning($"No valid skills were available for a {rewardSource} reward.");
                return false;
            }

            Skills.SkillType rewardedSkill = availableSkills[UnityEngine.Random.Range(0, availableSkills.Length)];
            if (!TryApplySkillReward(skills, rewardSource, rewardedSkill, out float previousLevel, out float newLevel))
            {
                return false;
            }

            discoveryZdo.Set(discoveryZdoKey, currentResetVersion);
            ShowRewardMessage(player, rewardSource, rewardedSkill, newLevel - previousLevel, newLevel);
            PlayRewardEffect(player.transform.position);
            PieceManagerModTemplateLogger.LogInfo($"Awarded {rewardSource} reward {rewardedSkill} (+{newLevel - previousLevel:0.#}) for {discoveryKey}.");
            return true;
        }

        internal static void EnsureDiscoveryRpcProxyIfEligible(ZNetView? zNetView)
        {
            if (zNetView == null || !zNetView.IsValid())
            {
                return;
            }

            Component? component = GetMuralComponentFromRoot(zNetView.gameObject);
            if (component == null)
            {
                return;
            }

            if (component is RuneStone runeStone)
            {
                if (!TryGetRegularRunestoneDetection(runeStone, out _) && !TryGetMuralDetection(component, out _))
                {
                    return;
                }
            }
            else if (!TryGetMuralDetection(component, out _))
            {
                return;
            }

            EnsureDiscoveryRpcProxy(zNetView);
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
            IceCaveSkillsPlugin.TryDiscoverMural(component, player);
        }

        private static Skills? GetPlayerSkills(Player player)
        {
            return player.GetSkills();
        }

        private static bool TryApplySkillReward(Skills skills, RewardSource rewardSource, Skills.SkillType rewardedSkill, out float previousLevel, out float newLevel)
        {
            Skills.Skill? skill = skills.GetSkill(rewardedSkill);
            if (skill == null)
            {
                previousLevel = skills.GetSkillLevel(rewardedSkill);
                newLevel = previousLevel;
                return false;
            }

            previousLevel = skill.m_level;
            float rewardAmount = GetRewardAmount(rewardSource, previousLevel);
            if (rewardAmount <= 0f)
            {
                newLevel = previousLevel;
                return false;
            }

            skills.CheatRaiseSkill(rewardedSkill.ToString(), rewardAmount, showMessage: false);
            newLevel = skills.GetSkill(rewardedSkill).m_level;
            return newLevel > previousLevel;
        }

        private static float GetRewardAmount(RewardSource rewardSource, float currentLevel)
        {
            float configuredAmount = Mathf.Max(0f, rewardSource == RewardSource.Runestone ? _runestoneRewardAmount.Value : _cavePaintingRewardAmount.Value);
            if (configuredAmount <= 0f || currentLevel >= MaxSkillLevel)
            {
                return 0f;
            }

            RewardAmountMode rewardMode = rewardSource == RewardSource.Runestone ? _runestoneRewardAmountMode.Value : _cavePaintingRewardAmountMode.Value;
            float rewardAmount = rewardMode == RewardAmountMode.PercentageOfMax
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

        private static Component? GetMuralComponentFromRoot(GameObject rootObject)
        {
            Component? component = rootObject.GetComponentInChildren<HoverText>(true);
            if (component != null)
            {
                return component;
            }

            component = rootObject.GetComponentInChildren<RuneStone>(true);
            if (component != null)
            {
                return component;
            }

            return rootObject.GetComponentInChildren<Vegvisir>(true);
        }

        private static MuralDiscoveryRpcProxy EnsureDiscoveryRpcProxy(ZNetView zNetView)
        {
            return zNetView.GetComponent<MuralDiscoveryRpcProxy>() ?? zNetView.gameObject.AddComponent<MuralDiscoveryRpcProxy>();
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

        private static bool TryGetRegularRunestoneDetection(RuneStone runeStone, out string discoveryKey)
        {
            if (!IsRegularRunestone(runeStone))
            {
                discoveryKey = string.Empty;
                return false;
            }

            discoveryKey = BuildDiscoveryKey(runeStone);
            return true;
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

        private static bool IsRegularRunestone(RuneStone runeStone)
        {
            if (_enableRunestoneRewards.Value == Toggle.Off)
            {
                return false;
            }

            if (IsFrostCaveMural(runeStone))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(runeStone.m_locationName)
                || runeStone.m_showMap
                || ContainsAny(runeStone.m_name, BossRuneStoneTerms)
                || ContainsAny(runeStone.m_topic, BossRuneStoneTerms)
                || ContainsAny(runeStone.m_pinName, BossRuneStoneTerms))
            {
                return false;
            }

            return true;
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

        private static ZNetView? GetDiscoveryZNetView(Component component)
        {
            return component.GetComponentInParent<ZNetView>();
        }

        private static int GetDiscoveryZdoKey(string discoveryKey)
        {
            return (DiscoveryZdoKeyPrefix + discoveryKey).GetStableHashCode();
        }

        private static long GetDiscoveryResetVersion()
        {
            if (ZoneSystem.instance == null)
            {
                return 0L;
            }

            for (long version = 1L; version < 1024L; ++version)
            {
                if (!ZoneSystem.instance.GetGlobalKey(DiscoveryResetVersionGlobalKeyPrefix + version))
                {
                    return version - 1L;
                }
            }

            return 1023L;
        }

        private static bool TryResetDiscoveries(out long newVersion)
        {
            newVersion = 0L;

            if (!IsResetCommandAuthorized())
            {
                return false;
            }

            if (ZoneSystem.instance == null)
            {
                return false;
            }

            newVersion = GetDiscoveryResetVersion() + 1L;
            ZoneSystem.instance.SetGlobalKey(DiscoveryResetVersionGlobalKeyPrefix + newVersion);
            ResetHoveredMuralTracking();
            PieceManagerModTemplateLogger.LogInfo($"Discovery reset command executed. New reset version: {newVersion}.");
            return true;
        }

        private static bool IsResetCommandAuthorized()
        {
            return ConfigSync.IsAdmin;
        }

        private static void RegisterConsoleCommand()
        {
            new Terminal.ConsoleCommand(ResetDiscoveriesCommand,
                "Reset all Ice&RuneStone Skills mural and runestone discoveries so they can be found again.",
                args =>
                {
                    if (args == null)
                    {
                        return;
                    }

                    if (!IsResetCommandAuthorized())
                    {
                        args.Context?.AddString("You must be a server admin to reset discoveries.");
                        return;
                    }

                    if (TryResetDiscoveries(out long newVersion))
                    {
                        args.Context?.AddString($"Ice&RuneStone Skills discoveries reset. New reset version: {newVersion}.");
                        return;
                    }

                    args.Context?.AddString("Unable to reset discoveries right now.");
                },
                optionsFetcher: null,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: false,
                allowInDevBuild: true);
        }

        private static void ShowRewardMessage(Player player, RewardSource rewardSource, Skills.SkillType rewardedSkill, float grantedLevels, float newLevel)
        {
            string skillName = GetSkillDisplayName(rewardedSkill);
            string format = Localization.instance.Localize(rewardSource == RewardSource.Runestone
                ? "$icecaveskills_runestone_reward_message"
                : "$icecaveskills_reward_message");
            string message = string.Format(format, skillName, grantedLevels.ToString("0.#"), newLevel.ToString("0.#"));
            player.Message(MessageHud.MessageType.Center, message, 0, null);
        }

        private static void PlayRewardEffect(Vector3 position)
        {
            GameObject? rewardEffectPrefab = ZNetScene.instance?.GetPrefab(RewardSfxPrefabName);
            if (rewardEffectPrefab == null)
            {
                PieceManagerModTemplateLogger.LogWarning($"Failed to find reward effect prefab '{RewardSfxPrefabName}'.");
                return;
            }

            UnityEngine.Object.Instantiate(rewardEffectPrefab, position, Quaternion.identity);
        }

        private static void ShowClaimedMessage(Player player, RewardSource rewardSource)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long lastShown = player.m_nview?.GetZDO()?.GetLong(ClaimedMessageCooldownHash, 0L) ?? 0L;
            if (now - lastShown < 3L)
            {
                return;
            }

            player.m_nview?.GetZDO()?.Set(ClaimedMessageCooldownHash, now);
            player.Message(MessageHud.MessageType.Center,
                Localization.instance.Localize(rewardSource == RewardSource.Runestone
                    ? "$icecaveskills_runestone_claimed_message"
                    : "$icecaveskills_claimed_message"),
                0,
                null);
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
                if (!IceCaveSkillsPlugin.TryDiscoverRunestone(__instance, character))
                {
                    IceCaveSkillsPlugin.TryDiscoverMural(__instance, character);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Vegvisir), nameof(Vegvisir.Interact))]
    public static class VegvisirInteractPatch
    {
        [UsedImplicitly]
        private static void Postfix(Vegvisir __instance, Humanoid character, bool hold, bool __result)
        {
            _ = __instance;
            _ = character;
            _ = hold;
            _ = __result;
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

    [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.Awake))]
    public static class ZNetViewAwakePatch
    {
        [UsedImplicitly]
        private static void Postfix(ZNetView __instance)
        {
            IceCaveSkillsPlugin.EnsureDiscoveryRpcProxyIfEligible(__instance);
        }
    }

    public class MuralDiscoveryRpcProxy : MonoBehaviour
    {
        private const string RpcName = "IceCaveSkills_ClaimMural";
        private ZNetView _zNetView = null!;

        private void Awake()
        {
            _zNetView = GetComponent<ZNetView>();
            _zNetView.Register<long, string>(RpcName, RPC_ClaimMural);
        }

        public bool TryClaimMural(long playerId, string discoveryKey)
        {
            if (!_zNetView.IsValid())
            {
                return false;
            }

            if (_zNetView.IsOwner())
            {
                return IceCaveSkillsPlugin.TryProcessMuralClaim(_zNetView, playerId, discoveryKey);
            }

            _zNetView.InvokeRPC(RpcName, playerId, discoveryKey);
            return true;
        }

        private void RPC_ClaimMural(long sender, long playerId, string discoveryKey)
        {
            if (_zNetView.IsOwner())
            {
                IceCaveSkillsPlugin.TryProcessMuralClaim(_zNetView, playerId, discoveryKey);
            }
        }
    }

}
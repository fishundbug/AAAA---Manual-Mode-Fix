using System;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using Verse;
using RimWorld;
using HarmonyLib;

namespace AAAAManualModeFix
{
    /// <summary>
    /// Mod 主类：提供模组设置 UI，并在运行时动态注入 Harmony 补丁以解决原模组各类 Bug 与判定缺陷
    /// </summary>
    public class ManualModeFixMod : Mod
    {
        public static ManualModeFixSettings Settings { get; private set; }

        public ManualModeFixMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ManualModeFixSettings>();

            try
            {
                var harmony = new Harmony("fishundbug.AAAAManualModeFix");

                var watcherType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Watcher");
                if (watcherType != null)
                {
                    // 1. 动态注册：AllowedAreaChangeNormalMode 拦截与雷达解除避难联动补丁
                    var normalModeMethod = AccessTools.Method(watcherType, "AllowedAreaChangeNormalMode");
                    if (normalModeMethod != null)
                    {
                        var prefix = AccessTools.Method(typeof(AllowedAreaChangeNormalModePatch), nameof(AllowedAreaChangeNormalModePatch.Prefix));
                        var postfix = AccessTools.Method(typeof(AllowedAreaChangeNormalModePatch), nameof(AllowedAreaChangeNormalModePatch.Postfix));
                        harmony.Patch(normalModeMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                    }

                    // 2. 动态注册：进入避难警报雷达开启联动补丁
                    var dangerModeMethod = AccessTools.Method(watcherType, "AllowedAreaChangeDangerMode");
                    if (dangerModeMethod != null)
                    {
                        var dangerPostfix = AccessTools.Method(typeof(AllowedAreaChangeDangerModePatch), nameof(AllowedAreaChangeDangerModePatch.Postfix));
                        harmony.Patch(dangerModeMethod, postfix: new HarmonyMethod(dangerPostfix));
                    }

                    // 3. 动态注册：对 AAAA 内部自动解除入口进行监控，代替耗时的 StackTrace 漫游
                    var takeInventoryMethod = AccessTools.Method(watcherType, "TakeInventory");
                    BindAutoClearFlags(harmony, takeInventoryMethod);
                }

                // 4. 动态注册 AAAA 机械剿灭 Patch 入口监测
                var mechDefeatPatchType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Patch_LordJob_MechanoidDefendBase");
                if (mechDefeatPatchType != null)
                {
                    var onDefeatPostfix = AccessTools.Method(mechDefeatPatchType, "OnDefeat_Postfix");
                    BindAutoClearFlags(harmony, onDefeatPostfix);
                }

                // 5. 动态注册 AAAA 虫巢被歼 Patch 入口监测
                var hiveCountPatchType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Patch_HiveUtility");
                if (hiveCountPatchType != null)
                {
                    var countPostfix = AccessTools.Method(hiveCountPatchType, "TotalSpawnedHivesCount_Postfix");
                    BindAutoClearFlags(harmony, countPostfix);
                }

                // 6. 机械集群落地兜底：不依赖 AAAA 内部的落点判定，直接在落地后强制触发一次避难警报
                var spawnThingsMethod = AccessTools.Method(typeof(DropPodIncoming), "SpawnThings");
                if (spawnThingsMethod != null)
                {
                    var spawnThingsPostfix = AccessTools.Method(typeof(MechClusterArrivalFallbackPatch), nameof(MechClusterArrivalFallbackPatch.Postfix));
                    harmony.Patch(spawnThingsMethod, postfix: new HarmonyMethod(spawnThingsPostfix));
                }

                // 7. 动态注册：禁用休眠过滤拦截补丁 (拦截原模组的 Patch 自身)
                var dangerWatcherPatchType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Patch_DangerWatcher");
                if (dangerWatcherPatchType != null)
                {
                    var affectsStoryDangerPrefix = AccessTools.Method(dangerWatcherPatchType, "AffectsStoryDanger_Prefix");
                    if (affectsStoryDangerPrefix != null)
                    {
                        var disableDormantPrefix = AccessTools.Method(typeof(DisableDormantFilterPatch), nameof(DisableDormantFilterPatch.Prefix));
                        harmony.Patch(affectsStoryDangerPrefix, prefix: new HarmonyMethod(disableDormantPrefix));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[AAAAManualModeFix] 初始化 Harmony 补丁时发生异常: " + ex);
            }
        }

        /// <summary>
        /// 将指定的方法绑定高效率的自动解除执行上下文标志 (Prefix 置 true, Postfix 置 false)
        /// </summary>
        private void BindAutoClearFlags(Harmony harmony, MethodInfo targetMethod)
        {
            if (targetMethod == null) return;
            var flagPrefix = AccessTools.Method(typeof(AutoClearContextTracker), nameof(AutoClearContextTracker.Prefix));
            var flagPostfix = AccessTools.Method(typeof(AutoClearContextTracker), nameof(AutoClearContextTracker.Postfix));
            harmony.Patch(targetMethod, prefix: new HarmonyMethod(flagPrefix), postfix: new HarmonyMethod(flagPostfix));
        }

        public override string SettingsCategory()
        {
            return "[AAAA] Manual Mode Fix";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // 1. 自动解除修复开关
            listing.CheckboxLabeled(
                "[修复] 启用手动模式避难自动解除修复", 
                ref Settings.enableAutoRecoveryFix,
                "修复原版模组在开启‘手动避难模式’下，原版机械族或虫群被全歼后仍会自动解除避难的逻辑缺陷。"
            );
            listing.Gap(4f);
            
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("介绍：启用该项后，当在游戏内开启手动避难模式时，哪怕外部机械集群被全歼或虫巢被剿灭，系统也不会代为自作聪明解除警报。警报的撤销将完全服从你在 UI 界面上的‘手动解除’指令，消除自动解除对策略防守产生的干扰。");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(14f);

            // 2. 禁用休眠过滤判定开关
            listing.CheckboxLabeled(
                "[增强] 强制禁用原模组对休眠机械的过滤判定", 
                ref Settings.disableDormantFilter,
                "强行禁用 AAAA 模组对‘休眠状态机械族’的忽略过滤。开启后，休眠的机械集群也会被判定为致命威胁。"
            );
            listing.Gap(4f);

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("介绍：原版 AAAA 模组中，如果开启了防守威胁判定，模组会过滤掉处于休眠（Dormant）状态的机械人，使其在沉睡时不触发避难警报。启用该补丁开关后，模组将退回最安全的判定机制，把刚空投落地但仍在沉睡的机械集群无条件判定为致命威胁，并立刻全自动拉响避难警报，为殖民地整装迎敌争取最多时间。");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(14f);

            // 3. 反隐雷达避难联动开关
            listing.CheckboxLabeled(
                "[联动] 启用反隐雷达与避难警报自动化联动", 
                ref Settings.enableRadarLinkage,
                "当进入避难警报时自动开启全图反隐雷达；当解除避难警报时自动将其关闭以节省资源。"
            );
            listing.Gap(4f);

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("介绍：开启该联动后，补丁将动态监视你建造的反隐雷达（包含机械人 Praetor 的移动雷达）。当 AAAA 警报拉响或进入手动避难时，全图所有雷达将自动紧急开机探测隐形怪；当避难解除、警报平息后，雷达将自动转为休眠关机状态，省去你手动切换的繁琐，并在日常运营中最大限度节约电能。");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.End();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            Log.Message("[AAAAManualModeFix] 模组设置已保存并生效。");
        }
    }

    /// <summary>
    /// 自动清除执行上下文监控器 (取代 StackTrace 的高性能核心)
    /// </summary>
    public static class AutoClearContextTracker
    {
        public static bool isAutoClearing = false;

        public static void Prefix()
        {
            isAutoClearing = true;
        }

        public static void Postfix()
        {
            isAutoClearing = false;
        }
    }

    /// <summary>
    /// 修复手动避难模式被强行自动解除的 Harmony 补丁类
    /// </summary>
    public static class AllowedAreaChangeNormalModePatch
    {
        private static FieldInfo _manualOnlyModeField;
        private static bool _fieldResolved;

        public static bool Prefix()
        {
            try
            {
                if (!ManualModeFixMod.Settings.enableAutoRecoveryFix)
                {
                    return true;
                }

                // 判断 AAAA 模组是否开启了手动模式
                if (!IsManualOnlyModeEnabled())
                {
                    return true;
                }

                // 在手动模式下，且监控到当前正处于“自动解除上下文”期间，执行瞬间拦截！
                if (AutoClearContextTracker.isAutoClearing)
                {
                    return false; // 高性能常数级 $O(1)$ 拦截，零性能抖动！
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AAAAManualModeFix] 拦截判定时发生异常，放行原版: " + ex);
            }

            return true;
        }

        /// <summary>
        /// 后置拦截：解除避难警报时自动将雷达关闭
        /// </summary>
        public static void Postfix(Map map)
        {
            if (ManualModeFixMod.Settings.enableRadarLinkage && map != null)
            {
                RadarLinkageUtility.SetAllRadarsState(map, false);
            }
        }

        private static bool IsManualOnlyModeEnabled()
        {
            if (!_fieldResolved)
            {
                var settingType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Setting");
                if (settingType != null)
                {
                    _manualOnlyModeField = settingType.GetField("manualOnlyMode", BindingFlags.Public | BindingFlags.Static);
                }
                _fieldResolved = true;
            }

            if (_manualOnlyModeField != null)
            {
                return (bool)_manualOnlyModeField.GetValue(null);
            }

            return false;
        }
    }

    /// <summary>
    /// 进入避难警报的 Harmony 后置补丁
    /// </summary>
    public static class AllowedAreaChangeDangerModePatch
    {
        /// <summary>
        /// 后置拦截：拉响避难警报时自动开启雷达
        /// </summary>
        public static void Postfix(Map map)
        {
            if (ManualModeFixMod.Settings.enableRadarLinkage && map != null)
            {
                RadarLinkageUtility.SetAllRadarsState(map, true);
            }
        }
    }

    /// <summary>
    /// 机械集群落地兜底补丁：在 AAAA 漏判时，落地后强制触发一次避难警报
    /// </summary>
    public static class MechClusterArrivalFallbackPatch
    {
        private const int MapTriggerCooldownTicks = 300;
        private static readonly HashSet<string> TriggeredEvents = new HashSet<string>();
        private static readonly Dictionary<int, int> LastTriggeredTickByMap = new Dictionary<int, int>();

        public static void Postfix(DropPodIncoming __instance)
        {
            try
            {
                if (__instance == null || ManualModeFixMod.Settings == null || !ManualModeFixMod.Settings.disableDormantFilter)
                {
                    return;
                }

                var map = ((Thing)__instance).Map;
                if (map == null)
                {
                    return;
                }

                if (!LooksLikeMechanoidCluster(__instance))
                {
                    return;
                }

                int currentTick = Find.TickManager?.TicksGame ?? -1;
                if (currentTick >= 0 && LastTriggeredTickByMap.TryGetValue(map.uniqueID, out int lastTick) && currentTick - lastTick < MapTriggerCooldownTicks)
                {
                    return;
                }

                string eventKey = BuildEventKey(__instance, map);
                if (!TriggeredEvents.Add(eventKey))
                {
                    return;
                }

                if (!MapHasHostileMechanoids(map))
                {
                    return;
                }

                if (currentTick >= 0)
                {
                    LastTriggeredTickByMap[map.uniqueID] = currentTick;
                }

                TriggerAAAAAlert(map);
                Log.Message($"[AAAAManualModeFix] 机械集群落地兜底已触发避难警报：map={map.uniqueID}");
            }
            catch (Exception ex)
            {
                Log.Warning("[AAAAManualModeFix] 机械集群落地兜底补丁执行异常: " + ex);
            }
        }

        private static string BuildEventKey(DropPodIncoming dropPod, Map map)
        {
            var defName = dropPod?.def?.defName ?? "unknown";
            var position = ((Thing)dropPod).Position;
            return $"{map.uniqueID}:{defName}:{position.x}:{position.z}";
        }


        private static void TriggerAAAAAlert(Map map)
        {
            var watcherType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Watcher");
            if (watcherType == null)
            {
                Log.Warning("[AAAAManualModeFix] 未找到 AAAA Watcher 类型，无法触发机械集群兜底警报。");
                return;
            }

            var mechClusterDictProperty = watcherType.GetProperty("MechanoidClusterDictionary", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (mechClusterDictProperty != null)
            {
                var dict = mechClusterDictProperty.GetValue(null, null) as IDictionary<int, bool>;
                if (dict != null)
                {
                    dict[map.uniqueID] = true;
                }
            }

            var dangerModeMethod = watcherType.GetMethod("AllowedAreaChangeDangerMode", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (dangerModeMethod != null)
            {
                dangerModeMethod.Invoke(null, new object[] { map });
                return;
            }

            Log.Warning("[AAAAManualModeFix] 未找到 AAAA AllowedAreaChangeDangerMode 方法，无法触发机械集群兜底警报。");
        }

        private static bool LooksLikeMechanoidCluster(DropPodIncoming dropPod)
        {
            var defName = dropPod?.def?.defName;
            if (!string.IsNullOrEmpty(defName) && defName.IndexOf("Mech", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (dropPod?.innerContainer != null)
            {
                for (int i = 0; i < dropPod.innerContainer.Count; i++)
                {
                    var thing = dropPod.innerContainer[i];
                    if (thing is Pawn pawn && pawn.RaceProps?.IsMechanoid == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MapHasHostileMechanoids(Map map)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
            {
                return false;
            }

            var playerFaction = Faction.OfPlayer;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (pawn != null && pawn.RaceProps?.IsMechanoid == true && pawn.Faction != null && pawn.Faction.HostileTo(playerFaction))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 强行禁用原模组对休眠机械过滤的 Harmony 补丁类
    /// </summary>
    public static class DisableDormantFilterPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (ManualModeFixMod.Settings.disableDormantFilter)
            {
                // 让 AAAA 的 Prefix 返回 true，从而指示 Harmony 继续执行原版的 DangerWatcher.AffectsStoryDanger
                __result = true; 
                // 拦截 AAAA 的 Prefix，跳过它自身的休眠判断逻辑
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 处理反隐雷达联动的辅助工具类
    /// 极致性能优化版：仅精准扫描极少数特定目标，将扫描开销直接降为微秒级
    /// </summary>
    public static class RadarLinkageUtility
    {
        private const string TargetBuildingDefName = "NCL_Overwatch_Nexus";

        /// <summary>
        /// 精准且无 Bug 的联动设定
        /// </summary>
        public static void SetAllRadarsState(Map map, bool state)
        {
            if (map == null) return;

            int count = 0;
            try
            {
                // 1. 精准处理玩家拥有的雷达建筑 (仅在 map.listerBuildings.allBuildingsColonist 中匹配 NCL_Overwatch_Nexus)
                if (map.listerBuildings != null && map.listerBuildings.allBuildingsColonist != null)
                {
                    var colonistBuildings = map.listerBuildings.allBuildingsColonist;
                    for (int i = 0; i < colonistBuildings.Count; i++)
                    {
                        var b = colonistBuildings[i];
                        if (b != null && b.def?.defName == TargetBuildingDefName)
                        {
                            if (ProcessThing(b, state)) count++;
                        }
                    }
                }

                // 2. 精准处理玩家拥有的机械人/宠物 (仅在玩家阵营的 Spawned 列表中检索，排除野兽和敌军)
                if (map.mapPawns != null)
                {
                    var playerFaction = RimWorld.Faction.OfPlayer;
                    var playerPawns = map.mapPawns.SpawnedPawnsInFaction(playerFaction);
                    if (playerPawns != null)
                    {
                        for (int i = 0; i < playerPawns.Count; i++)
                        {
                            var pawn = playerPawns[i];
                            if (pawn != null)
                            {
                                if (ProcessThing(pawn, state)) count++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AAAAManualModeFix] 雷达联动状态设定时发生异常: {ex}");
            }

            if (count > 0)
            {
                Log.Message($"[AAAAManualModeFix] 避难警报切换 -> 已将 {count} 台玩家反隐雷达自动设定为：{(state ? "【开启】" : "【关闭】")}");
            }
        }

        private static bool ProcessThing(Thing thing, bool state)
        {
            if (thing is ThingWithComps thingWithComps && thingWithComps.AllComps != null)
            {
                var comps = thingWithComps.AllComps;
                for (int j = 0; j < comps.Count; j++)
                {
                    var comp = comps[j];
                    if (comp.GetType().FullName == "NCL.CompAntiInvisibilityField" || 
                        comp.GetType().FullName == "CompAntiInvisibilityField")
                    {
                        var field = comp.GetType().GetField("isActivated", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(comp, state);
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}

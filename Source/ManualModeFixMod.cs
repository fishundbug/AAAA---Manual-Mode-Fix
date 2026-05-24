using System;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using Verse;
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
                    // 1. 动态注册：手动模式自动解除 Bug 修复补丁 + 警报解除雷达联动
                    var normalModeMethod = AccessTools.Method(watcherType, "AllowedAreaChangeNormalMode");
                    if (normalModeMethod != null)
                    {
                        var prefixMethod = AccessTools.Method(typeof(AllowedAreaChangeNormalModePatch), nameof(AllowedAreaChangeNormalModePatch.Prefix));
                        var postfixMethod = AccessTools.Method(typeof(AllowedAreaChangeNormalModePatch), nameof(AllowedAreaChangeNormalModePatch.Postfix));
                        harmony.Patch(normalModeMethod, 
                            prefix: new HarmonyMethod(prefixMethod),
                            postfix: new HarmonyMethod(postfixMethod)
                        );
                        Log.Message("[AAAAManualModeFix] 成功应用：手动避难自动解除修复与解除避难雷达联动补丁。");
                    }

                    // 2. 动态注册：进入避难雷达联动补丁
                    var dangerModeMethod = AccessTools.Method(watcherType, "AllowedAreaChangeDangerMode");
                    if (dangerModeMethod != null)
                    {
                        var dangerPostfix = AccessTools.Method(typeof(AllowedAreaChangeDangerModePatch), nameof(AllowedAreaChangeDangerModePatch.Postfix));
                        harmony.Patch(dangerModeMethod, postfix: new HarmonyMethod(dangerPostfix));
                        Log.Message("[AAAAManualModeFix] 成功应用：进入避难雷达联动补丁。");
                    }
                }

                // 3. 动态注册：禁用休眠过滤拦截补丁 (拦截原模组的 Patch 自身)
                var dangerWatcherPatchType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Patch_DangerWatcher");
                if (dangerWatcherPatchType != null)
                {
                    var affectsStoryDangerPrefix = AccessTools.Method(dangerWatcherPatchType, "AffectsStoryDanger_Prefix");
                    if (affectsStoryDangerPrefix != null)
                    {
                        var disableDormantPrefix = AccessTools.Method(typeof(DisableDormantFilterPatch), nameof(DisableDormantFilterPatch.Prefix));
                        harmony.Patch(affectsStoryDangerPrefix, prefix: new HarmonyMethod(disableDormantPrefix));
                        Log.Message("[AAAAManualModeFix] 成功应用：禁用休眠过滤拦截补丁。");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[AAAAManualModeFix] 初始化 Harmony 补丁时发生异常: " + ex);
            }
        }

        /// <summary>
        /// 设置页面标题
        /// </summary>
        public override string SettingsCategory()
        {
            return "[AAAA] Manual Mode Fix";
        }

        /// <summary>
        /// 绘制模组设置界面
        /// </summary>
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

        /// <summary>
        /// 保存设置
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            Log.Message("[AAAAManualModeFix] 模组设置已保存并生效。");
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

                if (!IsManualOnlyModeEnabled())
                {
                    return true;
                }

                var stackTrace = new StackTrace();
                for (int i = 2; i < stackTrace.FrameCount; i++)
                {
                    var frame = stackTrace.GetFrame(i);
                    var method = frame?.GetMethod();
                    if (method == null) continue;

                    string typeName = method.DeclaringType?.FullName ?? "";
                    string methodName = method.Name;

                    if (methodName == "TakeInventory" || 
                        typeName.Contains("Patch_LordJob_MechanoidDefendBase") || 
                        typeName.Contains("Patch_HiveUtility"))
                    {
                        return false;
                    }
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
    /// 强行禁用原模组对休眠机械过滤的 Harmony 补丁类
    /// </summary>
    public static class DisableDormantFilterPatch
    {
        public static bool Prefix(ref bool __result)
        {
            if (ManualModeFixMod.Settings.disableDormantFilter)
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 处理反隐雷达联动的辅助工具类
    /// </summary>
    public static class RadarLinkageUtility
    {
        /// <summary>
        /// 遍历全图所有的 ThingWithComps 实体，搜寻反隐雷达组件并动态变更其开关状态
        /// </summary>
        public static void SetAllRadarsState(Map map, bool state)
        {
            if (map?.listerThings?.AllThings == null)
            {
                return;
            }

            int count = 0;
            try
            {
                var allThings = map.listerThings.AllThings;
                for (int i = 0; i < allThings.Count; i++)
                {
                    var thing = allThings[i];
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
                                    count++;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AAAAManualModeFix] 执行反隐雷达自动联动状态设定时发生异常: {ex}");
            }

            if (count > 0)
            {
                Log.Message($"[AAAAManualModeFix] 避难状态发生改变 -> 已将该地图的 {count} 个反隐雷达设备设置为：{(state ? "【开启】" : "【关闭】")} 状态。");
            }
        }
    }
}

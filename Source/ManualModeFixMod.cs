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

                // 1. 动态注册：手动模式自动解除 Bug 修复补丁
                var watcherType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Watcher");
                if (watcherType != null)
                {
                    var normalModeMethod = AccessTools.Method(watcherType, "AllowedAreaChangeNormalMode");
                    if (normalModeMethod != null)
                    {
                        var prefixMethod = AccessTools.Method(typeof(AllowedAreaChangeNormalModePatch), nameof(AllowedAreaChangeNormalModePatch.Prefix));
                        harmony.Patch(normalModeMethod, prefix: new HarmonyMethod(prefixMethod));
                        Log.Message("[AAAAManualModeFix] 成功应用：手动避难自动解除修复补丁。");
                    }
                }

                // 2. 动态注册：禁用休眠过滤拦截补丁 (拦截原模组的 Patch 自身)
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
            
            // 详细功能介绍文案
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label("介绍：启用该项后，当在游戏内开启手动避难模式时，哪怕外部机械集群被全歼或虫巢被剿灭，系统也不会代为自作聪明解除警报。警报的撤销将完全服从你在 UI 界面上的‘手动解除’指令，消除自动解除对策略防守产生的干扰。");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            listing.Gap(18f);

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
                // 如果用户在设置中关闭了该修复，直接放行
                if (!ManualModeFixMod.Settings.enableAutoRecoveryFix)
                {
                    return true;
                }

                // 判断 AAAA 模组是否开启了手动模式
                if (!IsManualOnlyModeEnabled())
                {
                    return true;
                }

                // 分析调用栈，检测是否属于威胁解除时发起的“自动解除”
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
                        // 处于手动模式下且判定为自动剿灭解除，直接强行拦截！
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
    /// 强行禁用原模组对休眠机械过滤的 Harmony 补丁类
    /// 核心设计：拦截原模组的 Patch 自身，使其不执行过滤代码，完美让控制权回传到原版检测逻辑
    /// </summary>
    public static class DisableDormantFilterPatch
    {
        public static bool Prefix(ref bool __result)
        {
            // 如果用户在设置中开启了“禁用休眠过滤”
            if (ManualModeFixMod.Settings.disableDormantFilter)
            {
                // 直接返回 false，强行掐断 AAAA 模组 Prefix 的执行！
                // 这将完美使 AAAA 原模组对 DangerWatcher.AffectsStoryDanger 的过滤失效，
                // 让 RimWorld 核心原版逻辑顺利执行，从而把所有休眠机械判定为实际威胁，触发自动避难。
                return false;
            }

            return true; // 否则放行，让 AAAA 原模组正常处理过滤
        }
    }
}

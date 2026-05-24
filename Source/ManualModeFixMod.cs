using System;
using System.Reflection;
using System.Diagnostics;
using UnityEngine;
using Verse;
using HarmonyLib;

namespace AAAAManualModeFix
{
    /// <summary>
    /// Mod 主类：动态注册 Harmony 补丁，修复 AAAA 模组在手动模式下仍会自动解除避难的 Bug
    /// </summary>
    public class ManualModeFixMod : Mod
    {
        public ManualModeFixMod(ModContentPack content) : base(content)
        {
            try
            {
                var harmony = new Harmony("fishundbug.AAAAManualModeFix");

                // 动态获取目标类型和方法，实现完美解耦
                var targetType = AccessTools.TypeByName("seekiworks_AllowedAreaAutomaticAdapter.Watcher");
                if (targetType == null)
                {
                    Log.Warning("[AAAAManualModeFix] 未找到 AAAA 模组的 Watcher 类，跳过补丁应用。");
                    return;
                }

                // 目标方法：AllowedAreaChangeNormalMode(Map map, bool reverseLookup = false)
                var targetMethod = AccessTools.Method(targetType, "AllowedAreaChangeNormalMode");
                if (targetMethod == null)
                {
                    Log.Warning("[AAAAManualModeFix] 未找到方法 Watcher.AllowedAreaChangeNormalMode，跳过补丁应用。");
                    return;
                }

                var prefixMethod = AccessTools.Method(typeof(AllowedAreaChangeNormalModePatch), nameof(AllowedAreaChangeNormalModePatch.Prefix));
                
                harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
                Log.Message("[AAAAManualModeFix] 成功应用 AAAA 手动模式修复补丁（基于反射与调用栈动态拦截）！");
            }
            catch (Exception ex)
            {
                Log.Error("[AAAAManualModeFix] 应用 Harmony 补丁时发生异常: " + ex);
            }
        }
    }

    /// <summary>
    /// 拦截解除避难逻辑的 Harmony 补丁类
    /// </summary>
    public static class AllowedAreaChangeNormalModePatch
    {
        private static FieldInfo _manualOnlyModeField;
        private static bool _fieldResolved;

        /// <summary>
        /// Prefix 拦截器：如果是系统自动解除（且处于手动模式下），则拦截此次调用
        /// </summary>
        public static bool Prefix()
        {
            try
            {
                // 1. 判断是否开启了手动模式
                if (!IsManualOnlyModeEnabled())
                {
                    return true; // 未开启手动模式，允许原版自动解除逻辑正常执行
                }

                // 2. 分析调用栈，区分“系统自动解除”与“玩家点击按钮解除”
                var stackTrace = new StackTrace();
                // 忽略前两个 frame（即当前 Prefix 方法和 Harmony 包装方法）
                for (int i = 2; i < stackTrace.FrameCount; i++)
                {
                    var frame = stackTrace.GetFrame(i);
                    var method = frame?.GetMethod();
                    if (method == null) continue;

                    string typeName = method.DeclaringType?.FullName ?? "";
                    string methodName = method.Name;

                    // 若调用栈包含 AAAA 模组的自动清理(TakeInventory)或剿灭事件(OnDefeat/TotalSpawnedHivesCount)
                    if (methodName == "TakeInventory" || 
                        typeName.Contains("Patch_LordJob_MechanoidDefendBase") || 
                        typeName.Contains("Patch_HiveUtility"))
                    {
                        // 处于手动模式，且属于自动解除调用，直接拦截！
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AAAAManualModeFix] 拦截判定时发生异常，默认放行原逻辑: " + ex);
            }

            return true; // 允许执行（如玩家手动按钮产生的延迟预约解除）
        }

        /// <summary>
        /// 反射获取 AAAA 模组 Setting.manualOnlyMode 的实时值
        /// </summary>
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
}

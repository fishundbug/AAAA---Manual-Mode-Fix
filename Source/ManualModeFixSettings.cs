using Verse;

namespace AAAAManualModeFix
{
    /// <summary>
    /// 模组设置类，保存用户的个性化开关
    /// </summary>
    public class ManualModeFixSettings : ModSettings
    {
        public bool enableAutoRecoveryFix = true;
        public bool disableDormantFilter = false;
        public bool enableRadarLinkage = true; // 新增：雷达与避难联动开关

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enableAutoRecoveryFix, "enableAutoRecoveryFix", true);
            Scribe_Values.Look(ref disableDormantFilter, "disableDormantFilter", false);
            Scribe_Values.Look(ref enableRadarLinkage, "enableRadarLinkage", true);
        }
    }
}

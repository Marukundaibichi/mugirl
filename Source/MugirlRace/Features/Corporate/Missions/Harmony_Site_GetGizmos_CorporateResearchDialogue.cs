using System.Linq;
using HarmonyLib;
using RimWorld.Planet;

namespace Mugirl.Features.Corporate;

// 研究站地图上的命令按钮，可重新打开研究员对白。
[HarmonyPatch(typeof(Site), nameof(Site.GetGizmos))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Low, "Research site dialogue remains accessible through the corporate terminal.")]
public static class Harmony_Site_GetGizmos_CorporateResearchDialogue
{
    public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Site __instance)
    {
        foreach (Gizmo gizmo in __result)
        {
            yield return gizmo;
        }

        if (CorporateNetwork.Current is not { } network || !__instance.HasMap || !__instance.Map.mapPawns.FreeColonistsSpawned.Any())
        {
            yield break;
        }

        foreach (CorporateMission mission in network.Missions)
        {
            if (mission.IsSide && mission.site == __instance && mission.state == CorporateMissionState.Active
                && mission.choice == CorporateResearchChoice.Undecided)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Mugirl.CQ.ResearchTitle".Translate(),
                    defaultDesc = "Mugirl.CQ.ReopenDialogue".Translate(),
                    action = () => network.ShowResearchDialogue(mission),
                    icon = TexCommand.ForbidOff,
                };
                yield break;
            }
        }
    }
}

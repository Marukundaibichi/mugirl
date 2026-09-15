using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Mugirl
{
    public partial class Window_CorporateComms
    {
        private Vector2 missionScroll;
        private int missionTab;
        private float missionHeight = 600f;

        private void DrawMissions(Rect rect)
        {
            GUI.BeginGroup(rect);
            float width = rect.width;
            string[] tabs = { "Mugirl.CQ.Daily", "Mugirl.CQ.Side", "Mugirl.CQ.Main" };
            for (int i = 0; i < tabs.Length; i++)
            {
                if (CorporateUI.Button(new Rect(i * (width / 3), 0, width / 3 - 5, 34), tabs[i].Translate(), primary: missionTab == i))
                { missionTab = i; missionScroll = Vector2.zero; }
            }
            if (missionTab == 2)
            {
                DrawIntroduction(new Rect(0, 44, width, rect.height - 44));
                GUI.EndGroup();
                return;
            }
            CorporateUI.Label(new Rect(0, 43, width, 44), "Mugirl.CQ.BoardHelp".Translate(network.ActiveMissionCount), color: CorporateUI.Muted);
            Rect outRect = new Rect(0, 94, width, rect.height - 94);
            Rect view = new Rect(0, 0, width - 20, missionHeight);
            Widgets.BeginScrollView(outRect, ref missionScroll, view);
            float y = 0;
            var items = network.Missions.Where(m => m.IsSide == (missionTab == 1))
                .OrderBy(m => m.IsFinished ? 2 : m.state == CorporateMissionState.Available ? 1 : 0).ThenByDescending(m => m.id).ToList();
            if (items.Count == 0)
            {
                CorporateUI.Notice(new Rect(0, y, view.width, 100), (missionTab == 1 ? "Mugirl.CQ.NoSide" : "Mugirl.CQ.NoMissions").Translate());
                y += 110;
            }
            foreach (CorporateMission mission in items)
            {
                string description = mission.Description;
                if (mission.kind != CorporateMissionKind.Native)
                    description += "\n" + "Mugirl.CQ.Terms".Translate(mission.duration / 60000f, mission.goodwill).ToString();
                if (mission.kind == CorporateMissionKind.Fusion)
                    description += "\n" + "Mugirl.CQ.FusionTerms".Translate(mission.reward, CorporateNetwork.MissionConfig.fusionRefusalGoodwill).ToString();
                float descHeight = Text.CalcHeight(description, view.width - 24);
                float cardHeight = 120 + descHeight;
                if (mission.pendingGoods.Count > 0) cardHeight += 36;
                CorporateUI.Panel(new Rect(0, y, view.width, cardHeight));
                CorporateUI.Label(new Rect(12, y + 8, view.width - 24, 28), mission.Title, color: CorporateUI.Accent);
                CorporateUI.Label(new Rect(12, y + 37, view.width - 24, descHeight), description);
                float row = y + 43 + descHeight;
                string progress = mission.kind == CorporateMissionKind.Processing || mission.kind == CorporateMissionKind.Distribution
                    ? " · " + "Mugirl.CQ.Progress".Translate(mission.progress, mission.kind == CorporateMissionKind.Distribution ? mission.SalesTarget : mission.count).ToString() : "";
                string time = "";
                if (mission.state == CorporateMissionState.Available) time = "Mugirl.CQ.OfferTime".Translate(((mission.acceptByTick - CorporateNetwork.Now) / 60000f).ToString("0.0"));
                else if (mission.state == CorporateMissionState.Active && mission.kind != CorporateMissionKind.Native)
                    time = "Mugirl.CQ.DueTime".Translate(Mathf.Max(0, (mission.deadline - CorporateNetwork.Now) / 60000f).ToString("0.0"));
                CorporateUI.Label(new Rect(12, row, view.width - 24, 25), mission.Status + progress + " · " + time, color: CorporateUI.Muted);
                row += 30;
                float x = 12;
                if (mission.state == CorporateMissionState.Available)
                {
                    bool enabled = network.CanAcceptMission(mission, context, out string reason);
                    if (CorporateUI.Button(new Rect(x, row, 145, 30), "Mugirl.CQ.Accept".Translate(), enabled, true))
                    {
                        CorporateMission selected = mission;
                        MugirlGameUtility.Windows.Add(Dialog_MessageBox.CreateConfirmation(selected.Description + "\n\n" + "Mugirl.CQ.SignConfirm".Translate(selected.cost),
                            () => { if (!network.AcceptMission(selected, context, out string error)) Messages.Message(error, MessageTypeDefOf.RejectInput, false); }));
                    }
                    if (!enabled) CorporateUI.Label(new Rect(165, row, view.width - 177, 42), reason, color: CorporateUI.Danger);
                }
                else
                {
                    if (mission.state == CorporateMissionState.Active && mission.kind == CorporateMissionKind.Processing)
                    {
                        if (CorporateUI.Button(new Rect(x, row, 126, 30), "Mugirl.CQ.Submit".Translate(), context.IsValid, true))
                            if (!network.SubmitMissionProducts(mission, context)) Messages.Message("Mugirl.CQ.NoProducts".Translate(), MessageTypeDefOf.RejectInput, false);
                        x += 132;
                    }
                    if (mission.state == CorporateMissionState.Ready)
                    {
                        int total = mission.reward + (mission.kind == CorporateMissionKind.Processing ? mission.cost : 0);
                        if (CorporateUI.Button(new Rect(x, row, 170, 30), "Mugirl.CQ.Claim".Translate(total), context.IsValid, true))
                            if (!network.ClaimMission(mission, context)) Messages.Message("Mugirl.CQ.DeliveryFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                        x += 176;
                    }
                    if (mission.site != null && mission.site.Spawned && x + 100 < view.width)
                    {
                        if (CorporateUI.Button(new Rect(x, row, 92, 30), "Mugirl.CQ.Locate".Translate())) { Close(); CameraJumper.TryJumpAndSelect(mission.site); }
                        x += 98;
                    }
                    if (mission.IsSide && mission.state == CorporateMissionState.Active && mission.choice == CorporateResearchChoice.Undecided
                        && mission.site?.HasMap == true && mission.site.Map.mapPawns.FreeColonistsSpawned.Any() && x + 100 < view.width)
                    {
                        if (CorporateUI.Button(new Rect(x, row, 92, 30), "Mugirl.CQ.Talk".Translate())) network.ShowResearchDialogue(mission);
                        x += 98;
                    }
                    if (mission.quest != null && x + 100 < view.width)
                    {
                        if (CorporateUI.Button(new Rect(x, row, 92, 30), "Mugirl.CQ.QuestLog".Translate())) { Close(); MugirlGameUtility.MainTabs.SetCurrentTab(MainButtonDefOf.Quests); }
                        x += 98;
                    }
                    if (mission.state == CorporateMissionState.Active && mission.kind != CorporateMissionKind.Investment && mission.kind != CorporateMissionKind.Native && x + 95 < view.width)
                    {
                        if (CorporateUI.Button(new Rect(x, row, 90, 30), "Mugirl.CQ.Abandon".Translate()))
                        {
                            CorporateMission selected = mission;
                            MugirlGameUtility.Windows.Add(Dialog_MessageBox.CreateConfirmation("Mugirl.CQ.AbandonConfirm".Translate(), () => network.AbandonMission(selected), true));
                        }
                    }
                }
                if (mission.pendingGoods.Count > 0)
                {
                    row += 36;
                    if (CorporateUI.Button(new Rect(12, row, view.width - 24, 30), "Mugirl.CQ.ReceiveGoods".Translate(), context.IsValid))
                        network.ReceiveMissionGoods(mission, context);
                }
                y += cardHeight + 12;
            }
            missionHeight = Mathf.Max(y, 120);
            Widgets.EndScrollView();
            GUI.EndGroup();
        }
    }
}

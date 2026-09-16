using System.Collections.Generic;
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
                if (CorporateUI.Button(new Rect(i * (width / 3), 0, width / 3 - 5, 34), tabs[i].Translate(), primary: missionTab == i, id: "missions/tab/" + i) && missionTab != i)
                {
                    int tab = i;
                    RequestContentTransition(() => { missionTab = tab; missionScroll = Vector2.zero; }, "missions/tab/" + tab);
                }
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
            CorporateUI.BeginScrollView(outRect, ref missionScroll, view, "missions/page-scroll");
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
                bool canLocate = mission.site != null && mission.site.Spawned;
                bool canTalk = mission.IsSide && mission.state == CorporateMissionState.Active && mission.choice == CorporateResearchChoice.Undecided
                    && mission.site?.HasMap == true && mission.site.Map.mapPawns.FreeColonistsSpawned.Any();
                bool canAbandon = mission.state == CorporateMissionState.Active && mission.kind != CorporateMissionKind.Investment && mission.kind != CorporateMissionKind.Native;
                List<float> actionWidths = new List<float>();
                if (mission.state == CorporateMissionState.Available) actionWidths.Add(145f);
                else
                {
                    if (mission.state == CorporateMissionState.Active && mission.kind == CorporateMissionKind.Processing) actionWidths.Add(126f);
                    if (mission.state == CorporateMissionState.Ready) actionWidths.Add(170f);
                    if (canLocate) actionWidths.Add(92f);
                    if (canTalk) actionWidths.Add(92f);
                    if (mission.quest != null) actionWidths.Add(92f);
                    if (canAbandon) actionWidths.Add(90f);
                }
                float actionHeight = CorporateMissionActionHeight(view.width - 24f, actionWidths);
                float cardHeight = 120f + descHeight + actionHeight - 30f;
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
                    if (CorporateUI.Button(new Rect(x, row, 145, 30), "Mugirl.CQ.Accept".Translate(), enabled, true, "missions/accept/" + mission.id))
                    {
                        CorporateMission selected = mission;
                        Confirm(selected.Description + "\n\n" + "Mugirl.CQ.SignConfirm".Translate(selected.cost),
                            () => { bool success = network.AcceptMission(selected, context, out string error); CorporateFeedback(success, error); });
                    }
                    if (!enabled) CorporateUI.Label(new Rect(165, row, view.width - 177, 42), reason, color: CorporateUI.Danger);
                }
                else
                {
                    if (mission.state == CorporateMissionState.Active && mission.kind == CorporateMissionKind.Processing)
                    {
                        if (CorporateUI.Button(CorporateMissionActionRect(ref x, ref row, view.width, 126f), "Mugirl.CQ.Submit".Translate(), context.IsValid, true, "missions/submit/" + mission.id))
                            CorporateFeedback(network.SubmitMissionProducts(mission, context), "Mugirl.CQ.NoProducts".Translate());
                    }
                    if (mission.state == CorporateMissionState.Ready)
                    {
                        int total = mission.reward + (mission.kind == CorporateMissionKind.Processing ? mission.cost : 0);
                        if (CorporateUI.Button(CorporateMissionActionRect(ref x, ref row, view.width, 170f), "Mugirl.CQ.Claim".Translate(total), context.IsValid, true, "missions/claim/" + mission.id))
                            CorporateFeedback(network.ClaimMission(mission, context), "Mugirl.CQ.DeliveryFailed".Translate());
                    }
                    if (canLocate)
                    {
                        if (CorporateUI.Button(CorporateMissionActionRect(ref x, ref row, view.width, 92f), "Mugirl.CQ.Locate".Translate(), id: "missions/locate/" + mission.id))
                            CloseThen(() => CameraJumper.TryJumpAndSelect(mission.site));
                    }
                    if (canTalk)
                    {
                        if (CorporateUI.Button(CorporateMissionActionRect(ref x, ref row, view.width, 92f), "Mugirl.CQ.Talk".Translate(), id: "missions/talk/" + mission.id)) network.ShowResearchDialogue(mission);
                    }
                    if (mission.quest != null)
                    {
                        if (CorporateUI.Button(CorporateMissionActionRect(ref x, ref row, view.width, 92f), "Mugirl.CQ.QuestLog".Translate(), id: "missions/log/" + mission.id))
                            CloseThen(() => MugirlGameUtility.MainTabs.SetCurrentTab(MainButtonDefOf.Quests));
                    }
                    if (canAbandon)
                    {
                        if (CorporateUI.Button(CorporateMissionActionRect(ref x, ref row, view.width, 90f), "Mugirl.CQ.Abandon".Translate(), id: "missions/abandon/" + mission.id))
                        {
                            CorporateMission selected = mission;
                            Confirm("Mugirl.CQ.AbandonConfirm".Translate(), () => CorporateFeedback(network.AbandonMission(selected), null), true);
                        }
                    }
                }
                if (mission.pendingGoods.Count > 0)
                {
                    row += 36;
                    if (CorporateUI.Button(new Rect(12, row, view.width - 24, 30), "Mugirl.CQ.ReceiveGoods".Translate(), context.IsValid, id: "missions/goods/" + mission.id))
                        CorporateFeedback(network.ReceiveMissionGoods(mission, context), "Mugirl.CQ.DeliveryFailed".Translate());
                }
                y += cardHeight + 12;
            }
            missionHeight = Mathf.Max(y, 120);
            CorporateUI.EndScrollView();
            GUI.EndGroup();
        }

        private static Rect CorporateMissionActionRect(ref float x, ref float y, float width, float buttonWidth)
        {
            buttonWidth = Mathf.Min(buttonWidth, width - 24f);
            if (x > 12f && x + buttonWidth > width - 12f) { x = 12f; y += 36f; }
            Rect rect = new Rect(x, y, buttonWidth, 30f);
            x += buttonWidth + 6f;
            return rect;
        }

        private static float CorporateMissionActionHeight(float width, List<float> widths)
        {
            float x = 0f;
            float height = 30f;
            foreach (float requested in widths)
            {
                float button = Mathf.Min(requested, width);
                if (x > 0f && x + button > width) { x = 0f; height += 36f; }
                x += button + 6f;
            }
            return height;
        }
    }
}

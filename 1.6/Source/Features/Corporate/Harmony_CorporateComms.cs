using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Mugirl
{
    internal static class CorporateCommsAccess
    {
        internal static string ConsoleFailure(Building_CommsConsole console, Pawn pawn)
        {
            if (console?.Spawned != true || pawn?.Spawned != true || pawn.Map != console.Map
                || !MugirlWildSlaveUtility.IsPlayerFaction(console.Faction) || !MugirlWildSlaveUtility.IsPlayerFaction(pawn.Faction)
                || pawn.Dead || pawn.Downed || pawn.InMentalState || !pawn.RaceProps.Humanlike)
                return "Mugirl.CorporateUI.Unavailable".Translate();
            if (!console.CanUseCommsNow) return "Mugirl.CorporateUI.NoPower".Translate();
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking)) return "Mugirl.CorporateUI.CannotTalk".Translate();
            if (!pawn.CanReach(console, PathEndMode.InteractionCell, Danger.Some)) return "Mugirl.CorporateUI.NoPath".Translate();
            if (CorporateIntroduction.Current?.Completed != true) return "Mugirl.CorporateUI.Locked".Translate();
            return null;
        }

        internal static bool AtSettlement(Caravan caravan, Settlement settlement)
        {
            return caravan?.Spawned == true && caravan.IsPlayerControlled && !caravan.pather.Moving
                && settlement?.Spawned == true && caravan.Tile == settlement.Tile
                && settlement.Faction?.def == MugirlContentDefOf.Mugirl_GiantCorporations_Hostile;
        }

        internal static void OpenConsole(Building_CommsConsole console, Pawn pawn)
        {
            string failure = ConsoleFailure(console, pawn);
            if (failure != null)
            {
                Messages.Message(failure, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            MugirlGameUtility.Windows.Add(new Window_CorporateComms(new CorporateTradeContext(console.Map,
                () => ConsoleFailure(console, pawn) == null && pawn.Position == console.InteractionCell) { Negotiator = pawn }));
        }

        internal static void OpenSettlement(Caravan caravan, Settlement settlement)
        {
            if (!AtSettlement(caravan, settlement) || CorporateIntroduction.Current?.Completed != true) return;
            MugirlGameUtility.Windows.Add(new Window_CorporateComms(new CorporateTradeContext(caravan,
                () => AtSettlement(caravan, settlement) && CorporateIntroduction.Current?.Completed == true)
            {
                Negotiator = caravan.PawnsListForReading.Where(p => p.IsColonist && !p.IsSlave && !p.Dead && !p.Downed
                    && !p.InMentalState && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                    .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0).FirstOrDefault()
            }));
        }
    }

    // PatchGovernance: 只追加独立通讯选项，不替换原版外交或轨道贸易；失败时保留原菜单。
    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetFloatMenuOptions))]
    internal static class Harmony_CorporateCommsConsole
    {
        private static void Postfix(Building_CommsConsole __instance, Pawn myPawn, ref IEnumerable<FloatMenuOption> __result)
        {
            __result = Append(__result, __instance, myPawn);
        }

        private static IEnumerable<FloatMenuOption> Append(IEnumerable<FloatMenuOption> original, Building_CommsConsole console, Pawn pawn)
        {
            if (original != null) foreach (FloatMenuOption option in original) yield return option;
            if (!MugirlWildSlaveUtility.IsPlayerFaction(console.Faction) || !MugirlWildSlaveUtility.IsPlayerFaction(pawn?.Faction)) yield break;
            string failure = CorporateCommsAccess.ConsoleFailure(console, pawn);
            string label = "Mugirl.CorporateUI.CallOption".Translate();
            if (failure != null) yield return new FloatMenuOption(label + ": " + failure, null);
            else yield return new FloatMenuOption(label, () =>
            {
                if (CorporateCommsAccess.ConsoleFailure(console, pawn) != null) return;
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(CorporateIntroductionDefOf.Mugirl_CorporateUseComms, console), JobTag.Misc);
            });
        }
    }

    // PatchGovernance: 偿债入口独立于原版 Visitable，债务敌对不能封死还款；不改据点贸易。
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetCaravanGizmos))]
    internal static class Harmony_CorporateSettlementGizmos
    {
        private static void Postfix(Settlement __instance, Caravan caravan, ref IEnumerable<Gizmo> __result)
        {
            __result = Append(__result, __instance, caravan);
        }

        private static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> original, Settlement settlement, Caravan caravan)
        {
            if (original != null) foreach (Gizmo gizmo in original) yield return gizmo;
            if (!CorporateCommsAccess.AtSettlement(caravan, settlement)) yield break;
            Command_Action command = new Command_Action
            {
                defaultLabel = "Mugirl.CorporateUI.SettlementOption".Translate(),
                defaultDesc = "Mugirl.CorporateUI.SettlementDescription".Translate(),
                icon = TexCommand.OpenLinkedQuestTex,
                action = () => CorporateCommsAccess.OpenSettlement(caravan, settlement)
            };
            if (CorporateIntroduction.Current?.Completed != true) command.Disable("Mugirl.CorporateUI.Locked".Translate());
            yield return command;
        }
    }

    public class JobDriver_CorporateUseComms : JobDriver
    {
        private Building_CommsConsole Console => job.GetTarget(TargetIndex.A).Thing as Building_CommsConsole;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return CorporateCommsAccess.ConsoleFailure(Console, pawn) == null
                && pawn.Reserve(Console, job, 1, -1, null, errorOnFailed);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => CorporateCommsAccess.ConsoleFailure(Console, pawn) != null);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_General.Do(() => CorporateCommsAccess.OpenConsole(Console, pawn));
        }
    }

    public class FloatMenuProvider_CorporateRepresentative : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => false;
        public override bool TargetPawnValid(Pawn target, FloatMenuContext context)
        {
            return base.TargetPawnValid(target, context) && CorporateIntroduction.Current?.CanTalk(context.FirstSelectedPawn, target) == true;
        }
        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn actor = context.FirstSelectedPawn;
            if (CorporateIntroduction.Current?.CanTalk(actor, clickedPawn) != true) yield break;
            FloatMenuOption option = new FloatMenuOption("Mugirl.CorporateIntro.Talk".Translate(), () =>
            {
                if (CorporateIntroduction.Current?.CanTalk(actor, clickedPawn) == true)
                    actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(CorporateIntroductionDefOf.Mugirl_CorporateTalkRepresentative, clickedPawn), JobTag.Misc);
            }, MenuOptionPriority.High);
            if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
            {
                option.Disabled = true;
                option.Label += ": " + "Mugirl.CorporateUI.NoPath".Translate();
            }
            yield return FloatMenuUtility.DecoratePrioritizedTask(option, actor, clickedPawn);
        }
    }

    public class JobDriver_CorporateTalkRepresentative : JobDriver
    {
        private Pawn Representative => job.GetTarget(TargetIndex.A).Thing as Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return CorporateIntroduction.Current?.CanTalk(pawn, Representative) == true
                && pawn.Reserve(Representative, job, 1, -1, null, errorOnFailed);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => CorporateIntroduction.Current?.CanTalk(pawn, Representative) != true);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Do(() => CorporateIntroduction.Current?.ShowDialog(pawn, Representative));
        }
    }

    // PatchGovernance: 只记录开户前可证实的攻击责任，送货员单独处理；不改伤害与吸收结果。
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    internal static class Harmony_CorporateIntroductionDamage
    {
        private static void Prefix(Pawn __instance, ref DamageInfo dinfo)
        {
            CorporateIntroduction.Current?.NotifyDamage(__instance, dinfo);
        }
    }

    // PatchGovernance: 只在原版实际死亡后保存送货员结果，不把开始交战误认为死亡。
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class Harmony_CorporateIntroductionKilled
    {
        private static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance.Dead) CorporateIntroduction.Current?.NotifyKilled(__instance, dinfo);
        }
    }
}

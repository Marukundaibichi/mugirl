using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Mugirl
{
    // 巨企业务唯一的资产保管者；各合同只保存 Vault 中对象的引用。
    public partial class CorporateNetwork : GameComponent, IThingHolder
    {
        public const int DayTicks = 60000;
        public const int WeekTicks = 7 * DayTicks;
        private ThingOwner<Thing> vault;
        private int nextRefreshTick = -1;
        private int sequence;
        private int nextServiceTick;
        private List<CorporateRecord> records = new List<CorporateRecord>();

        public CorporateNetwork(Game game) { vault = new ThingOwner<Thing>(this, false, LookMode.Deep); }
        public static CorporateNetwork Current => MugirlGameUtility.GameComponent<CorporateNetwork>();
        public static int Now => MugirlTickUtility.CurrentGameTickOrFallback(0);
        public bool Unlocked => MugirlGameUtility.GameComponent<CorporateIntroduction>()?.Completed == true;
        public Faction CorporateFaction => MugirlGameUtility.Factions?.FirstFactionOfDef(MugirlContentDefOf.Mugirl_GiantCorporations_Hostile);
        public ThingOwner<Thing> Vault => vault;
        public int NextRefreshTick => nextRefreshTick;
        public IReadOnlyList<CorporateRecord> Records => records;
        public IThingHolder ParentHolder => null;
        public ThingOwner GetDirectlyHeldThings() => vault;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, vault);

        public int NewId() { return ++sequence; }

        public bool CanTrade(CorporateTradeContext context, out string reason)
        {
            if (context == null || !context.IsValid) reason = "Mugirl.Corporate.InvalidContext".Translate();
            else if (!Unlocked) reason = "Mugirl.Corporate.Locked".Translate();
            else if (CorporateFaction == null || CorporateFaction.defeated) reason = "Mugirl.Corporate.NoFaction".Translate();
            else if (CorporateFaction.HostileTo(MugirlWildSlaveUtility.PlayerFaction)) reason = "Mugirl.Corporate.Hostile".Translate();
            else { reason = null; return true; }
            return false;
        }

        public void Record(string key, string detail, int amount = 0)
        {
            records.Add(new CorporateRecord { tick = Now, key = key, detail = detail, amount = amount });
            if (records.Count > 200) records.RemoveRange(0, records.Count - 200);
        }

        public void EnsureWeeklyOffers()
        {
            if (!Unlocked || (nextRefreshTick >= 0 && Now < nextRefreshTick)) return;
            // 一次跳到当前周期，旧档不会补生成过去每一周的人口或合同。
            nextRefreshTick = nextRefreshTick < 0 ? Now + WeekTicks
                : nextRefreshTick + ((Now - nextRefreshTick) / WeekTicks + 1) * WeekTicks;
            TradeRefreshWeekly();
            PeopleRefreshWeekly();
            QuestsRefreshWeekly();
        }

        public override void GameComponentTick()
        {
            if (Now < nextServiceTick) return;
            nextServiceTick = Now + 250;
            EnsureWeeklyOffers();
            TradeTick();
            FinanceTick();
            PeopleTick();
            QuestsTick();
        }

        public override void LoadedGame() { nextServiceTick = 0; }
        public override void StartedNewGame() { nextServiceTick = 0; }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref vault, "corporateVault", this);
            Scribe_Values.Look(ref nextRefreshTick, "corporateNextRefresh", -1);
            Scribe_Values.Look(ref sequence, "corporateSequence", 0);
            Scribe_Collections.Look(ref records, "corporateRecords", LookMode.Deep);
            TradeExposeData();
            FinanceExposeData();
            PeopleExposeData();
            QuestsExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (vault == null) vault = new ThingOwner<Thing>(this, false, LookMode.Deep);
                if (records == null) records = new List<CorporateRecord>();
                nextServiceTick = 0;
            }
        }

        partial void TradeExposeData();
        partial void TradeTick();
        partial void TradeRefreshWeekly();
        partial void FinanceExposeData();
        partial void FinanceTick();
        partial void PeopleExposeData();
        partial void PeopleTick();
        partial void PeopleRefreshWeekly();
        partial void QuestsExposeData();
        partial void QuestsTick();
        partial void QuestsRefreshWeekly();
    }

    public sealed class CorporateRecord : IExposable
    {
        public int tick;
        public string key;
        public string detail;
        public int amount;
        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref key, "key", null);
            Scribe_Values.Look(ref detail, "detail", null);
            Scribe_Values.Look(ref amount, "amount", 0);
        }
    }
}

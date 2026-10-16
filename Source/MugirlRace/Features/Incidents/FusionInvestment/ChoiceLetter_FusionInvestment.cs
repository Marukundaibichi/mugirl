namespace Mugirl.Features.Incidents;

// 信件形态的投资入口。未决信件保留投资、暂缓和拒绝选项以及所属地图。
public class ChoiceLetter_FusionInvestment : ChoiceLetter
{
    public Map? map;

    public override IEnumerable<DiaOption> Choices
    {
        get
        {
            Map? targetMap = ResolveMap();
            int optionCount = FusionInvestmentMath.InvestmentAmounts.Length;
            for (int i = 0; i < optionCount; i++)
            {
                int amount = FusionInvestmentMath.InvestmentAmounts[i];
                DiaOption option = new DiaOption("Mugirl.FusionInvestment.OptionInvest".Translate(amount));
                if (targetMap == null)
                {
                    option.Disable("Mugirl.FusionInvestment.OptionNoMap".Translate());
                }
                else if (!FusionInvestmentUtility.HasSilver(targetMap, amount))
                {
                    option.Disable("Mugirl.FusionInvestment.OptionNoSilver".Translate(amount));
                }
                else
                {
                    option.action = delegate
                    {
                        if (FusionInvestmentUtility.TryAcceptInvestment(targetMap, amount, null))
                        {
                            GameUtility.TryRemoveLetter(this);
                        }
                    };
                }

                option.resolveTree = true;
                yield return option;
            }

            DiaOption postpone = new DiaOption("Mugirl.FusionInvestment.OptionPostpone".Translate())
            {
                resolveTree = true
            };
            yield return postpone;

            DiaOption reject = new DiaOption("Mugirl.FusionInvestment.OptionReject".Translate())
            {
                action = delegate
                {
                    FusionInvestmentUtility.ScheduleNextOffer();
                    Messages.Message("Mugirl.FusionInvestment.MessageRejected".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
                    GameUtility.TryRemoveLetter(this);
                },
                resolveTree = true
            };
            yield return reject;
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref map, "map");
    }

    private Map? ResolveMap()
    {
        if (map != null)
        {
            return map;
        }

        GameUtility.TryResolvePlayerEventMap(out Map? resolvedMap);
        map = resolvedMap;
        return map;
    }
}

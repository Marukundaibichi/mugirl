namespace Mugirl.Features.Incidents;

// 投资人的对话表现；选项、禁用理由和回调次序独立于业务状态存储。
internal static class FusionInvestmentDialog
{
    internal static void ShowDialog(Pawn? investor, Pawn negotiator)
    {
        if (!FusionInvestmentUtility.CanNegotiateWithInvestor(negotiator, investor))
        {
            Messages.Message("Mugirl.FusionInvestment.AlreadyResolved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        Map? targetMap = investor.Map;
        DiaNode node = new DiaNode("Mugirl.FusionInvestment.DialogText".Translate(investor.Named("INVESTOR"), negotiator.Named("NEGOTIATOR")));
        ReadOnlySpan<int> amounts = FusionInvestmentMath.InvestmentAmounts;
        for (int i = 0; i < amounts.Length; i++)
        {
            int amount = amounts[i];
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
                    FusionInvestmentUtility.TryAcceptInvestment(targetMap, amount, investor);
                };
            }

            option.resolveTree = true;
            node.options.Add(option);
        }

        // “之后再谈”只关闭对话：投资人留在地图上等待（Lord 计时结束才离开），玩家可以再次交谈。
        node.options.Add(new DiaOption("Mugirl.FusionInvestment.OptionPostpone".Translate())
        {
            resolveTree = true
        });

        node.options.Add(new DiaOption("Mugirl.FusionInvestment.OptionReject".Translate())
        {
            action = delegate
            {
                FusionInvestmentUtility.RejectInvestment(investor);
            },
            resolveTree = true
        });

        GameUtility.TryAddWindow(new Dialog_NodeTree(node, delayInteractivity: true, title: "Mugirl.FusionInvestment.DialogTitle".Translate()));
    }
}

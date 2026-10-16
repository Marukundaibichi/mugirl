using Mugirl.Features.Corporate;

namespace Mugirl.SaveUpgrade;

internal sealed partial class CorporateUpgradeModule
{
    static partial void RegisterStructure(UpgradeModuleBuilder module)
    {
        // 先恢复省略的历史哨兵值，再由收据步骤计算规范金额。
        module.MaterializeDefault(OwnerSelector.Class(typeof(CorporateOrder)), "creditedTurnover", "-1", LegacyEvidence.LegacyClass,
            "Corporate.ReceiptDefaults computes the signed amount before the order is loaded.");
        module.MaterializeDefault(OwnerSelector.Class(typeof(CorporatePersonOffer)), "paidAmount", "-1", LegacyEvidence.LegacyClass,
            "Corporate.ReceiptDefaults materializes the original paid receipt before repricing.");
        module.Step(new CorporateReceiptDefaults());
        module.Step(new CorporateNetworkStructure());
        module.Step(new CorporateContactStructure());
        module.Step(new CorporateSupportGraph());
        foreach ((string oldName, _, _) in CorporateNetworkStructure.Fields)
        {
            module.ConsumeLabel(CorporateNetworkStructure.Owner, oldName, CorporateNetworkStructure.StepId, "Move intact value into its owning account.");
        }

        foreach ((string oldName, _, _) in CorporateContactStructure.Fields)
        {
            module.ConsumeLabel(CorporateContactStructure.Owner, oldName, CorporateContactStructure.StepId, "Preserve independent contact facts in the contact state.");
        }

        module.ConsumeLabel(OwnerSelector.Class(typeof(CorporatePersonOffer)), "priceVersion", CorporateReceiptDefaults.StepId, "Materialize a durable completion at the original per-offer load point.");
        module.ConsumeLabel(OwnerSelector.Class(typeof(LordJob_CorporateSupport)), "supportGraphVersion", CorporateSupportGraph.StepId, "Preserve the exact graph until its original first-tick completion.");
        module.ClaimLordJob("Mugirl.LordJob_CorporateSupport", CorporateSupportGraph.StepId);
        module.PreserveInFlightJobs("Mugirl.JobDriver_CorporateSupportTend");
        module.PreserveInFlightJobs("Mugirl.JobDriver_CorporateTalkRepresentative");
        module.PreserveInFlightJobs("Mugirl.JobDriver_CorporateUseComms");
    }
}

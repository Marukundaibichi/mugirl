namespace Mugirl.SaveUpgrade;

internal sealed partial class IncidentsUpgradeModule
{
    static partial void RegisterStructure(UpgradeModuleBuilder module)
    {
        module.Step(new StorylineStateUpgrade());
        module.Step(new MigrationRecordsUpgrade());
        module.Step(new PunisherRecordsUpgrade());
        foreach ((string oldName, _, _) in StorylineStateUpgrade.Fields)
        {
            module.ConsumeLabel(StorylineStateUpgrade.Owner, oldName, StorylineStateUpgrade.StepId, "Move exact storyline fact without changing its schedule.");
        }

        foreach (string oldName in MigrationRecordsUpgrade.OldLabels)
        {
            module.ConsumeLabel(MigrationRecordsUpgrade.Owner, oldName, MigrationRecordsUpgrade.StepId, "Preserve indexed member data and complete initialization at the original load point.");
        }

        foreach (string oldName in PunisherRecordsUpgrade.OldLabels)
        {
            module.ConsumeLabel(PunisherRecordsUpgrade.Owner, oldName, PunisherRecordsUpgrade.StepId, "Keep Pawn, spawn tick, leaving state and loot references together.");
        }

        module.PreserveInFlightJobs("Mugirl.JobDriver_Nuzzle");
        module.PreserveInFlightJobs("Mugirl.JobDriver_ReadCourierDiary");
        module.PreserveInFlightJobs("Mugirl.JobDriver_TalkCourier");
        module.PreserveInFlightJobs("Mugirl.JobDriver_TalkFusionInvestor");
    }
}

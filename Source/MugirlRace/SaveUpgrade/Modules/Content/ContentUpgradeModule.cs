namespace Mugirl.SaveUpgrade;

internal sealed partial class ContentUpgradeModule
{
    static partial void RegisterStructure(UpgradeModuleBuilder module)
    {
        const string Retired = "Definition already absent before the 1.7 refactor; remove only its typed legacy references.";
        module.DropDef("ThingDef", "Mugirl_MechanoidBulldog", DefDropPolicy.RemoveThings, Retired);
        module.DropDef("PawnKindDef", "Mugirl_MechanoidBulldog", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("BodyDef", "Mugirl_MechanoidBulldogBody", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("BodyPartDef", "Mugirl_MechanoidTailAssembly", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("BodyPartDef", "Mugirl_MechanoidTailChainsaw", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("BodyPartGroupDef", "Mugirl_BulldogLeftForeclaw", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("BodyPartGroupDef", "Mugirl_BulldogRightForeclaw", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("BodyPartGroupDef", "Mugirl_MechanoidTail", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("AnimationDef", "Mugirl_MilkInteractionDrinkerAnimation", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("EmotionShapeDef", "lovinblush", DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("ManeuverDef", ContentRetiredBulldogVerbsStep.Maneuver, DefDropPolicy.RemoveReferences, Retired);
        module.DropDef("ToolCapacityDef", "Mugirl_BulldogTailAssistedCut", DefDropPolicy.RemoveReferences, Retired);
        // 全部已发布内容及完整旧 Def/组件关系中均无使用者；不删除承载它的任意健康状态。
        OwnerSelector unusedRescue = OwnerSelector.HediffComp("Mugirl.HediffComp_JoinWhenRescued");
        module.DropLabel(unusedRescue, "joinedAlready", "Published dead component: no legacy or current Def instantiates it; no runtime state or successor exists.");
        module.DropLabel(unusedRescue, "ticksUntilNextCheck", "Published dead component: no legacy or current Def instantiates it; no runtime state or successor exists.");
        module.DeclareRemovableContainer("verbTracker/verbs", "VerbTracker rebuilds its list from current properties and matches saved state by load ID, not by list index.");
        module.Step(new ContentRetiredBulldogVerbsStep());
    }
}

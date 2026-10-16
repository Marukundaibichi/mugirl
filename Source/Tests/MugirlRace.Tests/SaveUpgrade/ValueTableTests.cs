using Mugirl.SaveUpgrade;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>枚举成员、字符串值、字符串 defName 与旧默认值补写。</summary>
public sealed class ValueTableTests
{
    [Fact]
    public void RenamesEnumMembersInScalarsFlagsListsAndDictionaryKeysAndValues()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameEnumMember("Mugirl.Mode", "OldMode", "NewMode");
            m.DeclareEnumSite(OwnerSelector.Class("Mugirl.Owner"), "mode", "Mugirl.Mode");
            m.DeclareEnumSite(OwnerSelector.Class("Mugirl.Owner"), "flags", "Mugirl.Mode", flags: true);
            m.DeclareEnumSite(OwnerSelector.Class("Mugirl.Owner"), "modes", "Mugirl.Mode");
            m.DeclareEnumSite(OwnerSelector.Class("Mugirl.Owner"), "byMode", "Mugirl.Mode", part: ValuePart.Keys);
            m.DeclareEnumSite(OwnerSelector.Class("Mugirl.Owner"), "modeByName", "Mugirl.Mode", part: ValuePart.Values);
        });
        string result = SaveXml.Upgraded(
            "<li Class=\"Mugirl.Owner\"><mode>OldMode</mode><flags>Other, OldMode</flags><modes><li>OldMode</li><li>Other</li></modes>"
            + "<byMode><keys><li>OldMode</li></keys><values><li>OldMode</li></values></byMode>"
            + "<modeByName><keys><li>OldMode</li></keys><values><li>OldMode</li></values></modeByName></li><li Class=\"Other\"><mode>OldMode</mode></li>",
            tables);

        Assert.Equal(
            "<li Class=\"Mugirl.Owner\"><mode>NewMode</mode><flags>Other, NewMode</flags><modes><li>NewMode</li><li>Other</li></modes>"
            + "<byMode><keys><li>NewMode</li></keys><values><li>OldMode</li></values></byMode>"
            + "<modeByName><keys><li>OldMode</li></keys><values><li>NewMode</li></values></modeByName></li><li Class=\"Other\"><mode>OldMode</mode></li>",
            result);
    }

    [Fact]
    public void MapsTextValuesAndDropsThemThroughTheContainerRules()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.DeclareValueContext("QuestTags", OwnerSelector.Class("Mugirl.Part"), "questTags");
            m.DeclareValueContext("Signal", OwnerSelector.Class("Mugirl.Part"), "inSignal");
            m.DeclareValueContext("Signals", OwnerSelector.Class("Mugirl.Part"), "signals");
            m.DeclareValueContext("Keys", OwnerSelector.Class("Mugirl.Part"), "byKey", part: ValuePart.Keys);
            m.DeclareRemovableContainer("questTags", "List<string> of quest tags; order has no meaning.");
            m.DeclareParallelGroup(OwnerSelector.Class("Mugirl.Part"), "signals", "ticks");
            m.MapValue("QuestTags", "Mugirl_OldTag", "Mugirl_NewTag");
            m.MapValue("QuestTags", "Mugirl_DeadTag", null);
            m.MapValue("Signal", "Quest.OldSignal", null);
            m.MapValue("Signals", "Dead", null);
            m.MapValue("Keys", "DeadKey", null);
        });
        string result = SaveXml.Upgraded(
            "<li Class=\"Mugirl.Part\"><questTags><li>Mugirl_OldTag</li><li>Mugirl_DeadTag</li><li>Keep</li></questTags><inSignal>Quest.OldSignal</inSignal>"
            + "<signals><li>Dead</li><li>Live</li></signals><ticks><li>1</li><li>2</li></ticks>"
            + "<byKey><keys><li>DeadKey</li><li>LiveKey</li></keys><values><li>1</li><li>2</li></values></byKey></li>"
            + "<li Class=\"Mugirl.Part\"><signals><li>Dead</li></signals></li>",
            tables);

        // 可移除容器直接删除；单值标签删除（读档时回到默认值）；并行组同下标一起删除；字典成对删除。
        // 第二个所属元素缺少并行伙伴列表 ticks，只删除 signals 中的项。
        Assert.Equal(
            "<li Class=\"Mugirl.Part\"><questTags><li>Mugirl_NewTag</li><li>Keep</li></questTags>"
            + "<signals><li>Live</li></signals><ticks><li>2</li></ticks>"
            + "<byKey><keys><li>LiveKey</li></keys><values><li>2</li></values></byKey></li>"
            + "<li Class=\"Mugirl.Part\"><signals></signals></li>",
            result);
    }

    [Fact]
    public void DroppedStringValuesInOtherListsAreNulledWithIsNull()
    {
        // ScribeExtractor.ValueFromNode 只把 IsNull="True" 读成 null；文本 null 会读成字符串 "null"。
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.DeclareValueContext("Names", OwnerSelector.Class("Mugirl.Part"), "names");
            m.MapValue("Names", "Dead", null);
        });
        string result = SaveXml.Upgraded("<li Class=\"Mugirl.Part\"><names><li>Dead</li><li>Live</li></names></li>", tables);

        Assert.Equal("<li Class=\"Mugirl.Part\"><names><li IsNull=\"True\" /><li>Live</li></names></li>", result);
    }

    [Fact]
    public void RenamesDefNamesStoredAsStrings()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameDef("IncidentDef", "Mugirl_OldIncident", "Mugirl_NewIncident", "renamed");
            m.DeclareValueContext("PendingIncident", OwnerSelector.Class("Mugirl.Story"), "pendingIncident", ValueContextKind.Def, "IncidentDef");
        });
        string result = SaveXml.Upgraded("<li Class=\"Mugirl.Story\"><pendingIncident>Mugirl_OldIncident</pendingIncident></li>", tables);

        Assert.Equal("<li Class=\"Mugirl.Story\"><pendingIncident>Mugirl_NewIncident</pendingIncident></li>", result);
    }

    [Fact]
    public void MaterializesOldDefaultsOnlyWithPositiveLegacyEvidence()
    {
        string component = Fakes.Name<Fakes.NewComponent>();
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameType("Mugirl.OldComponent", typeof(Fakes.NewComponent), "renamed");
            m.MaterializeDefault(OwnerSelector.Class(typeof(Fakes.NewComponent)), "limit", "3", LegacyEvidence.LegacyClass, "default changed from 3 to 5");
            m.MaterializeDefault(OwnerSelector.Class("Mugirl.Tracker"), "enabled", "True", LegacyEvidence.LegacyLabel, "default changed", "legacyOnly");
        });
        string result = SaveXml.Upgraded(
            "<a Class=\"Mugirl.OldComponent\" /><b Class=\"" + component + "\" /><c Class=\"Mugirl.OldComponent\"><limit>7</limit></c>"
            + "<d Class=\"Mugirl.Tracker\"><legacyOnly>1</legacyOnly></d><e Class=\"Mugirl.Tracker\" />",
            tables);

        // b 已是新类型、e 没有旧版专有标签：没有旧版证据，缺失的标签表示新默认值，不能补写。
        Assert.Equal(
            "<a Class=\"" + component + "\"><limit>3</limit></a><b Class=\"" + component + "\" /><c Class=\"" + component + "\"><limit>7</limit></c>"
            + "<d Class=\"Mugirl.Tracker\"><legacyOnly>1</legacyOnly><enabled>True</enabled></d><e Class=\"Mugirl.Tracker\" />",
            result);
    }

    [Fact]
    public void MaterializesDefaultsForThingsWithLegacyDefsAndNeverDuplicatesARenamedLabel()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameDef("ThingDef", "Mugirl_OldCollar", "Mugirl_Collar", "renamed");
            m.MaterializeDefault(OwnerSelector.Path(OwnerSelector.Document("things"), "thing"), "lockCount", "1", LegacyEvidence.LegacyDef, "default depended on the def");
            m.RenameLabel(OwnerSelector.Path(OwnerSelector.Document("things"), "thing"), "locks", "lockCount", "renamed");
        });
        string result = SaveXml.Upgraded(
            "<things><thing><def>Mugirl_OldCollar</def><id>Mugirl_OldCollar1</id></thing><thing><def>Mugirl_OldCollar</def><id>Mugirl_OldCollar2</id><locks>4</locks></thing>"
            + "<thing><def>Steel</def><id>Steel3</id></thing></things>",
            tables);

        Assert.Equal(
            "<things><thing><def>Mugirl_OldCollar</def><id>Mugirl_Collar1</id><lockCount>1</lockCount></thing><thing><def>Mugirl_OldCollar</def><id>Mugirl_Collar2</id><lockCount>4</lockCount></thing>"
            + "<thing><def>Steel</def><id>Steel3</id></thing></things>",
            result);
    }
}

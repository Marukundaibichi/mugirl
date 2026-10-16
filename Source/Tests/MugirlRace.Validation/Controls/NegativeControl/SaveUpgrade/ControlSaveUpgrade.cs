using HarmonyLib;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// C.save-upgrade 负对照的升级清单（契约 K1，选择器与令牌用升级器 OwnerSelector 的写法）：负对照各违反一条规则（目标存档根错误、缺构造函数、
/// 链、名字复用、过期键、目标 Def 不存在、Def 名以数字结尾、冻结的 ManeuverDef、标签改名到不读的名字、已改名的标签仍在读、枚举映射到非成员、
/// 未经黄金样例证明的消费、过期消费、没有测试的步骤、独立文档类型改名而没有 GetBackCompatibleType 后缀、错误的存档戳）；正对照证明新名书写的
/// 选择器（Class、Path）、令牌、枚举、值上下文与默认值物化都被正确映射回旧名。
/// </summary>
public static class SaveUpgradeManifest
{
    public static string ToJson() => """
        {
          "manifestVersion": 2, "schema": 1, "stampLabel": "mugirlSaveSchema", "stampType": "Mugirl.Control.WrongStamp",
          "types": [
            { "old": "Mugirl.Control.Upgrade.OldComponent", "new": "Mugirl.Control.Upgrade.WrongRootTarget", "disposition": "rename", "policy": null, "module": "control", "reason": "control: wrong save root" },
            { "old": "Mugirl.Control.Upgrade.OldNoCtor", "new": "Mugirl.Control.Upgrade.NoCtorTarget", "disposition": "rename", "policy": null, "module": "control", "reason": "control: target without the (Game) constructor" },
            { "old": "Mugirl.Control.Upgrade.ChainA", "new": "Mugirl.Control.Upgrade.ChainB", "disposition": "rename", "policy": null, "module": "control", "reason": "control: uncomposed chain" },
            { "old": "Mugirl.Control.Upgrade.ChainB", "new": "Mugirl.Control.Upgrade.NewComponent", "disposition": "rename", "policy": null, "module": "control", "reason": "control: uncomposed chain" },
            { "old": "Mugirl.Control.Upgrade.Reuser", "new": "Mugirl.Control.Upgrade.LegacyOther", "disposition": "rename", "policy": null, "module": "control", "reason": "control: name reuse" },
            { "old": "Mugirl.Control.Upgrade.NeverExisted", "new": null, "disposition": "drop", "policy": "removeNode", "module": "control", "reason": "control: stale key" },
            { "old": "Mugirl.Control.Upgrade.OldOwner", "new": "Mugirl.Control.Upgrade.RenamedOwner", "disposition": "rename", "policy": null, "module": "control", "reason": "control: renamed label owner (positive)" },
            { "old": "Mugirl.Control.Upgrade.OldMode", "new": "Mugirl.Control.Upgrade.Mode", "disposition": "rename", "policy": null, "module": "control", "reason": "control: renamed enum (positive)" },
            { "old": "Mugirl.Control.Upgrade.OldScenPart", "new": "Mugirl.Control.Upgrade.NewScenPart", "disposition": "rename", "policy": null, "module": "control", "reason": "control: side-document type renamed without the GetBackCompatibleType postfix" }
          ],
          "defs": [
            { "root": "ThingDef", "legacyRoot": "ControlOldRoot", "old": "ControlOldRootDef", "new": "Mugirl_Control_NewManeuver", "disposition": "rename", "module": "control", "reason": "control: target checked in current root, not arbitrary matching name" },
            { "root": "ThingDef", "legacyRoot": "ControlGoodOldRoot", "old": "ControlGoodOldRootDef", "new": "Mugirl_Control_Duplicate", "disposition": "rename", "module": "control", "reason": "control: old root scopes corpus, current root scopes target" },
            { "root": "ThingDef", "old": "Mugirl_Control_OldThing", "new": "Mugirl_Control_NoSuchThing", "disposition": "rename", "policy": null, "module": "control", "reason": "control: missing target def" },
            { "root": "ThingDef", "old": "Mugirl_Control_OldGun", "new": "Mugirl_Control_Thing2", "disposition": "rename", "policy": null, "module": "control", "reason": "control: target ends in a digit" },
            { "root": "ManeuverDef", "old": "Mugirl_Control_OldManeuver", "new": "Mugirl_Control_NewManeuver", "disposition": "rename", "policy": null, "module": "control", "reason": "control: frozen verb-ID def renamed without a step" }
          ],
          "labels": [
            { "owner": "Class(Mugirl.Control.Upgrade.LabelOwner)", "old": "oldName", "new": "neverRead", "disposition": "rename", "step": null, "module": "control", "reason": "control: target label never read" },
            { "owner": "Class(Mugirl.Control.Upgrade.LabelOwner)", "old": "stillRead", "new": "kept", "disposition": "rename", "step": null, "module": "control", "reason": "control: legacy label still read" },
            { "owner": "Class(Mugirl.Control.Upgrade.RenamedOwner)", "old": "legacyCount", "new": "count", "disposition": "rename", "step": null, "module": "control", "reason": "control: label of a renamed owner, selected by the new name (positive)" },
            { "owner": "Path(Class(Mugirl.Control.Upgrade.RenamedOwner), slots/li)", "old": "oldSlotLabel", "new": "slotLabel", "disposition": "rename", "step": null, "module": "control", "reason": "control: nested object without Class reached through the schema graph (positive)" }
          ],
          "enums": [
            { "enum": "Mugirl.Control.Upgrade.ControlState", "old": "Paused", "new": "Stopped", "module": "control" },
            { "enum": "Mugirl.Control.Upgrade.Mode", "old": "Legacy", "new": "Current", "module": "control" }
          ],
          "values": [
            { "context": "controlSignals", "old": "OldSignal", "new": "NewSignal", "module": "control" },
            { "context": "controlKeys", "old": "Mugirl.Control.Legacy.SavedKey", "new": null, "module": "control" }
          ],
          "defaults": [ { "owner": "Class(Mugirl.Control.Upgrade.LabelOwner)", "label": "drift2", "oldDefault": "4", "module": "control" } ],
          "steps": [
            { "id": "ControlStepLacking", "phase": "dom", "module": "control", "consumes": ["type:Mugirl.Control.Upgrade.ConsumedType"], "reason": "control: before.xml lacks the name" },
            { "id": "ControlStepNoGolden", "phase": "dom", "module": "control", "consumes": ["type:Mugirl.Control.Upgrade.ConsumedToo"], "reason": "control: no golden fixture" },
            { "id": "ControlStepGood", "phase": "dom", "module": "control", "consumes": ["label:Class(Mugirl.Control.Upgrade.RenamedOwner)/legacyFlag"], "reason": "control: verified label consumption in the upgrader's token format (positive)" },
            { "id": "ControlStepWrongOwner", "phase": "dom", "module": "control", "consumes": ["label:Class(Mugirl.Control.Upgrade.RenamedOwner)/scopeProbe"], "reason": "control: the same label exists only under another owner" },
            { "id": "ControlStepStale", "phase": "dom", "module": "control", "consumes": ["label:Class(Mugirl.Control.Upgrade.RenamedOwner)/neverWritten"], "reason": "control: consumption of a label no legacy build wrote" },
            { "id": "ControlPostLoadUntested", "phase": "B", "module": "control", "consumes": [], "reason": "control: post-load step that no test mentions" },
            { "id": "ControlPostLoadTested", "phase": "C", "module": "control", "consumes": [], "reason": "control: post-load step that a test mentions (positive)" }
          ],
          "jobPolicies": [
            { "type": "Mugirl.Control.Upgrade.PreservedDriver", "policy": "preserve", "module": "control", "reason": "control: existing concrete driver" },
            { "type": "Mugirl.Control.Upgrade.MissingPreservedDriver", "policy": "preserve", "module": "control", "reason": "control: missing target is not a preserve disposition" }
          ], "lordPolicies": [],
          "settings": { "fileClass": { "old": "Mugirl.Other", "new": "Mugirl.Other" }, "labels": [], "schema": 1 },
          "moogirlEra": { "typePrefixes": [], "defPatterns": ["MooGirl*", "MuGirl*"] }
        }
        """;
}

/// <summary>
/// 正对照：升级层可以给存档读取内部打补丁、读 curXmlParent、并以字符串保存旧名（B.save-confinement、B.architecture save-internals、
/// C.translation-keys 的键形字面量规则都不报告它）。它补的是 BackCompatibleDefName 而不是 GetBackCompatibleType，所以独立文档类型的改名
/// 缺少后者（负对照 OldScenPart）。
/// </summary>
[HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.BackCompatibleDefName))]
[MugirlPatch(PatchModule.Core, PatchRisk.Low, "control")]
public static class ControlUpgradeHook
{
    public static readonly string[] LegacyNames = ["Mugirl.Control.Upgrade.Vanished", "Mugirl.Control.Legacy.SavedKey", "Mugirl_Control_OldThing"];

    // 正对照：升级层的旧名表不受 C.forbidden-patterns 约束（Gloden 是内容中禁止的旧拼写）。
    public static readonly string LegacyTypo = "Gloden";

    public static void Postfix()
    {
    }

    public static string CurrentNode() => Scribe.loader.curXmlParent?.Name;
}

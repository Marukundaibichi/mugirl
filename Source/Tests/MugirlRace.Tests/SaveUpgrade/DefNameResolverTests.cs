using System.Collections.Generic;
using Mugirl.SaveUpgrade;
using RimWorld;
using Verse;

namespace Mugirl.Tests.SaveUpgrade;

/// <summary>BackCompatibleDefName 后缀的判定：根类型规范化、按原始参数查表、只在当前结果缺失时覆盖。</summary>
public sealed class DefNameResolverTests
{
    [Fact]
    public void NormalizesDefTypesToTheirDatabaseRoot()
    {
        Assert.Equal(typeof(ThingDef), DefRoots.DatabaseRoot(typeof(ThingDef)));
        Assert.Equal(typeof(ThingDef), DefRoots.DatabaseRoot(typeof(RaceDefLike)));
        Assert.Equal(typeof(HediffDef), DefRoots.DatabaseRoot(typeof(HediffDef)));
        Assert.Null(DefRoots.DatabaseRoot(typeof(Def)));
        Assert.Null(DefRoots.DatabaseRoot(typeof(BuildableDef)));
        Assert.Null(DefRoots.DatabaseRoot(typeof(string)));
    }

    [Fact]
    public void RenamesOnlyWhenTheCurrentResultIsMissingFromTheRootDatabase()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.RenameDef("ThingDef", "Mugirl_OldGun", "Mugirl_Gun_Rifle", "renamed");
            m.RenameDef("HediffDef", "Mugirl_Shared", "Mugirl_SharedHediff", "renamed hediff only");
        });
        var existing = new HashSet<(Type, string)> { (typeof(ThingDef), "Mugirl_Shared"), (typeof(ThingDef), "ThirdParty_Gun") };
        bool Exists(Type root, string name) => existing.Contains((root, name));

        Assert.Equal("Mugirl_Gun_Rifle", DefNameResolver.Resolve(tables, typeof(RaceDefLike), "Mugirl_OldGun", "Mugirl_OldGun", Exists));
        Assert.Equal("Make_Mugirl_Gun_Rifle", DefNameResolver.Resolve(tables, typeof(RecipeDef), "Make_Mugirl_OldGun", "Make_Mugirl_OldGun", Exists));
        Assert.Equal("Mugirl_SharedHediff", DefNameResolver.Resolve(tables, typeof(HediffDef), "Mugirl_Shared", "Mugirl_Shared", Exists));

        // 同名 ThingDef 仍然存在、根类型不同、根是 Def、或其他转换器已给出有效名称：都不覆盖。
        Assert.Null(DefNameResolver.Resolve(tables, typeof(ThingDef), "Mugirl_Shared", "Mugirl_Shared", Exists));
        Assert.Null(DefNameResolver.Resolve(tables, typeof(HediffDef), "Mugirl_OldGun", "Mugirl_OldGun", Exists));
        Assert.Null(DefNameResolver.Resolve(tables, typeof(Def), "Mugirl_OldGun", "Mugirl_OldGun", Exists));
        Assert.Null(DefNameResolver.Resolve(tables, typeof(ThingDef), "Mugirl_OldGun", "ThirdParty_Gun", Exists));

        // 按原始参数查表：前面的转换器改出的名称同样不存在时仍然改名。
        Assert.Equal("Mugirl_Gun_Rifle", DefNameResolver.Resolve(tables, typeof(ThingDef), "Mugirl_OldGun", "Converted_Missing", Exists));
        Assert.Null(DefNameResolver.Resolve(tables, typeof(ThingDef), "Unknown", "Unknown", Exists));
    }

    [Fact]
    public void ReportsDroppedDefsOfTheRootDatabaseUnlessTheNameStillExists()
    {
        UpgradeTables tables = SaveXml.Tables(m =>
        {
            m.DropDef("ThingDef", "Mugirl_OldGun", DefDropPolicy.RemoveThings, "retired");
            m.DropDef("HediffDef", "Mugirl_OldBuff", DefDropPolicy.RemoveHediffs, "retired");
            m.RenameDef("ThingDef", "Mugirl_OldBow", "Mugirl_Bow", "renamed");
        });
        var existing = new HashSet<(Type, string)> { (typeof(HediffDef), "Mugirl_OldBuff") };
        bool Exists(Type root, string name) => existing.Contains((root, name));

        Assert.True(DefNameResolver.WasDropped(tables, typeof(RaceDefLike), "Mugirl_OldGun", Exists));
        Assert.True(DefNameResolver.WasDropped(tables, typeof(RecipeDef), "Make_Mugirl_OldGun", Exists));
        Assert.True(DefNameResolver.WasDropped(tables, typeof(ThingDef), "Corpse_Mugirl_OldGun", Exists));

        // 根类型不同、根是 Def、名称被第三方重新定义、或只是改名：都不算删除。
        Assert.False(DefNameResolver.WasDropped(tables, typeof(HediffDef), "Mugirl_OldGun", Exists));
        Assert.False(DefNameResolver.WasDropped(tables, typeof(Def), "Mugirl_OldGun", Exists));
        Assert.False(DefNameResolver.WasDropped(tables, typeof(HediffDef), "Mugirl_OldBuff", Exists));
        Assert.False(DefNameResolver.WasDropped(tables, typeof(ThingDef), "Mugirl_OldBow", Exists));
    }

    private sealed class RaceDefLike : ThingDef
    {
    }
}

using System;
using System.Linq;
using System.Xml;
using HarmonyLib;
using Mugirl.Features.AdvancedArmor;
using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class NeuralArmorRuntimeChecks
    {
        internal static void Run(Pawn pawn, Pawn enemy, Action<string, bool> check)
        {
            check("standalone shoulders and their crafting recipe are absent",
                DefDatabase<ThingDef>.GetNamedSilentFail("Mugirl_NeuralCombatShoulders") == null
                && DefDatabase<RecipeDef>.GetNamedSilentFail("Make_Mugirl_NeuralCombatShoulders") == null);
            ThingDef armorDef = Mugirl_DefOf.Mugirl_NeuralCombatArmor;
            check("body armor covers shoulders", armorDef.apparel.bodyPartGroups.Any(group => group.defName == "Shoulders"));
            Apparel armor = (Apparel)ThingMaker.MakeThing(armorDef);
            CompNeuralShoulderDeflection comp = armor.TryGetComp<CompNeuralShoulderDeflection>();
            check("body armor retains consciousness comp and gains deflection comp",
                comp != null && armor.TryGetComp<CompNeuralCombatArmor>() != null);
            CompProperties_NeuralShoulderDeflection original = (CompProperties_NeuralShoulderDeflection)comp.props;
            check("body armor has two independent 12-percent deflection attempts",
                original.shoulderCount == 2 && Math.Abs(original.deflectionChancePerShoulder - 0.12f) < 0.00001f);
            pawn.apparel.Wear(armor);
            // 只替换该测试实例的参数以确定触发；生产 Def 的 12% 概率保持不变。
            comp.props = new CompProperties_NeuralShoulderDeflection { deflectionChancePerShoulder = 1f, shoulderCount = 2 };
            try
            {
                ThingDef rifle = DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle");
                float injuryBefore = pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity);
                int hitPointsBefore = armor.HitPoints;
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 25f, 10f, -1f, enemy, null, rifle));
                check("wearing only body armor deflects bullets before pawn injury and consumes body armor durability",
                    pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity) == injuryBefore
                    && armor.HitPoints < hitPointsBefore);
                check("explosions do not trigger deflection", !armor.CheckPreAbsorbDamage(new DamageInfo(DamageDefOf.Bomb, 25f, 1f, -1f, enemy, null, rifle)));
                check("melee attacks do not trigger deflection", !armor.CheckPreAbsorbDamage(new DamageInfo(DamageDefOf.Stab, 25f, 1f, -1f, enemy, null, pawn.equipment.Primary.def)));
                pawn.apparel.Remove(armor);
                check("unworn body armor does not deflect", !armor.CheckPreAbsorbDamage(new DamageInfo(DamageDefOf.Bullet, 25f, 1f, -1f, enemy, null, rifle)));
            }
            finally { comp.props = original; }
            CheckMigration(check);
        }

        private static void CheckMigration(Action<string, bool> check)
        {
            XmlDocument document = new XmlDocument();
            document.LoadXml("<game><maps><li><things><thing Class='Apparel'><def>Mugirl_NeuralCombatShoulders</def><id>Mugirl_NeuralCombatShoulders1</id><health>280</health></thing>"
                + "<thing Class='UnfinishedThing'><def>UnfinishedTechArmor</def><id>UnfinishedTechArmor5</id><recipe>Make_Mugirl_NeuralCombatShoulders</recipe></thing>"
                + "<thing Class='Pawn'><apparel><wornApparel><innerList>"
                + "<li Class='Apparel'><def>Mugirl_NeuralCombatShoulders</def><id>Mugirl_NeuralCombatShoulders2</id></li>"
                + "<li Class='Apparel'><def>Mugirl_NeuralCombatArmor</def><id>Mugirl_NeuralCombatArmor3</id><health>432</health></li>"
                + "</innerList></wornApparel><lockedApparel><li>Thing_Mugirl_NeuralCombatShoulders2</li><li>Thing_Mugirl_NeuralCombatArmor3</li></lockedApparel></apparel>"
                + "<jobs><curJob><targetA>Thing_Mugirl_NeuralCombatShoulders1</targetA><bill>Bill_Make_Mugirl_NeuralCombatShoulders_6</bill></curJob></jobs></thing>"
                + "<thing><def>Mugirl_NeuralCombatShouldersOther</def><id>Other4</id></thing></things>"
                + "<bills><li><recipe>Make_Mugirl_NeuralCombatShoulders</recipe><loadID>6</loadID></li><li><recipe>Make_Mugirl_NeuralCombatArmor</recipe></li></bills>"
                + "<filter><defs><li>Mugirl_NeuralCombatShoulders</li><li>Mugirl_NeuralCombatArmor</li></defs></filter>"
                + "<note>Mugirl_NeuralCombatShoulders</note></li></maps></game>");
            check("legacy migration removes only the two old shoulder items and unfinished shoulder work", NeuralShoulderSaveMigration.RemoveLegacyShoulders(document.DocumentElement) == 3);
            check("legacy migration preserves existing body armor and its durability",
                document.SelectSingleNode("//innerList/li/def")?.InnerText == "Mugirl_NeuralCombatArmor"
                && document.SelectSingleNode("//innerList/li/health")?.InnerText == "432");
            check("legacy migration clears removed-item references, bills and filters",
                document.SelectNodes("//lockedApparel/li").Count == 1 && document.SelectSingleNode("//targetA")?.InnerText == "null"
                && document.SelectSingleNode("//curJob/bill")?.InnerText == "null"
                && document.SelectNodes("//bills/li").Count == 1 && document.SelectNodes("//filter/defs/li").Count == 1);
            check("legacy migration preserves near names and unrelated text",
                document.SelectSingleNode("//thing[id='Other4']/def")?.InnerText == "Mugirl_NeuralCombatShouldersOther"
                && document.SelectSingleNode("//note")?.InnerText == "Mugirl_NeuralCombatShoulders");
            string expected = document.OuterXml;
            check("legacy migration is idempotent and ignores unrelated roots",
                NeuralShoulderSaveMigration.RemoveLegacyShoulders(document.DocumentElement) == 0
                && NeuralShoulderSaveMigration.RemoveLegacyShoulders(document.SelectSingleNode("//maps")) == 0
                && NeuralShoulderSaveMigration.RemoveLegacyShoulders(null) == 0 && document.OuterXml == expected);
            var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(Game), "ExposeSmallComponents"));
            check("legacy migration is registered before game deserialization",
                patches != null && patches.Prefixes.Count(p => p.PatchMethod.DeclaringType == typeof(NeuralShoulderSaveMigration)) == 1);
        }
    }
}

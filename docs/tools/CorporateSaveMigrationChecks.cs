// Optional checks for the narrow legacy validation-component migration.
using System;
using System.Linq;
using System.Xml;
using HarmonyLib;
using Verse;

namespace Mugirl
{
    internal static class CorporateSaveMigrationChecks
    {
        internal static void Run(Action<string, bool> check)
        {
            XmlDocument fixture = new XmlDocument { PreserveWhitespace = true };
            fixture.LoadXml("<savegame><meta><version>1.6</version></meta><game>"
                + "<components><li Class=\"Mugirl.CorporateNetwork\"><balance>12345</balance></li>"
                + "<li Class=\"Mugirl.CorporateRuntimeValidation\"><validationPhase>1</validationPhase></li>"
                + "<li Class=\"OtherMod.GameComponent\"><value>keep</value></li>"
                + "<li Class=\"Mugirl.CorporateVisualValidation\" />"
                + "<li Class=\"Mugirl.CorporateVisualValidationOther\" />"
                + "<li Class=\"CorporateRuntimeValidation\" />"
                + "<li Class=\"mugirl.CorporateRuntimeValidation\" />"
                + "<li Class=\"Mugirl.CorporateRuntimeValidation, OtherAssembly\" />"
                + "<record Class=\"Mugirl.CorporateVisualValidation\" /></components>"
                + "<world><components><li Class=\"Mugirl.CorporateVisualValidation\" /></components></world>"
                + "</game></savegame>");
            XmlDocument expected = (XmlDocument)fixture.CloneNode(true);
            XmlNode expectedComponents = expected.SelectSingleNode("/savegame/game/components");
            expectedComponents.RemoveChild(expectedComponents.ChildNodes[3]);
            expectedComponents.RemoveChild(expectedComponents.ChildNodes[1]);

            int removed = CorporateValidationSaveMigration.RemoveLegacyComponents(fixture.SelectSingleNode("/savegame/game"));
            check("save migration removes precisely the two obsolete validation drivers", removed == 2);
            check("save migration preserves all other components, payload, near names and nested worlds",
                fixture.OuterXml == expected.OuterXml);
            check("save migration is idempotent",
                CorporateValidationSaveMigration.RemoveLegacyComponents(fixture.SelectSingleNode("/savegame/game")) == 0
                && fixture.OuterXml == expected.OuterXml);
            check("save migration ignores absent and unrelated XML roots",
                CorporateValidationSaveMigration.RemoveLegacyComponents(null) == 0
                && CorporateValidationSaveMigration.RemoveLegacyComponents(fixture.DocumentElement) == 0
                && CorporateValidationSaveMigration.RemoveLegacyComponents(fixture.SelectSingleNode("/savegame/game/world")) == 0
                && fixture.OuterXml == expected.OuterXml);

            XmlDocument missing = new XmlDocument();
            missing.LoadXml("<game><world /></game>");
            check("save migration accepts a game with no serialized components",
                CorporateValidationSaveMigration.RemoveLegacyComponents(missing.DocumentElement) == 0);
            check("validation drivers cannot be automatically instantiated or saved as GameComponents",
                !typeof(GameComponent).IsAssignableFrom(typeof(CorporateRuntimeValidation))
                && !typeof(GameComponent).IsAssignableFrom(typeof(CorporateVisualValidation)));
            var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(Game), "ExposeSmallComponents"));
            check("save migration prefix is registered on the engine's pre-deserialization boundary",
                patches != null && patches.Prefixes.Count(p => p.PatchMethod.DeclaringType == typeof(CorporateValidationSaveMigration)) == 1);
        }
    }
}

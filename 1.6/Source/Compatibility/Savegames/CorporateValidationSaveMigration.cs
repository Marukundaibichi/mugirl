using System.Xml;
using HarmonyLib;
using Verse;

namespace Mugirl
{
    // Older validation builds accidentally registered their inactive drivers as
    // GameComponents. Remove only those two obsolete records before Scribe tries
    // to instantiate them. This edits the in-memory load tree, never the save file.
    [HarmonyPatch(typeof(Game), "ExposeSmallComponents")]
    internal static class CorporateValidationSaveMigration
    {
        private static void Prefix()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                RemoveLegacyComponents(Scribe.loader?.curXmlParent);
        }

        internal static int RemoveLegacyComponents(XmlNode gameNode)
        {
            if (gameNode == null || gameNode.Name != "game") return 0;
            XmlNode components = gameNode["components"];
            if (components == null) return 0;

            int removed = 0;
            for (XmlNode node = components.LastChild; node != null;)
            {
                XmlNode previous = node.PreviousSibling;
                string className = node.Attributes?["Class"]?.Value;
                if (node.Name == "li" && (className == "Mugirl.CorporateRuntimeValidation"
                    || className == "Mugirl.CorporateVisualValidation"))
                {
                    components.RemoveChild(node);
                    removed++;
                }
                node = previous;
            }
            return removed;
        }
    }
}

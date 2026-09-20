using System.Collections.Generic;
using System.Linq;
using System.Xml;
using HarmonyLib;
using Verse;

namespace Mugirl
{
    // 肩甲已并入主甲。仅清理内存中的旧肩甲和相关订单，原存档文件不改写。
    [HarmonyPatch(typeof(Game), "ExposeSmallComponents")]
    internal static class NeuralShoulderSaveMigration
    {
        private const string RemovedDef = "Mugirl_NeuralCombatShoulders";
        private const string RemovedRecipe = "Make_Mugirl_NeuralCombatShoulders";

        private static void Prefix()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                RemoveLegacyShoulders(Scribe.loader?.curXmlParent);
        }

        internal static int RemoveLegacyShoulders(XmlNode gameNode)
        {
            if (gameNode == null || gameNode.Name != "game") return 0;
            int removed = 0;
            HashSet<string> removedReferences = new HashSet<string>();
            foreach (XmlNode node in gameNode.SelectNodes(".//*[def='" + RemovedDef + "' or recipe='" + RemovedRecipe + "']").Cast<XmlNode>().ToList())
            {
                // Thing 存档节点必须带 id，避免误删仅引用 ThingDef 的其他数据容器。
                string id = node["id"]?.InnerText;
                if (string.IsNullOrEmpty(id) || (node.Name != "li" && node.Name != "thing")) continue;
                removedReferences.Add(id);
                removedReferences.Add("Thing_" + id);
                node.ParentNode.RemoveChild(node);
                removed++;
            }
            foreach (XmlNode bill in gameNode.SelectNodes(".//li[recipe='" + RemovedRecipe + "']").Cast<XmlNode>().ToList())
            {
                string loadId = bill["loadID"]?.InnerText;
                if (!string.IsNullOrEmpty(loadId)) removedReferences.Add("Bill_" + RemovedRecipe + "_" + loadId);
                bill.ParentNode.RemoveChild(bill);
            }

            foreach (XmlNode node in gameNode.SelectNodes(".//*[not(*)]").Cast<XmlNode>().ToList())
            {
                string value = node.InnerText;
                bool removedDefReference = (value == RemovedDef || value == RemovedRecipe)
                    && (node.Name == "li" || node.Name == "def" || node.Name == "thingDef" || node.Name == "recipe");
                if (!removedDefReference && !removedReferences.Contains(value)) continue;
                // 列表直接移除，单值引用改成 Scribe 的空引用，避免锁定服装和当前 Job 残留悬空引用。
                if (node.Name == "li") node.ParentNode.RemoveChild(node);
                else node.InnerText = "null";
            }
            return removed;
        }
    }
}

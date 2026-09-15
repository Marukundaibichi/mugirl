using AlienRace;
using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Mugirl
{
    // HAR edits these values directly without invalidating the pawn render tree.
    // Compare around its input handler so linked variants and colors refresh together.
    [HarmonyPatch(typeof(StylingStation), nameof(StylingStation.DoRaceTabs))]
    internal static class Harmony_StylingStationRefresh
    {
        internal sealed class AppearanceSnapshot
        {
            private readonly int[] variants;
            private readonly List<Color?> addonColors = new List<Color?>();
            private readonly Dictionary<string, Color[]> channels = new Dictionary<string, Color[]>();

            internal AppearanceSnapshot(AlienPartGenerator.AlienComp comp)
            {
                variants = comp.addonVariants.ToArray();
                foreach (var color in comp.addonColors)
                {
                    addonColors.Add(color.first);
                    addonColors.Add(color.second);
                }
                // HAR mutates color tuples in place; retaining their references misses edits.
                foreach (var channel in comp.ColorChannels)
                {
                    channels.Add(channel.Key, new[] { channel.Value.first, channel.Value.second });
                }
            }

            internal bool HasChanged(AlienPartGenerator.AlienComp comp)
            {
                if (variants.Length != comp.addonVariants.Count ||
                    addonColors.Count != comp.addonColors.Count * 2 ||
                    channels.Count != comp.ColorChannels.Count)
                {
                    return true;
                }
                for (int i = 0; i < variants.Length; i++)
                {
                    if (variants[i] != comp.addonVariants[i]) return true;
                }
                for (int i = 0; i < comp.addonColors.Count; i++)
                {
                    if (addonColors[i * 2] != comp.addonColors[i].first ||
                        addonColors[i * 2 + 1] != comp.addonColors[i].second) return true;
                }
                foreach (var channel in comp.ColorChannels)
                {
                    if (!channels.TryGetValue(channel.Key, out Color[] colors) ||
                        colors[0] != channel.Value.first || colors[1] != channel.Value.second) return true;
                }
                return false;
            }
        }

        internal static void Prefix(Pawn ___pawn, AlienPartGenerator.AlienComp ___alienComp,
            out AppearanceSnapshot __state)
        {
            __state = null;
            // No snapshots or graphics rebuilds on the recurring layout/repaint passes.
            Event current = Event.current;
            if (current == null || current.type == EventType.Layout || current.type == EventType.Repaint ||
                !MugirlIdentity.IsMugirlPawn(___pawn) || ___alienComp == null)
            {
                return;
            }
            __state = new AppearanceSnapshot(___alienComp);
        }

        internal static void Postfix(Pawn ___pawn, AlienPartGenerator.AlienComp ___alienComp,
            AppearanceSnapshot __state)
        {
            if (__state == null || ___alienComp == null || !__state.HasChanged(___alienComp)) return;

            ___pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(___pawn);
        }
    }
}

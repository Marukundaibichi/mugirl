using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Mugirl;
using RimWorld;
using UnityEngine;
using Verse;

[StaticConstructorOnStartup]
public static class PregnancyMoodProbe
{
    private static string root;
    private static int failures;

    static PregnancyMoodProbe()
    {
        if (!GenCommandLine.CommandLineArgPassed("mugirlPregnancyMoodProbe")) return;
        root = GenFilePaths.SaveDataFolderPath;
        if (!Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)).StartsWith("PregnancyMoodValidation-")) return;
        LongEventHandler.ExecuteWhenFinished(Run);
    }

    public static HediffComp_GiveRandomSituationalThought Make(bool mugirl, HediffDef def)
    {
        Pawn pawn = new Pawn { def = mugirl ? Mugirl_DefOf.Mugirl : ThingDefOf.Human };
        HediffWithComps parent = new HediffWithComps { def = def, pawn = pawn };
        return new HediffComp_GiveRandomSituationalThought
        {
            parent = parent,
            props = def.comps.OfType<HediffCompProperties_GiveRandomSituationalThought>().Single()
        };
    }

    private static void Run()
    {
        try
        {
            foreach (string method in new[] { "CompPostMake", "CompExposeData" })
            {
                var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(HediffComp_GiveRandomSituationalThought), method));
                Check(method + " patch registered", patches != null && patches.Postfixes.Any(p => p.owner == "MugirlMod.Mod"));
            }
            if (ModsConfig.BiotechActive)
            {
                var def = HediffDefOf.PregnancyMood;
                var options = def.comps.OfType<HediffCompProperties_GiveRandomSituationalThought>().Single().thoughtDefs.ToArray();
                bool mugirlAllPositive = true;
                bool humanNegative = false;
                var seen = new System.Collections.Generic.HashSet<string>();
                Rand.PushState(2192106);
                try
                {
                    for (int i = 0; i < 256; i++)
                    {
                        var mood = Make(true, def);
                        mood.CompPostMake();
                        mugirlAllPositive &= mood.selectedThought.stages[0].baseMoodEffect > 0;
                        seen.Add(mood.selectedThought.defName);
                        var human = Make(false, def);
                        human.CompPostMake();
                        humanNegative |= human.selectedThought.stages[0].baseMoodEffect < 0;
                    }
                }
                finally { Rand.PopState(); }
                Check("256 Mugirl new moods are positive", mugirlAllPositive);
                Check("both positive moods remain available", seen.SetEquals(new[] { "PregnancyMood_Up", "PregnancyMood_High" }));
                Check("human random moods still include negative states", humanNegative);
                Check("shared candidates unchanged", options.SequenceEqual(def.comps.OfType<HediffCompProperties_GiveRandomSituationalThought>().Single().thoughtDefs));
                foreach (var thought in options)
                {
                    foreach (bool mugirl in new[] { true, false })
                    {
                        var state = new PregnancyMoodProbeState(mugirl, thought);
                        string file = Path.Combine(root, "state.xml");
                        Scribe.saver.InitSaving(file, "PregnancyMoodProbeState");
                        Scribe_Deep.Look(ref state, "state");
                        Scribe.saver.FinalizeSaving();
                        Check("saving preserves " + mugirl + "/" + thought.defName, state.comp.selectedThought == thought);
                        state = null;
                        Scribe.loader.InitLoading(file);
                        Scribe_Deep.Look(ref state, "state");
                        Scribe.loader.FinalizeLoading();
                        Check("load " + mugirl + "/" + thought.defName,
                            mugirl && thought.stages[0].baseMoodEffect <= 0
                                ? state.comp.selectedThought.stages[0].baseMoodEffect > 0
                                : state.comp.selectedThought == thought);
                    }
                }
            }
            var negative = new ThoughtDef { defName = "ProbeNegative", stages = new System.Collections.Generic.List<ThoughtStage> { new ThoughtStage { baseMoodEffect = -5 } } };
            var unrelated = new HediffDef { defName = "ProbeUnrelated", comps = new System.Collections.Generic.List<HediffCompProperties> { new HediffCompProperties_GiveRandomSituationalThought { thoughtDefs = new System.Collections.Generic.List<ThoughtDef> { negative } } } };
            var untouched = Make(true, unrelated);
            untouched.CompPostMake();
            Check("unrelated random thoughts remain unchanged", untouched.selectedThought == negative);
            Check("Biotech fixture matches requested mode", ModsConfig.BiotechActive != GenCommandLine.CommandLineArgPassed("mugirlPregnancyNoBiotech"));
        }
        catch (Exception ex) { Check("exception: " + ex, false); }
        finally
        {
            File.WriteAllText(Path.Combine(root, "complete.txt"), (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures);
            Application.Quit();
        }
    }

    private static void Check(string label, bool pass)
    {
        if (!pass) failures++;
        File.AppendAllText(Path.Combine(root, "checks.txt"), (pass ? "PASS " : "FAIL ") + label + Environment.NewLine);
    }
}

public sealed class PregnancyMoodProbeState : IExposable
{
    private bool mugirl;
    public HediffComp_GiveRandomSituationalThought comp;
    public PregnancyMoodProbeState() { }
    public PregnancyMoodProbeState(bool isMugirl, ThoughtDef selected)
    {
        mugirl = isMugirl;
        comp = PregnancyMoodProbe.Make(mugirl, HediffDefOf.PregnancyMood);
        comp.selectedThought = selected;
    }
    public void ExposeData()
    {
        Scribe_Values.Look(ref mugirl, "mugirl");
        if (comp == null) comp = PregnancyMoodProbe.Make(mugirl, HediffDefOf.PregnancyMood);
        comp.CompExposeData();
    }
}

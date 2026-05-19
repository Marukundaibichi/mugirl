using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using UnityEngine.Diagnostics;
using Verse;
using Verse.AI;
using System.Text;

namespace MooGirl
{
    public class RopedMooMod : Mod
    {
        public RopedMooMod(ModContentPack content) : base(content)
        {
            new Harmony("RopedMoo").PatchAll();
            Log.Message("Roped loaded");
        }
    }
}

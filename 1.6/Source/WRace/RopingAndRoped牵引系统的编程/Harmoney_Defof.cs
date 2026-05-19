using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using UnityEngine.Diagnostics;
using Verse;
using Verse.AI;
using System.Text;
using System.Reflection;

namespace MooGirl
{
    [DefOf]
    public static class RopedMooDefs
    {
        static RopedMooDefs()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RopedMooDefs));
        }

        [DefAlias("JobDriver_RopeMoo")]
        public static JobDef RopeMoo;

        [DefAlias("JobDriver_RemoveRopeMoo")]
        public static JobDef RemoveRopeMoo;

    }
  
}

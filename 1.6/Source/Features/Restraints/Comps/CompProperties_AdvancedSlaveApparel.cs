using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl
{
    public class Comp_AdvancedSlaveApparel : ThingComp
    {
        public bool ParentIsCracked()
        {
            return !(parent is AdvancedSlaveApparel slave && slave.IsCracked());
        }
    }
}

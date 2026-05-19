using UnityEngine;
using Verse;

namespace MooGirl
{
    public class CompProperties_MooGirlMount : CompProperties
    {
        public int physiologicalTickInterval = 150;
        public int safetyCheckInterval = 250;
        public int turretTickInterval = 60;
        public int turretMinAimTicks = 15;
        public bool tickPhysiology = true;
        public bool autoDismount = true;
        public bool turretFireAtWillDefault = true;
        public float riderAltitudeOffset = 0.022f;
        public float northRiderAltitudeOffset = 0.04f;
        public float southRiderAltitudeOffset = -0.04f;
        public Vector3 northOffset = new Vector3(0f, 0f, 0.08f);
        public Vector3 eastOffset = new Vector3(-0.20f, 0f, 0.02f);
        public Vector3 southOffset = new Vector3(0f, 0f, 0.14f);
        public Vector3 westOffset = new Vector3(0.20f, 0f, 0.02f);

        public CompProperties_MooGirlMount()
        {
            compClass = typeof(Comp_MooGirlMount);
        }
    }
}

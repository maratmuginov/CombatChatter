using RimWorld;
using Verse;

namespace CombatChatter.Models
{
    public class ThingDef_CoronaBullet : ThingDef
    {
        public float AddHediffChance = 0.05f;
        public HediffDef HediffToAdd = HediffDefOf.Flu;
    }
}

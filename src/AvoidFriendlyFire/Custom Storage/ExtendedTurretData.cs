using Verse;

namespace AvoidFriendlyFire
{
    public class ExtendedTurretData : IExposable
    {
        public bool AvoidFriendlyFire = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref AvoidFriendlyFire, "AvoidFriendlyFire", false);
        }
    }
}

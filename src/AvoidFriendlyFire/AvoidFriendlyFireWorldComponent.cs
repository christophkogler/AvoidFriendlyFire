using RimWorld.Planet;
using Verse;

namespace AvoidFriendlyFire
{
    public class AvoidFriendlyFireWorldComponent : WorldComponent
    {
        public AvoidFriendlyFireWorldComponent(World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();

            var main = Main.Instance;
            if (main == null || Find.TickManager == null)
                return;

            main.OnWorldTick(Find.TickManager.TicksGame);
        }
    }
}

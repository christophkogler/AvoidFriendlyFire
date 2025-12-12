using System;
using System.Diagnostics;
using System.Text;
using Verse;

namespace AvoidFriendlyFire
{
    public enum PerfSection
    {
        WorldTick = 0,
        RemoveExpiredCones = 1,
        PawnStatusRemoveExpired = 2,
        TrackMapChange = 3,
        UpdateFireConeOverlay = 4,
        OverlayShouldUpdate = 5,
        OverlayBuildFireCone = 6,
        GetFireCone = 7,
        ComputeFireCone = 8,
        AddShootablePointsBetween = 9,
        CanHitTargetSafely = 10,
        GetOrCreateCachedFireCone = 11,
        Patch_Verb_CanHitTargetFrom = 12,
        Patch_AttackTargetFinder_BestAttackTarget = 13,
        Patch_Targeter_TargeterUpdate = 14,
    }

    public static class PerfMetrics
    {
        private const int TicksPerWindow = 60;

        private static bool _enabled;
        private static int _windowTicksObserved;
        private static int _windowStartTick;

        private static readonly long[] ElapsedStopwatchTicksBySection = new long[Enum.GetValues(typeof(PerfSection)).Length];
        private static readonly int[] CallCountBySection = new int[Enum.GetValues(typeof(PerfSection)).Length];

        private static readonly string[] SectionNames =
        {
            "WorldTick",
            "RemoveExpiredCones",
            "PawnStatusRemoveExpired",
            "TrackMapChange",
            "UpdateFireConeOverlay",
            "OverlayShouldUpdate",
            "OverlayBuildFireCone",
            "GetFireCone",
            "ComputeFireCone",
            "AddShootablePointsBetween",
            "CanHitTargetSafely",
            "GetOrCreateCachedFireCone",
            "Patch:Verb.CanHitTargetFrom",
            "Patch:AttackTargetFinder.BestAttackTarget",
            "Patch:Targeter.TargeterUpdate",
        };

        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!_enabled)
                ResetWindow();
        }

        public static bool Enabled => _enabled;

        public static Scope Measure(PerfSection section)
        {
            if (!_enabled)
                return default;

            return new Scope(section, Stopwatch.GetTimestamp());
        }

        public static void OnWorldTick(int currentTick)
        {
            if (!_enabled)
                return;

            if (_windowTicksObserved == 0)
                _windowStartTick = currentTick;

            _windowTicksObserved++;
            if (_windowTicksObserved < TicksPerWindow)
                return;

            LogWindow(currentTick);
            ResetWindow();
        }

        private static void LogWindow(int currentTick)
        {
            var tickWindowSize = _windowTicksObserved <= 0 ? 1 : _windowTicksObserved;
            var millisecondsPerStopwatchTick = 1000.0 / Stopwatch.Frequency;

            double totalMilliseconds = 0;
            for (var i = 0; i < ElapsedStopwatchTicksBySection.Length; i++)
                totalMilliseconds += ElapsedStopwatchTicksBySection[i] * millisecondsPerStopwatchTick;

            var builder = new StringBuilder(512);
            builder.Append("[AvoidFriendlyFire][Perf] ");
            builder.Append("ticks=");
            builder.Append(_windowStartTick);
            builder.Append("..");
            builder.Append(currentTick);
            builder.Append(" ");
            builder.Append("total=");
            builder.Append((totalMilliseconds / tickWindowSize).ToString("0.000"));
            builder.Append("ms/tick");

            for (var i = 0; i < ElapsedStopwatchTicksBySection.Length; i++)
            {
                var elapsedTicks = ElapsedStopwatchTicksBySection[i];
                var callCount = CallCountBySection[i];
                if (elapsedTicks == 0 && callCount == 0)
                    continue;

                var elapsedMilliseconds = elapsedTicks * millisecondsPerStopwatchTick;
                builder.Append("; ");
                builder.Append(SectionNames[i]);
                builder.Append('=');
                builder.Append((elapsedMilliseconds / tickWindowSize).ToString("0.000"));
                builder.Append("ms/tick");
                builder.Append(" (calls ");
                builder.Append(callCount);
                builder.Append(')');
            }

            Log.Message(builder.ToString());
        }

        private static void ResetWindow()
        {
            _windowTicksObserved = 0;
            _windowStartTick = 0;
            Array.Clear(ElapsedStopwatchTicksBySection, 0, ElapsedStopwatchTicksBySection.Length);
            Array.Clear(CallCountBySection, 0, CallCountBySection.Length);
        }

        public readonly struct Scope
        {
            private readonly bool _active;
            private readonly PerfSection _section;
            private readonly long _startTimestamp;

            public Scope(PerfSection section, long startTimestamp)
            {
                _active = true;
                _section = section;
                _startTimestamp = startTimestamp;
            }

            public void Dispose()
            {
                if (!_active)
                    return;

                var endTimestamp = Stopwatch.GetTimestamp();
                var elapsedTicks = endTimestamp - _startTimestamp;
                var sectionIndex = (int)_section;
                ElapsedStopwatchTicksBySection[sectionIndex] += elapsedTicks;
                CallCountBySection[sectionIndex]++;
            }
        }
    }
}


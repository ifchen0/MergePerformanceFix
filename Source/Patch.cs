using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MergePerformanceFix
{
    [StaticConstructorOnStartup]
    public static class Startup
    {
        static Startup()
        {
            new Harmony("ifchen0.mergeperformancefix").PatchAll();
        }
    }

    /// <summary>
    /// WorkGiver_Merge.JobOnThing walks every thing held by the storage group of the stack it was given, looking for
    /// another partial stack to merge into. With large linked storage groups that is thousands of things per call,
    /// and the work scanner calls it for every partial stack on the map. Any valid merge target is a partial,
    /// unforbidden stack of the same def in the same group, so it is always in ListerMergeables. Checks that list
    /// first and returns no job when it holds no such stack; otherwise runs the vanilla method unchanged, so the
    /// chosen target is the same.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_Merge), nameof(WorkGiver_Merge.JobOnThing))]
    public static class Patch_WorkGiver_Merge_JobOnThing
    {
        private static readonly Dictionary<ThingDef, List<Thing>> MergeablesByDef = new Dictionary<ThingDef, List<Thing>>();
        private static Map indexedMap;
        private static int indexedTick = -1;

        public static bool Prefix(Pawn pawn, Thing t, bool forced, ref Job __result)
        {
            // Forced orders and non-player haulers keep the vanilla path (forbidden checks are per faction).
            if (forced || pawn?.Map == null || pawn.Faction != Faction.OfPlayer || t == null)
                return true;
            if (t.stackCount == t.def.stackLimit)
                return true;
            ISlotGroup slotGroup = t.GetSlotGroup();
            if (slotGroup == null)
                return true;
            ISlotGroup group = slotGroup.StorageGroup ?? slotGroup;

            if (!MergeablesByDef.TryGetValue(t.def, out List<Thing> candidates) || IndexIsStale(pawn.Map))
            {
                RebuildIndex(pawn.Map);
                MergeablesByDef.TryGetValue(t.def, out candidates);
            }

            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    Thing other = candidates[i];
                    if (other == t || !other.Spawned || other.stackCount >= other.def.stackLimit)
                        continue;
                    ISlotGroup otherSlotGroup = other.GetSlotGroup();
                    if (otherSlotGroup != null && (otherSlotGroup.StorageGroup ?? otherSlotGroup) == group)
                        return true;
                }
            }

            __result = null;
            return false;
        }

        private static bool IndexIsStale(Map map) => indexedMap != map || indexedTick != Find.TickManager.TicksGame;

        private static void RebuildIndex(Map map)
        {
            if (!IndexIsStale(map))
                return;
            foreach (List<Thing> list in MergeablesByDef.Values)
                list.Clear();
            List<Thing> mergeables = map.listerMergeables.ThingsPotentiallyNeedingMerging();
            for (int i = 0; i < mergeables.Count; i++)
            {
                Thing thing = mergeables[i];
                if (!MergeablesByDef.TryGetValue(thing.def, out List<Thing> list))
                {
                    list = new List<Thing>();
                    MergeablesByDef[thing.def] = list;
                }
                list.Add(thing);
            }
            indexedMap = map;
            indexedTick = Find.TickManager.TicksGame;
        }
    }

    /// <summary>
    /// Before calling JobOnThing, the work scanner runs a reachability check on every partial stack in storage on the
    /// map. Stacks that have no other partial stack of the same def in the same storage group can never get a merge
    /// job, so they are dropped from the scan list here and never reach that check. The scanner is skipped entirely
    /// when no stack is left. The result is cached per tick and map, since every hauler asks for it.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_WorkGiver_Merge_ScanList
    {
        private static readonly List<Thing> Mergeable = new List<Thing>();
        private static readonly Dictionary<(ThingDef, ISlotGroup), int> CountByDefAndGroup = new Dictionary<(ThingDef, ISlotGroup), int>();
        private static Map cachedMap;
        private static int cachedTick = -1;

        [HarmonyPatch(typeof(WorkGiver_Merge), nameof(WorkGiver_Merge.PotentialWorkThingsGlobal))]
        [HarmonyPrefix]
        public static bool PotentialWorkThingsGlobal(Pawn pawn, ref IEnumerable<Thing> __result)
        {
            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer)
                return true;
            __result = MergeableFor(pawn.Map);
            return false;
        }

        [HarmonyPatch(typeof(WorkGiver_Merge), nameof(WorkGiver_Merge.ShouldSkip))]
        [HarmonyPrefix]
        public static bool ShouldSkip(Pawn pawn, ref bool __result)
        {
            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer)
                return true;
            __result = MergeableFor(pawn.Map).Count == 0;
            return false;
        }

        private static List<Thing> MergeableFor(Map map)
        {
            int ticksGame = Find.TickManager.TicksGame;
            if (cachedMap == map && cachedTick == ticksGame)
                return Mergeable;

            List<Thing> all = map.listerMergeables.ThingsPotentiallyNeedingMerging();
            CountByDefAndGroup.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                (ThingDef, ISlotGroup) key = KeyOf(all[i]);
                if (key.Item2 != null)
                    CountByDefAndGroup[key] = CountByDefAndGroup.TryGetValue(key, out int count) ? count + 1 : 1;
            }
            Mergeable.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                (ThingDef, ISlotGroup) key = KeyOf(all[i]);
                if (key.Item2 != null && CountByDefAndGroup[key] >= 2)
                    Mergeable.Add(all[i]);
            }
            cachedMap = map;
            cachedTick = ticksGame;
            return Mergeable;
        }

        private static (ThingDef, ISlotGroup) KeyOf(Thing thing)
        {
            ISlotGroup slotGroup = thing.Spawned ? thing.GetSlotGroup() : null;
            return (thing.def, slotGroup == null ? null : slotGroup.StorageGroup ?? slotGroup);
        }
    }
}

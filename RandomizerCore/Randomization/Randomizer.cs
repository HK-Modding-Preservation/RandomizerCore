﻿using RandomizerCore.Exceptions;
using RandomizerCore.Extensions;
using RandomizerCore.Logic;

namespace RandomizerCore.Randomization
{
    public class Randomizer
    {
        readonly LogicManager lm;
        readonly RandomizationStage[] stages;
        readonly IRandomizerSettings rs;
        readonly RandoContext ctx;
        readonly ProgressionManager pm;
        readonly Random rng;
        readonly RandoMonitor rm;
        readonly List<List<RandoPlacement>[]> stagedPlacements;

        public Randomizer(IRandomizerSettings rs, RandoContext ctx, RandomizationStage[] stages, RandoMonitor rm = null)
        {
            this.rs = rs;
            this.ctx = ctx;
            this.lm = ctx.LM;
            this.rm = rm;
            this.stages = stages;
            rng = new(rs.Seed);
            stagedPlacements = new();

            rs.Initialize(rng);
            pm = new(lm, rs, ctx);
        }

        public List<List<RandoPlacement>[]> Run()
        {
            while (true)
            {
                try
                {
                    PermuteAll();
                    for (int i = 0; i < stages.Length - 1; i++) RandomizeForward(i, State.Temporary);
                    Randomize(stages.Length - 1, State.Permanent);
                    for (int i = stages.Length - 2; i >= 0; i--) Rerandomize(i, State.Permanent);
                    break;
                }
                catch (OutOfLocationsException e)
                {
                    rm.SendEvent(RandoEventType.Error, e.ToString());
                    Reset();
                }
            }

            rm.SendEvent(RandoEventType.Validating);
            Validate();
            rm.SendEvent(RandoEventType.Finished);
            return stagedPlacements;
        }

        private void PermuteAll()
        {
            for (int i = 0; i < stages.Length; i++)
            {
                Permute(stages[i]);
            }
        }

        private void Permute(RandomizationStage stage)
        {
            RandomizationGroup[] groups = stage.groups;
            for (int i = 0; i < groups.Length; i++)
            {
                rng.PermuteInPlace(groups[i].Items, (i, p) => i.Priority = p);
                rng.PermuteInPlace(groups[i].Locations, (i, p) => i.Priority = p);
                groups[i].InvokeOnPermute(rng);

                groups[i].Items.StableSort((r, s) => s.Priority - r.Priority); // items are sorted in reverse since they will be loaded into a stack
                groups[i].Locations.StableSort((r, s) => r.Priority - s.Priority);
            }
        }

        private MainUpdater InitializeUpdater()
        {
            MainUpdater mu = new(lm);
            mu.AddPlacements(rs.GetVanillaPlacements());
            mu.AddPlacements(lm.Waypoints);
            if (ctx.transitionPlacements != null) mu.AddPlacements(ctx.transitionPlacements);
            if (ctx.itemPlacements != null) mu.AddPlacements(ctx.itemPlacements);
            return mu;
        }

        private void ResetStage(RandomizationStage stage)
        {
            foreach (RandomizationGroup group in stage.groups) ResetGroup(group);
        }

        private void ResetGroup(RandomizationGroup group)
        {
            foreach (IRandoItem item in group.Items) item.Placed = State.None;
            foreach (IRandoLocation location in group.Locations) location.Reachable = State.None;
        }

        // Randomizes the groups in the stage with the given index.
        // State can be Temporary or Permanent, or None if indeterminate.
        // All staged placements will be accounted for during randomization.
        // For stages of greater index, their items will be added directly to the ProgressionManager before randomization.
        // Finishes by adding a new entry to the stagedPlacements list.
        private void RandomizeForward(int index, State state)
        {
            RandomizationGroup[] groups = stages[index].groups;
            MainUpdater mu = InitializeUpdater();
            for (int i = 0; i < stagedPlacements.Count; i++)
            {
                foreach (List<RandoPlacement> m in stagedPlacements[i]) foreach (RandoPlacement n in m) mu.AddEntry(new PrePlacedItemUpdateEntry(n.Item, n.Location));
            }

            for (int i = index + 1; i < stages.Length; i++)
            {
                foreach (RandomizationGroup g in stages[i].groups) pm.Add(g.Items);
            }

            SphereBuilder sb = new(groups, pm, mu);
            sb.Advance();
            stagedPlacements.Add(stages[index].strategy.PlaceItems(stages[index], sb.Spheres, state));
            pm.Reset();
        }

        // Randomizes the groups in the stage with the given index.
        // State can be Temporary or Permanent, or None if indeterminate.
        // All staged placements will be accounted for during randomization.
        // No handling for stages of greater index.
        // Finishes by adding a new entry to the stagedPlacements list.
        private void Randomize(int index, State state)
        {
            RandomizationGroup[] groups = stages[index].groups;
            MainUpdater mu = InitializeUpdater();
            for (int i = 0; i < stagedPlacements.Count; i++)
            {
                foreach (List<RandoPlacement> m in stagedPlacements[i]) foreach (RandoPlacement n in m) mu.AddEntry(new PrePlacedItemUpdateEntry(n.Item, n.Location));
            }

            SphereBuilder sb = new(groups, pm, mu);
            sb.Advance();
            stagedPlacements.Add(stages[index].strategy.PlaceItems(stages[index], sb.Spheres, state));
            pm.Reset();
        }

        // Randomizes the groups in the stage with the given index.
        // State can be Temporary or Permanent, or None if indeterminate
        // Staged placements will be accounted for during randomization, except for the entry with the same index as the current stage which is ignored.
        // No handling for stages of greater index.
        // Finishes by overwriting the entry with the given index in stagedPlacements.
        private void Rerandomize(int index, State state)
        {
            ResetStage(stages[index]);
            RandomizationGroup[] groups = stages[index].groups;
            MainUpdater mu = InitializeUpdater();
            for (int i = 0; i < stagedPlacements.Count; i++)
            {
                if (i == index) continue;
                foreach (List<RandoPlacement> m in stagedPlacements[i]) foreach (RandoPlacement n in m) mu.AddEntry(new PrePlacedItemUpdateEntry(n.Item, n.Location));
            }

            SphereBuilder sb = new(groups, pm, mu);
            sb.Advance();
            stagedPlacements[index] = stages[index].strategy.PlaceItems(stages[index], sb.Spheres, state);
            pm.Reset();
        }

        private void Reset()
        {
            pm.Reset();
            stagedPlacements.Clear();
            foreach (RandomizationStage stage in stages) ResetStage(stage);
        }

        /// <summary>
        /// Tests the randomizer output and sends an exception if it is invalid.
        /// </summary>
        /// <exception cref="ValidationException"></exception>
        public void Validate()
        {
            Log("Beginning validation.");

            // First, check that each RandomizationGroup matches the output, by checking that the counts of items and locations by name match.
            Dictionary<string, int> nameCounts = new();
            for (int i = 0; i < stages.Length; i++)
            {
                RandomizationStage stage = stages[i];
                for (int j = 0; j < stage.groups.Length; j++)
                {
                    RandomizationGroup group = stage.groups[j];
                    List<RandoPlacement> ps = stagedPlacements[i][j];

                    if (group.Items.Length < ps.Count)
                    {
                        throw new ValidationException("Items deleted from final placement for randomization group " + group.Label);
                    }
                    else if (group.Items.Length > ps.Count)
                    {
                        throw new ValidationException("Too many items found in final placement for randomization group " + group.Label);
                    }
                    else
                    {
                        foreach (IRandoItem r in group.Items)
                        {
                            nameCounts.TryGetValue(r.Name, out int value);
                            nameCounts[r.Name] = value + 1;
                        }
                        foreach (RandoPlacement p in ps)
                        {
                            nameCounts.TryGetValue(p.Item.Name, out int value);
                            nameCounts[p.Item.Name] = value - 1;
                        }
                        if (nameCounts.Values.Any(i => i != 0)) throw new ValidationException("Improper item counts found in randomization group " + group.Label);
                        nameCounts.Clear();

                        foreach (IRandoLocation r in group.Locations)
                        {
                            nameCounts.TryGetValue(r.Name, out int value);
                            nameCounts[r.Name] = value + 1;
                        }
                        foreach (RandoPlacement p in ps)
                        {
                            nameCounts.TryGetValue(p.Location.Name, out int value);
                            nameCounts[p.Location.Name] = value - 1;
                        }
                        if (nameCounts.Values.Any(i => i != 0)) throw new ValidationException("Improper item counts found in randomization group " + group.Label);
                        nameCounts.Clear();
                    }
                }
            }

            pm.Reset();
            MainUpdater mu = InitializeUpdater();

            List<PrePlacedItemUpdateEntry> entries = new();
            foreach (List<RandoPlacement>[] arr in stagedPlacements)
            {
                foreach (List<RandoPlacement> ps in arr)
                {
                    foreach (RandoPlacement p in ps)
                    {
                        PrePlacedItemUpdateEntry e = new PrePlacedItemUpdateEntry(p.Item, p.Location);
                        entries.Add(e);
                        mu.AddEntry(e);
                        // TODO: update entry for coupled placement
                    }
                }
            }

            mu.Hook(pm);
            foreach (PrePlacedItemUpdateEntry e in entries)
            {
                if (!e.obtained) throw new ValidationException($"Unreachable item placement detected: {e.item.Name} at {e.location.Name}");
            }

            Log("Validation completed successfully!");

            Log();
            Log("Placements:");
            foreach (var l in stagedPlacements)
            {
                foreach (var m in l)
                {
                    foreach (var p in m)
                    {
                        Log($"({p.Item.Priority})  {{{p.Item.Name}}}   at   ({p.Location.Priority}) {{{p.Location.Name}}}");
                    }
                }
            }

            Log();
        }
    }
}

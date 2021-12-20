﻿using RandomizerCore.Exceptions;
using RandomizerCore.Extensions;

namespace RandomizerCore.Randomization
{
    /// <summary>
    /// Selector which proposes items from a RandomizationGroup by priority order.
    /// </summary>
    public class GroupItemSelector
    {
        public bool Finished { get; private set; } = false;

        readonly Stack<IRandoItem> unusedItems;
        readonly Stack<IRandoItem> proposedItems;
        readonly Stack<IRandoItem> rejectedItems;
        public readonly bool coupled;
        public readonly string label;
        List<IRandoItem> acceptedItems;
        List<IRandoItem> discardedItems;

        public bool TryGetNextProposalPriority(out float priority)
        {
            if (_cap > 0)
            {
                AdvanceToNextProposal();
                if (unusedItems.TryPeek(out IRandoItem t))
                {
                    priority = t.Priority;
                    return true;
                }
            }

            priority = default;
            return false;
        }

        public GroupItemSelector(RandomizationGroup group)
        {
            coupled = group is CoupledRandomizationGroup;
            label = group.Label;
            unusedItems = new Stack<IRandoItem>(group.Items);
            proposedItems = new Stack<IRandoItem>(unusedItems.Count);
            rejectedItems = new Stack<IRandoItem>(unusedItems.Count);
            acceptedItems = new List<IRandoItem>();
            discardedItems = new();

            if (unusedItems.Count == 0) Finished = true; 
        }

        private int _cap = 0;
        public void IncrementCap(int amount)
        {
            if (_cap + amount < 0) throw new OutOfLocationsException($"Tried to set cap for items of group {label} to negative value:\n" +
                $"Cap {_cap} was incremented by {amount}.");
            _cap += amount;
        }

        /// <summary>
        /// Detects coupled items which must be discarded.
        /// <br/>An element of a coupled group has item and location behavior. If it becomes reachable before it is placed, it is slated into the locations of a sphere of the dual group, and can no longer be placed.
        /// </summary>
        public bool ShouldDiscard(IRandoItem t) => coupled && ((IRandoCouple)t).Reachable == State.Permanent;

        public IEnumerable<IRandoItem> GetAcceptedItems() => acceptedItems;
        public IEnumerable<IRandoItem> GetProposedItems() => proposedItems;

        private void Discard(IRandoItem t)
        {
            discardedItems.Add(t);
        }

        private void AdvanceToNextProposal()
        {
            if (_cap <= 0) return;
            while (unusedItems.TryPeek(out IRandoItem t) && ShouldDiscard(t))
            {
                unusedItems.Pop();
                Discard(t);
            }
        }

        public bool TryProposeNext(out IRandoItem t)
        {
            if (_cap > 0)
            {
                AdvanceToNextProposal();
                if (unusedItems.TryPop(out t))
                {
                    t.Placed = State.Temporary;
                    proposedItems.Push(t);
                    return true;
                }
            }

            t = default;
            return false;
        }

        public bool TryRecallLast(out IRandoItem t)
        {
            while (proposedItems.TryPeek(out t))
            {
                return true;
            }
            return false;
        }

        public void AcceptLast()
        {
            while (proposedItems.TryPop(out IRandoItem t))
            {
                t.Placed = State.Permanent;
                acceptedItems.Add(t);
                return;
            }
            throw new InvalidOperationException("AcceptLast called with no valid proposed transitions.");
        }

        public void UnacceptAll()
        {
            while (acceptedItems.Count > 0)
            {
                IRandoItem t = acceptedItems.Pop();
                t.Placed = State.Temporary;
                proposedItems.Push(t);
            }
        }

        public void RejectCurrentAndUnacceptAll()
        {
            IRandoItem r = proposedItems.Pop();
            UnacceptAll();
            r.Placed = State.None;
            rejectedItems.Push(r);
        }

        public void RejectLast()
        {
            while (proposedItems.TryPop(out IRandoItem t))
            {
                t.Placed = State.None;
                rejectedItems.Push(t);
                return;
            }
            throw new InvalidOperationException("RejectLast called with no valid proposed transitions.");
        }

        /// <summary>
        /// Outputs list of accepted items. Moves all rejected items to proposed items and starts new acccepted item list.
        /// </summary>
        public void FinishAccepting(out List<IRandoItem> newItems)
        {
            newItems = acceptedItems;
            acceptedItems = new List<IRandoItem>();
            while (rejectedItems.TryPop(out IRandoItem item)) proposedItems.Push(item);
            while (proposedItems.TryPop(out IRandoItem item))
            {
                item.Placed = State.None;
                unusedItems.Push(item);
            }
            if (unusedItems.Count == 0) Finished = true;
            IncrementCap(-newItems.Count);
        }

        /// <summary>
        /// Outputs all items which have not yet been accepted.
        /// </summary>
        /// <exception cref="InvalidOperationException">There are uncollected accepted items.</exception>
        public void Finish(out List<IRandoItem> remainingItems)
        {
            if (acceptedItems.Count != 0) throw new InvalidOperationException("ItemSelector.Finish called with uncollected accepted items!");
            while (rejectedItems.TryPop(out IRandoItem r)) proposedItems.Push(r);
            while (proposedItems.TryPop(out IRandoItem p)) unusedItems.Push(p);
            remainingItems = new();
            while (unusedItems.TryPop(out IRandoItem t))
            {
                if (ShouldDiscard(t)) Discard(t);
                else remainingItems.Add(t);
            }

            foreach (var t in remainingItems) t.Placed = State.Permanent;
            Finished = true;
            IncrementCap(-remainingItems.Count);
        }

        /// <summary>
        /// Returns decoupled targets which are reachable but not placed.
        /// </summary>
        public void CollectDiscardedTransitions(out List<IRandoItem> discard)
        {
            discard = discardedItems;
            discardedItems = new();
        }
    }
}
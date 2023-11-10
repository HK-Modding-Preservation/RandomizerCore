﻿using RandomizerCore.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RandomizerCore.LogicItems
{
    public sealed record FirstOfItem(string Name, IEnumerable<LogicItem> NestedItems) : LogicItem(Name), IConditionalItem
    {
        public override void AddTo(ProgressionManager pm)
        {
            LogicItem? firstItemWithEffect = NestedItems.FirstOrDefault(i => i is not IConditionalItem ci 
                || ci.CheckForEffect(pm));
            if (firstItemWithEffect != null)
            {
                firstItemWithEffect.AddTo(pm);
            }
        }

        public override IEnumerable<Term> GetAffectedTerms() => NestedItems.SelectMany(i => i.GetAffectedTerms());

        public bool CheckForEffect(ProgressionManager pm) => NestedItems.Any(i => i is not IConditionalItem ci 
            || ci.CheckForEffect(pm));
    }
}

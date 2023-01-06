﻿using Newtonsoft.Json;
using RandomizerCore.Json;
using RandomizerCore.Logic.StateLogic;
using RandomizerCore.StringLogic;
using System.ComponentModel;

namespace RandomizerCore.Logic
{
    /// <summary>
    /// A <see cref="LogicDef"/> which stores its contents in disjunctive normal form.
    /// <br/>Preferred for operations which need to inspect the successful subexpression after evaluation, such as most state operations.
    /// </summary>
    public class DNFLogicDef : StateLogicDef
    {
        private readonly Clause[] clauses;
        private readonly LogicManager lm;
        private Dictionary<int, List<Clause>> termClauseLookup;

        internal class Clause
        {
            public readonly int[] logic;
            public readonly int[] stateLogic;
            public readonly int stateProvider;
            private StateLogicDef? parent;

            public Clause(int[] logic, int[] stateLogic, int stateProvider)
            {
                this.logic = logic;
                this.stateLogic = stateLogic;
                this.stateProvider = stateProvider;
            }

            internal void SetParent(StateLogicDef parent) => this.parent = parent;

            public bool EvaluateLogic(ProgressionManager pm)
            {
                for (int i = 0; i < logic.Length; i++)
                {
                    int id = logic[i];
                    switch (id)
                    {
                        case >= 0:
                            if (pm.Has(id)) continue;
                            break;
                        case (int)LogicOperators.ANY:
                            continue;
                        case (int)LogicOperators.NONE:
                            break;
                        case (int)LogicOperators.EQ or (int)LogicOperators.LT or (int)LogicOperators.GT:
                            LogicOperators op = (LogicOperators)id;
                            id = logic[++i];
                            int left = id >= 0 ? pm.Get(id) : EvaluateVariable(id, pm);
                            id = logic[++i];
                            int right = id >= 0 ? pm.Get(id) : EvaluateVariable(id, pm);
                            if (op switch
                            {
                                LogicOperators.EQ => left == right,
                                LogicOperators.LT => left < right,
                                _ => left > right,
                            }) continue;
                            break;
                        default:
                            if (EvaluateVariable(id, pm) > 0) continue;
                            break;
                    }
                    return false;
                }
                return true;
            }

            public bool EvaluateStateChange(ProgressionManager pm, List<State> result)
            {
                StateUnion? input = GetInputState(pm);
                if (input == null || !EvaluateLogic(pm)) return false;
                if (stateLogic.Length == 0)
                {
                    for (int k = 0; k < input.Count; k++) result.Add(input[k]);
                    return true;
                }
                else
                {
                    for (int k = 0; k < input.Count; k++)
                    {
                        EvaluateStateChangeRec(0, pm, result, new(input[k]));
                    }
                    return EmptyEvaluateStateChangeRec(0, pm, result);
                }
            }

            private int EvaluateVariable(int id, ProgressionManager pm)
            {
                return pm.lm.GetVariable(id) switch
                {
                    LogicInt li => li.GetValue(parent, pm),
                    _ => 0,
                };
            }

            public bool EvaluateStateDiscard(ProgressionManager pm)
            {
                if (stateLogic.Length == 0)
                {
                    return true;
                }
                StateUnion? input = GetInputState(pm);
                if (input is null)
                {
                    return false;
                }
                for (int k = 0; k < input.Count; k++)
                {
                    if (EvaluateStateDiscardRec(0, pm, new LazyStateBuilder(input[k]))) return true;
                }
                return EmptyEvaluateStateDiscardRec(0, pm);
            }

            private bool EvaluateStateDiscardRec(int i, ProgressionManager pm, LazyStateBuilder lsb)
            {
                if (i == stateLogic.Length)
                {
                    return true;
                }

                int id = stateLogic[i];
                if (id < -99)
                {
                    foreach (LazyStateBuilder lsb2 in ((StateModifier)pm.lm.GetVariable(id)).ModifyState(parent, pm, lsb))
                    {
                        if (EvaluateStateDiscardRec(i + 1, pm, lsb2)) return true;
                    }
                }
                else
                {
                    LogicOperators op = (LogicOperators)id;
                    int left = EvaluateStateVariable(stateLogic[i + 1], pm, lsb);
                    int right = EvaluateStateVariable(stateLogic[i + 1], pm, lsb);
                    if (op switch
                    {
                        LogicOperators.GT => left > right,
                        LogicOperators.LT => left < right,
                        LogicOperators.EQ => left == right,
                        _ => false
                    })
                    {
                        if (EvaluateStateDiscardRec(i + 3, pm, lsb)) return true;
                    }
                }

                return false;
            }

            private bool EmptyEvaluateStateDiscardRec(int i, ProgressionManager pm)
            {
                if (i == stateLogic.Length)
                {
                    return true;
                }

                int id = stateLogic[i];
                if (id < -99 && ((StateModifier)pm.lm.GetVariable(id)).ProvideState(parent, pm) is IEnumerable<LazyStateBuilder> lsbs)
                {
                    foreach (LazyStateBuilder lsb2 in lsbs)
                    {
                        if (EvaluateStateDiscardRec(i + 1, pm, lsb2)) return true;
                    }
                    if (EmptyEvaluateStateDiscardRec(i + 1, pm)) return true;
                }

                return false;
            }

            private void EvaluateStateChangeRec(int i, ProgressionManager pm, List<State> states, LazyStateBuilder lsb)
            {
                if (i == stateLogic.Length)
                {
                    states.Add(lsb.GetState());
                    return;
                }

                int id = stateLogic[i];
                if (id < -99)
                {
                    foreach (LazyStateBuilder lsb2 in ((StateModifier)pm.lm.GetVariable(id)).ModifyState(parent, pm, lsb))
                    {
                        EvaluateStateChangeRec(i + 1, pm, states, lsb2);
                    }
                }
                else
                {
                    LogicOperators op = (LogicOperators)id;
                    int left = EvaluateStateVariable(id + 1, pm, lsb);
                    int right = EvaluateStateVariable(id + 2, pm, lsb);
                    if (op switch
                    {
                        LogicOperators.GT => left > right,
                        LogicOperators.LT => left < right,
                        LogicOperators.EQ => left == right,
                        _ => false
                    })
                    {
                        EvaluateStateChangeRec(i + 3, pm, states, lsb);
                    }
                }
            }

            private bool EmptyEvaluateStateChangeRec(int i, ProgressionManager pm, List<State> states)
            {
                if (i == stateLogic.Length)
                {
                    return true;
                }

                int id = stateLogic[i];
                if (((StateModifier)pm.lm.GetVariable(id)).ProvideState(null, pm) is IEnumerable<LazyStateBuilder> lsbs)
                {
                    foreach (LazyStateBuilder lsb2 in lsbs)
                    {
                        EvaluateStateChangeRec(i, pm, states, lsb2);
                    }
                }
                return EmptyEvaluateStateChangeRec(i + 1, pm, states);
            }



            private int EvaluateStateVariable(int id, ProgressionManager pm, LazyStateBuilder state)
            {
                if (id >= 0) return pm.Get(id);

                return pm.lm.GetVariable(id) switch
                {
                    LogicInt li => li.GetValue(parent, pm),
                    StateAccessVariable sav => sav.GetValue(parent, pm, state),
                    _ => 0,
                };
            }

            public StateUnion? GetInputState(ProgressionManager pm)
            {
                switch (stateProvider)
                {
                    case >= 0:
                        return pm.GetState(stateProvider);
                    case <= LogicManager.intVariableOffset:
                        if (pm.lm.GetVariable(stateProvider) is StateProvider spv) return spv.GetInputState(parent, pm);
                        break;
                }
                return null;
            }

            public bool EvaluateClause(ProgressionManager pm) => EvaluateLogic(pm) && EvaluateStateDiscard(pm);

            public IEnumerable<Term> GetTerms(LogicManager lm)
            {
                for (int i = 0; i < logic.Length; i++)
                {
                    switch (logic[i])
                    {
                        case (int)LogicOperators.NONE:
                        case (int)LogicOperators.ANY:
                            continue;
                        case (int)LogicOperators.GT:
                        case (int)LogicOperators.LT:
                        case (int)LogicOperators.EQ:
                            {
                                int left = logic[++i];
                                if (left >= 0) yield return lm.GetTerm(left);
                                else foreach (Term t in lm.GetVariable(left).GetTerms()) yield return t;
                                int right = logic[++i];
                                if (right >= 0) yield return lm.GetTerm(right);
                                else foreach (Term t in lm.GetVariable(right).GetTerms()) yield return t;
                            }
                            break;
                        default:
                            {
                                if (logic[i] >= 0) yield return lm.GetTerm(logic[i]);
                                else foreach (Term t in lm.GetVariable(logic[i]).GetTerms()) yield return t;
                            }
                            break;
                    }
                }
                for (int i = 0; i < stateLogic.Length; i++)
                {
                    switch (stateLogic[i])
                    {
                        case (int)LogicOperators.NONE:
                        case (int)LogicOperators.ANY:
                            continue;
                        case (int)LogicOperators.GT:
                        case (int)LogicOperators.LT:
                        case (int)LogicOperators.EQ:
                            {
                                int left = stateLogic[++i];
                                if (left >= 0) yield return lm.GetTerm(left);
                                else foreach (Term t in lm.GetVariable(left).GetTerms()) yield return t;
                                int right = stateLogic[++i];
                                if (right >= 0) yield return lm.GetTerm(right);
                                else foreach (Term t in lm.GetVariable(right).GetTerms()) yield return t;
                            }
                            break;
                        default:
                            {
                                if (stateLogic[i] >= 0) yield return lm.GetTerm(stateLogic[i]);
                                else foreach (Term t in lm.GetVariable(stateLogic[i]).GetTerms()) yield return t;
                            }
                            break;
                    }
                }
            }
        }

        internal DNFLogicDef(Clause[] clauses, LogicManager lm, string name, string infixSource) : base(name, infixSource)
        {
            this.clauses = clauses;
            this.lm = lm;

            foreach (Clause c in clauses) c.SetParent(this);
        }

        public DNFLogicDef(DNFLogicDef other) : base(other.Name, other.InfixSource)
        {
            this.clauses = other.clauses;
            this.lm = other.lm;
            this.termClauseLookup = other.termClauseLookup;
        }

        public override bool CanGet(ProgressionManager pm)
        {
            for (int j = 0; j < clauses.Length; j++)
            {
                if (clauses[j].EvaluateClause(pm)) return true;
            }
            return false;
        }

        public override bool EvaluateState(ProgressionManager pm, List<State> states)
        {
            bool succeedsOnEmpty = false;
            for (int j = 0; j < clauses.Length; j++)
            {
                succeedsOnEmpty |= clauses[j].EvaluateStateChange(pm, states);
            }
            return succeedsOnEmpty;
        }

        private void CreateTermClauseLookup()
        {
            termClauseLookup = new();
            foreach (Clause c in clauses)
            {
                foreach (Term t in c.GetTerms(lm))
                {
                    if (!termClauseLookup.TryGetValue(t, out List<Clause> cs))
                    {
                        termClauseLookup.Add(t, cs = new());
                    }
                    cs.Add(c);
                }
            }
        }

        public override bool CheckForUpdatedState(ProgressionManager pm, StateUnion current, List<State> newStates, int modifiedTerm, out StateUnion result)
        {
            if (termClauseLookup is null) CreateTermClauseLookup();

            bool succeedOnEmpty = false;
            foreach (Clause c in termClauseLookup[modifiedTerm])
            {
                succeedOnEmpty |= c.EvaluateStateChange(pm, newStates);
            }
            if (current is null)
            {
                if (newStates.Count > 0)
                {
                    result = new(newStates);
                    return true;
                }
                else if (succeedOnEmpty)
                {
                    result = lm.StateManager.Empty;
                    return true;
                }
                else
                {
                    result = null;
                    return false;
                }
            }
            else return StateUnion.TryUnion(current, newStates, out result);
        }

        public List<TermToken>? GetFirstSuccessfulConjunction(ProgressionManager pm)
        {
            for (int j = 0; j < clauses.Length; j++)
            {
                if (clauses[j].EvaluateClause(pm)) return ConvertClause(j);
            }
            return null;
        }

        public List<List<TermToken>> GetAllSuccessfulConjunctions(ProgressionManager pm)
        {
            List<List<TermToken>> successes = new();
            for (int j = 0; j < clauses.Length; j++)
            {
                if (clauses[j].EvaluateClause(pm)) successes.Add(ConvertClause(j));
            }
            return successes;
        }

        private TermToken ConvertToken(int[] clause, ref int i)
        {
            int id = clause[i];
            switch (id)
            {
                case >= 0:
                    return lm.LP.GetTermToken(lm.Terms[id].Name);
                case (int)LogicOperators.ANY:
                    return ConstToken.True;
                case (int)LogicOperators.NONE:
                    return ConstToken.False;
                case (int)LogicOperators.AND:
                    throw new NotSupportedException();
                case (int)LogicOperators.EQ or (int)LogicOperators.LT or (int)LogicOperators.GT:
                    LogicOperators op = (LogicOperators)id;
                    id = clause[++i];
                    string left = id >= 0 ? lm.GetTerm(id).Name : lm.GetVariable(id).Name;
                    id = clause[++i];
                    string right = id >= 0 ? lm.GetTerm(id).Name : lm.GetVariable(id).Name;
                    return op switch
                    {
                        LogicOperators.EQ => lm.LP.GetComparisonToken(ComparisonType.EQ, left, right),
                        LogicOperators.LT => lm.LP.GetComparisonToken(ComparisonType.LT, left, right),
                        _ => lm.LP.GetComparisonToken(ComparisonType.GT, left, right),
                    };
                default:
                    return lm.LP.GetTermToken(lm.GetVariable(id).Name);
            }
        }

        private List<TermToken> ConvertClause(int j)
        {
            List<TermToken> result = new();

            Clause c = clauses[j];
            for (int i = 0; i < c.logic.Length; i++)
            {
                result.Add(ConvertToken(c.logic, ref i));
            }
            for (int i = 0; i < c.stateLogic.Length; i++)
            {
                result.Add(ConvertToken(c.logic, ref i));
            }

            return result;
        }

        public override IEnumerable<LogicToken> ToTokenSequence()
        {
            return RPN.OperateOver(Enumerable.Range(0, clauses.Length).Select(j => RPN.OperateOver(ConvertClause(j), OperatorToken.AND)), OperatorToken.OR);
        }

        public override IEnumerable<Term> GetTerms()
        {
            return clauses.SelectMany(c => c.GetTerms(lm));
        }

        // cursed hacks for deserialization into ILogicDef property type, where the converter doesn't trigger.
        [JsonConstructor]
        protected DNFLogicDef(string Name, string Logic) : this(ConverterFetchOrMake(Name, Logic))
        {
        }

        private static DNFLogicDef ConverterFetchOrMake(string name, string logic)
        {
            if (LogicDefConverter.Instance.LM.GetLogicDef(name) is DNFLogicDef other && other.InfixSource == logic) return other;
            return LogicDefConverter.Instance.LM.CreateDNFLogicDef(new(name, logic));
        }
    }

    [Obsolete]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public class OptimizedLogicDef : DNFLogicDef
    {
        public OptimizedLogicDef(DNFLogicDef def) : base(def)
        {
        }

        [JsonConstructor]
        protected OptimizedLogicDef(string Name, string Logic) : base(Name, Logic)
        {
        }
    }
}

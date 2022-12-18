﻿using RandomizerCore.Json;
using System.Collections.ObjectModel;

namespace RandomizerCore.Logic.StateLogic
{
    public record StateBool(int Id, string Name) : StateField(Id, Name)
    {
        public bool GetDefaultValue(StateManager sm)
        {
            if (sm.TryGetProperty(Name, DefaultValuePropertyName, out object? defaultValue) && defaultValue is bool b) return b;
            return false;
        }
        public override StateFieldType GetFieldType()
        {
            return StateFieldType.Bool;
        }
    }
    public record StateInt(int Id, string Name) : StateField(Id, Name)
    {
        public int GetDefaultValue(StateManager sm)
        {
            if (sm.TryGetProperty(Name, DefaultValuePropertyName, out object? defaultValue) && defaultValue is int i) return i;
            return 0;
        }

        public override StateFieldType GetFieldType()
        {
            return StateFieldType.Int;
        }
    }
    public abstract record StateField(int Id, string Name)
    {
        public const string DefaultValuePropertyName = "DefaultValue";
        public abstract StateFieldType GetFieldType();
        public static implicit operator int(StateField sf) => sf.Id;
    }

    public enum StateFieldType
    {
        Bool,
        Int,
    }

    /// <summary>
    /// Object which manages the list of fields which <see cref="State"/> should represent.
    /// </summary>
    public class StateManager
    {
        public ReadOnlyCollection<StateBool> Bools { get; }
        public ReadOnlyCollection<StateInt> Ints { get; }
        public ReadOnlyDictionary<string, StateField> FieldLookup { get; }
        public ReadOnlyDictionary<string, ReadOnlyCollection<StateField>> TagLookup { get; }
        public ReadOnlyDictionary<string, ReadOnlyDictionary<string, object?>> FieldProperties { get; }
        public ReadOnlyDictionary<string, State> NamedStates { get; }
        public ReadOnlyDictionary<string, StateUnion> NamedStateUnions { get; }
            
        private readonly StateBool[] _bools;
        private readonly StateInt[] _ints;
        private readonly Dictionary<string, StateField> _fieldLookup;

        private readonly Dictionary<string, object> _printer = new();

        public readonly State StartState;
        public readonly StateUnion StartStateSingleton;
        public readonly StateUnion Empty;

        public StateManager(StateManagerBuilder builder)
        {
            _bools = builder.Bools.ToArray();
            Bools = new(_bools);
            _ints = builder.Ints.ToArray();
            Ints = new(_ints);
            _fieldLookup = new(builder.FieldLookup);
            FieldLookup = new(_fieldLookup);
            TagLookup = new(builder.EnumerateTagLists().ToDictionary(p => p.tag, p => new ReadOnlyCollection<StateField>(p.Item2.ToArray())));
            FieldProperties = new(builder.EnumeratePropertyLists().ToDictionary(p => p.Item1, p => new ReadOnlyDictionary<string, object?>(p.Item2.ToDictionary(q => q.Item1, q => q.Item2))));
            NamedStates = new(builder.EnumerateNamedStates().ToDictionary(p => p.Item1, p => p.Item2.ToState(this)));
            NamedStateUnions = new(builder.EnumerateNamedStateUnions().ToDictionary(p => p.Item1, p => new StateUnion(p.Item2.Select(s => s.ToState(this)).ToList())));
            StartState = CreateDefault();
            StartStateSingleton = new(StartState);
            Empty = StateUnion.Empty;
        }

        /// <summary>
        /// Fetches the state bool by name. Returns null if not defined.
        /// </summary>
        public StateBool? GetBool(string name)
        {
            FieldLookup.TryGetValue(name, out StateField sf);
            return sf as StateBool;
        }

        /// <summary>
        /// Fetches the state bool by name.
        /// </summary>
        /// <exception cref="ArgumentException">The state bool is not defined.</exception>
        public StateBool GetBoolStrict(string name)
        {
            return GetBool(name) ?? throw new ArgumentException($"StateBool {name} is not defined.");
        }

        /// <summary>
        /// Fetches the state int by name. Returns null if not defined.
        /// </summary>
        public StateInt? GetInt(string name)
        {
            FieldLookup.TryGetValue(name, out StateField sf);
            return sf as StateInt;
        }

        /// <summary>
        /// Fetches the state int by name.
        /// </summary>
        /// <exception cref="ArgumentException">The state int is not defined.</exception>
        public StateInt GetIntStrict(string name)
        {
            return GetInt(name) ?? throw new ArgumentException($"StateInt {name} is not defined.");
        }

        public IEnumerable<StateField> GetListByTag(string tag)
        {
            TagLookup.TryGetValue(tag, out ReadOnlyCollection<StateField> value);
            return value ?? Enumerable.Empty<StateField>();
        }

        public bool TryGetProperty(string fieldName, string propertyName, out object? value)
        {
            if (FieldProperties.TryGetValue(fieldName, out ReadOnlyDictionary<string, object?> properties)
                && properties.TryGetValue(propertyName, out value)) return true;
            value = null;
            return false;
        }

        public State? GetNamedState(string name)
        {
            return NamedStates[name];
        }

        public State GetNamedStateStrict(string name)
        {
            return GetNamedState(name) ?? throw new ArgumentException($"Named state {name} is not defined.");
        }

        public StateUnion? GetNamedStateUnion(string name)
        {
            return NamedStateUnions[name];
        }

        public StateUnion GetNamedStateUnionStrict(string name)
        {
            return GetNamedStateUnion(name) ?? throw new ArgumentException($"Named state union {name} is not defined.");
        }

        public string PrettyPrint(State state)
        {
            _printer.Clear();
            for (int i = 0; i < Bools.Count; i++)
            {
                if (state.GetBool(i)) _printer.Add(_bools[i].Name, true);
            }
            for (int i = 0; i < Ints.Count; i++)
            {
                int j = state.GetInt(i);
                if (j > 0) _printer.Add(_ints[i].Name, j);
            }
            return JsonUtil.SerializeNonindented(_printer);
        }

        public string PrettyPrint(StateUnion? states)
        {
            if (states is null) return "null";
            return JsonUtil.SerializeNonindented(states.Select(s => PrettyPrint(s))).Replace("\"", "").Replace("\\", "");
        }

        public Dictionary<string, List<string>> GetFieldDefs()
        {
            return new Dictionary<string, List<string>>()
            {
                { StateFieldType.Bool.ToString(), new(Bools.Select(sb => sb.Name)) },
                { StateFieldType.Int.ToString(), new(Ints.Select(si => si.Name)) }
            };
        }

        private State CreateDefault()
        {
            StateBuilder sb = new(this);
            for (int i = 0; i < _bools.Length; i++)
            {
                if (Bools[i].GetDefaultValue(this))
                {
                    sb.SetBool(i, true);
                }
            }
            for (int i = 0; i < _ints.Length; i++)
            {
                int j = Ints[i].GetDefaultValue(this);
                if (j != 0)
                {
                    sb.SetInt(i, Ints[i].GetDefaultValue(this));
                }
            }
            return new(sb);
        }

    }
}
## Defining State Fields

State fields can be defined by using the StateManagerBuilder attached to a LogicManagerBuilder, or by defining them in a Json file and feeding the file into the LogicManagerBuilder. Once defined, the default value of a state field can be changed, but its type and name cannot be changed.

When adding a state field, it is essential to carefully consider how it will interact with the State ordering. For example, by default, state bools default to false, and can be set true. This is ideal for representing a consumable resource which one starts with, and can be spent once. States which have spent the resource will be discarded if there is a strictly better alternative which has not spent the resource. On the other hand, to represent a resource which one does not start with, but can be obtained once later, the state bool should be created to default to true, and be set false once the resource is obtained, so that states with the resource will not be pruned.

## Defining LogicVariables to Interact with State

A LogicVariable is a token in logic which has special effects defined in code. To define any LogicVariable, one needs to define a VariableResolver which can identify it by name. The VR is attached to the LogicManagerBuilder and to the LogicManager. To extend the strings recognized by a VR, first make a class deriving from VariableResolver which overrides TryMatch, then when replacing the old VariableResolver, assign the old VR to the Inner property of the new VR. Then calls which the outer VR cannot handle will be passed to the inner VR.

### StateModifier

StateModifiers take an input state and produce a sequence of output states, through the ModifyState method. They also can produce a sequence of output states with no input, through the ProvideState method. In a conjunction, StateModifiers act left-to-right sequentially, modifying the output of the previous modifier and providing any additional states. StateModifiers work in the general setting by first converting logic to disjunctive normal form and determining the input state for each conjunction in the DNF, then working as described for conjunctions.

Note: since StateModifiers do not derive from LogicInt, they cannot be used in comparisons (i.e. expressions with '<','=', or '>').

To define a StateModifier, one must implement ModifyState to return a non-null sequence of states. ModifyState should return an empty sequence if the input fails. 

ProvideState can be optionally overriden, to express when the StateModifier is able to succeed regardless of its input. Here, ProvideState should return a null sequence if the input fails, and its default implementation is to always return null. The empty sequence expresses that ProvideState succeeded with indeterminate output.

### StateProvider

Ordinarily, input state is determined by the first state-valued term (e.g. transition or waypoint) which appears in a conjunction. However, a StateProvider variable can be defined to supply a state determined in code, if it appears before any other state provider terms or variables in the conjunction. For example, "$START" provides the state which has all fields at their default values.

A StateProvider is additionally a LogicInt. By default, its LogicInt.GetValue always returns LogicVariable.TRUE, but this can be overriden if desired.

## Integration with the ProgressionManager and MainUpdater

When evaluating bool logic, all terms are interpreted as int-valued. The int value of a state-valued term is 0 if its state is null, and 1 otherwise. This is the value returned by pm.Get(id) if id is the id of a state-valued term, and the value used for derived computations such as pm.Has, etc. Use pm.GetState to retrieve the full StateUnion associated with the term.

The MainUpdater automatically manages logic-derived state updates for state-valued waypoints and transitions. Use mu.AddManagedStates to give a state-valued term logic-derived state updates. Often, the effect of an item may be to trigger an ongoing state modification. This can be done by modifying the MainUpdater attached to the ProgressionManager. If the state effect depends on the item's location, this can further be done within an ILocationDependentItem implementation for the item.
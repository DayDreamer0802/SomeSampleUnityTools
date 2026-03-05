using System;
using System.Collections.Generic;
using UnityEngine;

public delegate bool TransitionCondition(out object param);

public class StateTransition
{
    public TransitionCondition Condition;
    public int Order;
    public Type TargetType;
}

public class StateMachine
{
    public StateMachine(object parent)
    {
        this.parent = parent;
    }

    public object parent { get; private set; }

    private readonly Dictionary<Type, IState> states = new();

    private IState currentState;
    private IState previousState;

    public IState PreviousState => previousState;
    public IState CurrentState => currentState;

    public void RemoveState<T>(T type) where T : IState
    {
        Type typeToRemove = typeof(T);
        if (states.TryGetValue(typeToRemove, out var state) && currentState != state)
        {
            if (previousState == state)
                previousState = null;
            foreach (var VARIABLE in states)
            {
                var transition = VARIABLE.Value.Transitions;
                for (int i = transition.Count - 1; i >= 0; i++)
                {
                    if (transition[i].TargetType == typeToRemove)
                    {
                        transition.RemoveAt(i);
                    }
                }
            }

            states.Remove(typeToRemove);
        }
    }

    public void AddState<T>(T state, object parameters = null) where T : IState
    {
        var type = typeof(T);
        if (!states.TryAdd(typeof(T), state))
        {
            Debug.LogWarning("Trying to add more than one state with the same ID " + type + "(" +
                             typeof(T).Name + ")");
            return;
        }

        state.parentMachine = this;
        state.OnInit(parameters);
        state.Transitions = new List<StateTransition>();
    }

    public void ChangeState<T>(object parameters = null) where T : IState
    {
        var type = typeof(T);
        if (!ChangeStateByType(type, parameters))
        {
            Debug.LogWarning("Trying to change a state doesn't exist " + type + "(" + typeof(T).Name + ")");
        }
    }

    bool ChangeStateByType(Type ID, object parameters = null)
    {
        if (states.TryGetValue(ID, out var state))
        {
            ChangeState(state, parameters);
            return true;
        }

        return false;
    }

    void ChangeState(IState state, object parameters = null)
    {
        currentState?.OnExit();
        previousState = currentState;
        currentState = state;
        currentState?.OnEnter(parameters);
    }

    public void Update(float deltaTime)
    {
        if (currentState != null)
        {
            currentState.OnUpdate(deltaTime);
            IState iState = null;
            object parameters = null;
            bool Change = false;
            foreach (var transition in currentState.Transitions)
            {
                if (transition.Condition != null && transition.Condition(out object param) &&
                    states.TryGetValue(transition.TargetType, out var state))
                {
                    iState = state;
                    Change = true;
                    parameters = param;
                    break;
                }
            }

            if (Change)
            {
                ChangeState(iState, parameters);
            }
        }
    }

    public void RemoveTransition<T>(TransitionCondition func) where T : IState
    {
        var type = typeof(T);
        if (!states.TryGetValue(type, out IState state))
        {
            Debug.LogWarning("Trying to add transition to a empty state " + type + "(" + typeof(T).Name + ")");
            return;
        }

        state.RemoveTransition(func);
    }

    public void SortTransitions<T>() where T : IState
    {
        var type = typeof(T);
        if (!states.TryGetValue(type, out IState state))
        {
            Debug.LogWarning("Trying to sort transition from a empty state " + type + "(" + typeof(T).Name + ")");
            return;
        }

        state.SortTransitions();
    }

    public void AddTransition<T, K>(TransitionCondition func, int order) where T : IState where K : IState
    {
        var type = typeof(T);
        if (!states.TryGetValue(type, out IState state))
        {
            Debug.LogWarning("Trying to add transition to a empty state " + typeof(T) + "(" + typeof(T).Name + ")");
            return;
        }

        state.AddTransition(func, order, typeof(K));
    }
}

public interface IState
{
    public StateMachine parentMachine { get; set; }

    public void RemoveTransition(TransitionCondition func)
    {
        Transitions.RemoveAll(t => t.Condition == func);
    }

    public void SortTransitions()
    {
        Transitions.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    public void AddTransition(TransitionCondition func, int order, Type targetType)
    {
        Transitions.Add(new StateTransition() { Condition = func, Order = order, TargetType = targetType });
    }

    List<StateTransition> Transitions { get; set; }
    public void OnInit(object args = null);
    public void OnUpdate(float dt);
    public void OnEnter(object args = null);
    public void OnExit();
}
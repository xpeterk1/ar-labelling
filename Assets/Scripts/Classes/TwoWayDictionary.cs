using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TwoWayDictionary<T1, T2>
{
    private Dictionary<T1, T2> _first;
    private Dictionary<T2, T1> _second;

    public TwoWayDictionary() 
    {
        _first = new Dictionary<T1, T2>();
        _second = new Dictionary<T2, T1>();
    }

    public int Count { get => _first.Count; }

    public void Add(T1 key, T2 value) 
    {
        _first.Add(key, value);
        _second.Add(value, key);
    }

    public T2 GetValue(T1 key) => _first[key];

    public T1 GetKey(T2 value) => _second[value];

    public void PrintKeys() 
    {
        Debug.Log("=========KEYS=========");
        foreach (var key in _first.Keys) 
        {
            Debug.Log(key);
        }
    }

    public void PrintValues() 
    {
        Debug.Log("=========VALUES=========");
        foreach (var value in _first.Values) 
        {
            Debug.Log(value);
        }
    }

}

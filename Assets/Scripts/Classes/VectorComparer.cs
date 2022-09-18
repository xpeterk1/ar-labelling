using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VectorComparer : IComparer<Vector3>
{
    private int ContourSize { get; set; }

    private int CurrentGroupSize { get; set; }

    public VectorComparer(int contourSize, int currentSize)
    {
        ContourSize = contourSize;
        CurrentGroupSize = currentSize;
    }

    public int Compare(Vector3 x, Vector3 y)
    {
        int fst = (int)x.z;
        int snd = (int)y.z;

        int difference = Math.Abs(fst - snd);
        if (difference > CurrentGroupSize)
        {
            // transition
            return -1 * x.z.CompareTo(y.z);
        }
        else
        {
            // not a transition
            return x.z.CompareTo(y.z);
        }
    }
}

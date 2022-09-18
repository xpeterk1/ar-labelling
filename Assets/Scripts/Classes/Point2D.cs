using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Point2D
{
    public int X { get; set; }
    public int Y { get; set; }
    
    public object Tag { get; set; }

    public int Distance { get; set; }

    public Point2D EdgePoint { get; set; }

    public Point2D(float v) { }

    public Point2D(int x, int y) 
    {
        X = x;
        Y = y;
    }
}

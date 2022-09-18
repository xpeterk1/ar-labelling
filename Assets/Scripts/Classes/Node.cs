using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Node
{

    public bool IsLeaf { get => Left == null && Right == null; }

    /// <summary>
    /// Reference to left node, null if no node exists
    /// </summary>
    public Node Left { get; set; }
    
    /// <summary>
    /// Reference to right node, null if no node exists
    /// </summary>
    public Node Right { get; set; }

    /// <summary>
    /// Level of the tree, 0 = root, 2 = last node, 3 = leaf with points
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Point 'somewhere' on the line
    /// </summary>
    public Point2D P { get; set; }
    
    /// <summary>
    /// Perpendicular projection of point P onto parent line
    /// </summary>
    public Point2D Q { get; set; }

    /// <summary>
    /// List of all anchors, that belong to this node
    /// </summary>
    public List<Point2D> Anchors { get; set; }

    /// <summary>
    /// List of all convex hull points within this node
    /// </summary>
    public SortedSet<Vector3> HullPoints { get; set; }

    /// <summary>
    /// Intersecion of the line with convex hull
    /// </summary>
    public Point2D ContourIntersection { get; set; }

    /// <summary>
    /// Only root node has one
    /// </summary>
    public Point2D SecondContourIntersection { get; set; }

}

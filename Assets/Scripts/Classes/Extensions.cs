using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Extensions
{
    public static Point2D ToPoint(this Vector3 v)
    {
        return new Point2D((int)v.x, (int)v.y);
    }

    public static Point2D AlignText(this TextAlignment alignment, Vector2 textBounds, Point2D originalPoint)
    {
        Point2D output = new Point2D(originalPoint.X, originalPoint.Y);
        
        switch (alignment)
        {
            case TextAlignment.TopLeft:
                break;
            case TextAlignment.TopRight:
                output.X -= (int)(textBounds.x);
                break;
            case TextAlignment.Left:
                output.X -= (int)(textBounds.x);
                output.Y += (int)(textBounds.y / 2);
                break;
            case TextAlignment.Right:
                output.Y += (int)(textBounds.y / 2);
                break;
            case TextAlignment.BottomLeft:
                output.Y += (int)(textBounds.y);
                break;
            case TextAlignment.BottomRight:
                output.X -= (int)(textBounds.x);
                output.Y += (int)(textBounds.y);
                break;
        }

        return output;
    }
}

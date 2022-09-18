using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum MovementMode
{
    // FAR MOVEMENT, requires the next still frame to be fully recomputed
    Far, 
    
    // Always recompute
    Normal, 
    
    // Never recompute
    Close
}

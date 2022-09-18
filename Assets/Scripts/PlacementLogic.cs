using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class PlacementLogic : MonoBehaviour
{
    private ARRaycastManager raycastManager;
    private GameObject visual;
    private bool objectPlaced = false;

    void Start()
    {
        raycastManager = FindObjectOfType<ARRaycastManager>();
        visual = transform.GetChild(0).gameObject;

        visual.SetActive(false);
    }

    void Update()
    {
        List<ARRaycastHit> hits = new List<ARRaycastHit>();
        var screenCenter = Camera.current.ViewportToScreenPoint(new Vector3(0.5f, 0.5f));
        raycastManager.Raycast(screenCenter, hits, UnityEngine.XR.ARSubsystems.TrackableType.PlaneWithinPolygon);

        if (hits.Count > 0) 
        {
            transform.SetPositionAndRotation(hits[0].pose.position, hits[0].pose.rotation);

            if (!visual.activeInHierarchy)
                visual.SetActive(true);
        }

        // if model is already placed, disable this script (no longer required)
        if (!objectPlaced && Input.touchCount > 0 && Input.touches[0].phase == TouchPhase.Began) 
        {
            this.enabled = false;
            objectPlaced = true;
            visual.SetActive(false);
        }
    }
}

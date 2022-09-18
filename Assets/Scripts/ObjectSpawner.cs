using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectSpawner : MonoBehaviour
{
    public GameObject spawnobject;
    private PlacementLogic placementLogic;
    private bool objectPlaced = false;

    private void Start()
    {
        placementLogic = FindObjectOfType<PlacementLogic>();
    }

    private void Update()
    {
        if (!objectPlaced && Input.touchCount > 0 && Input.touches[0].phase == TouchPhase.Began) 
        {
            objectPlaced = true;
            Instantiate(spawnobject, placementLogic.transform.position, Quaternion.Euler(0, -180, 0));
            GameObject.Find("AnnotationLogicHolder").SetActive(true);
        }
    }
}

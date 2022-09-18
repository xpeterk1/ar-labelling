using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class AssignTexturesToModels : MonoBehaviour
{
    [MenuItem("Tools/Assign Materials")]
    static void CreateMaterials()
    {
        GameObject modelPrefab = Resources.Load("model") as GameObject;

        for (int i = 0; i < modelPrefab.transform.childCount; i++) 
        {
            Transform child = modelPrefab.transform.GetChild(i);
            string name = child.name.Replace(" ", "_");
            Material m = Resources.Load($"Materials/{name}") as Material;
            child.GetComponent<Renderer>().material = m;
        }
    }
}

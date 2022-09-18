using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Linq;
using System.IO;
using System.Collections.Generic;

public class CreateMaterialsForTextures : Editor
{
    [MenuItem("Tools/Create Materials")]
    static void CreateMaterials()
    {
        try
        {
            AssetDatabase.StartAssetEditing();

            string dataPath = Application.dataPath;
            string textureFolderPath = dataPath.Substring(0, dataPath.Length - 6) + "Assets/Resources/Textures";
            List<string> filePaths = Directory.GetFiles(textureFolderPath).ToList();
            filePaths = filePaths.Where(x => x.EndsWith(".png")).ToList();

            string materialFolder = dataPath.Substring(0, dataPath.Length - 6) + "Assets/Resources/Materials";
            if (Directory.Exists(materialFolder))
                Directory.Delete(materialFolder, true);
            Directory.CreateDirectory(materialFolder);

            filePaths.Sort();

            for (int i = 0; i < filePaths.Count; i++) 
            {
                if (!filePaths[i].EndsWith("_diffuse.png") && !filePaths[i].EndsWith("_normal.png"))
                    continue;

                string diffuseDataPath = filePaths[i].Substring(filePaths[i].LastIndexOf("\\") + 1);
                diffuseDataPath = diffuseDataPath.Substring(0, diffuseDataPath.Length - 4);

                string normalDataPath = filePaths[i + 1].Substring(filePaths[i + 1].LastIndexOf("\\") + 1);
                normalDataPath = normalDataPath.Substring(0, normalDataPath.Length - 4);

                Texture2D diffuseTexture = (Resources.Load($"Textures/{diffuseDataPath}") as Texture2D);
                Texture2D normalTexture = (Resources.Load($"Textures/{normalDataPath}") as Texture2D);


                Material m = new Material(Shader.Find("Standard"));
                m.SetTexture("_MainTex", diffuseTexture);
                m.SetTexture("_BumpMap", normalTexture);

                string materialName = filePaths[i].Substring(filePaths[i].LastIndexOf('\\') + 1);
                materialName = materialName.Substring(0, materialName.LastIndexOf('_'));
                
                AssetDatabase.CreateAsset(m, "Assets/Resources/Materials/" +  materialName + ".mat");
                
                i++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }
    }
}

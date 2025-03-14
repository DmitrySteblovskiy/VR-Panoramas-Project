using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SkyboxSwitcher : MonoBehaviour
{
    public Material skyboxMaterial;
    public string folderName = "Content";

    private List<Texture2D> textures = new List<Texture2D>();
    private int currentIndex = 0;

    void Start()
    {
        LoadTextures();
        if (textures.Count > 0)
        {
            ApplyTexture(currentIndex);
        }
    }

    void Update()
    {
        // Получаем текущее состояние клавиатуры из новой Input System
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            {
                ChangeSkybox(-1);
            }
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            {
                ChangeSkybox(1);
            }
        }
    }

    void LoadTextures()
    {
        Texture2D[] loadedTextures = Resources.LoadAll<Texture2D>(folderName);
        textures.AddRange(loadedTextures);

        if (textures.Count == 0)
        {
            Debug.LogError("Не найдено панорам в папке Resources/" + folderName);
        }
    }

    void ChangeSkybox(int direction)
    {
        if (textures.Count == 0) return;

        currentIndex += direction;

        if (currentIndex < 0) currentIndex = textures.Count - 1;
        if (currentIndex >= textures.Count) currentIndex = 0;

        ApplyTexture(currentIndex);
    }

    void ApplyTexture(int index)
    {
        if (skyboxMaterial != null)
        {
            skyboxMaterial.SetTexture("_MainTex", textures[index]);
            RenderSettings.skybox = skyboxMaterial;
            Debug.Log("Смена Skybox: " + textures[index].name);
        }
        else
        {
            Debug.LogError("Skybox Material не установлен!");
        }
    }
}

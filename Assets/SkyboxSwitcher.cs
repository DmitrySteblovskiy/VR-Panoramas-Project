using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Oculus;

public class SkyboxSwitcher : MonoBehaviour
{
    public Material skyboxMaterial;
    public string folderName = "Content";

    private List<Texture2D> textures = new List<Texture2D>();
    private int currentIndex = 0;
    private bool comboTriggered = false;

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
            // || keyboard.aKey.wasPressedThisFrame
            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                ChangeSkybox(-1);
            }

            // || keyboard.dKey.wasPressedThisFram
            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                ChangeSkybox(1);
            }

            // Переключение вперёд (кнопка A)
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                ChangeSkybox(1);
            }

            // Переключение назад (кнопка B)
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            {
                ChangeSkybox(-1);
            }

            // Проверка зажатой комбинации буквы и цифры:
            string letter = "";
            if (keyboard.aKey.isPressed) letter = "a";
            else if (keyboard.bKey.isPressed) letter = "b";
            else if (keyboard.cKey.isPressed) letter = "c";
            else if (keyboard.dKey.isPressed) letter = "d";
            else if (keyboard.eKey.isPressed) letter = "e";
            else if (keyboard.fKey.isPressed) letter = "f";
            else if (keyboard.gKey.isPressed) letter = "g";
            else if (keyboard.hKey.isPressed) letter = "h";
            else if (keyboard.iKey.isPressed) letter = "i";
            else if (keyboard.jKey.isPressed) letter = "j";
            else if (keyboard.kKey.isPressed) letter = "k";
            else if (keyboard.lKey.isPressed) letter = "l";
            else if (keyboard.mKey.isPressed) letter = "m";
            else if (keyboard.nKey.isPressed) letter = "n";
            else if (keyboard.oKey.isPressed) letter = "o";
            else if (keyboard.pKey.isPressed) letter = "p";
            else if (keyboard.qKey.isPressed) letter = "q";
            else if (keyboard.rKey.isPressed) letter = "r";
            else if (keyboard.sKey.isPressed) letter = "s";
            else if (keyboard.tKey.isPressed) letter = "t";
            else if (keyboard.uKey.isPressed) letter = "u";
            else if (keyboard.vKey.isPressed) letter = "v";
            else if (keyboard.wKey.isPressed) letter = "w";
            else if (keyboard.xKey.isPressed) letter = "x";
            else if (keyboard.zKey.isPressed) letter = "z";

            string digit = "";
            if (keyboard.digit0Key.isPressed) digit = "0";
            else if (keyboard.digit1Key.isPressed) digit = "1";
            else if (keyboard.digit2Key.isPressed) digit = "2";
            else if (keyboard.digit3Key.isPressed) digit = "3";
            else if (keyboard.digit4Key.isPressed) digit = "4";
            else if (keyboard.digit5Key.isPressed) digit = "5";
            else if (keyboard.digit6Key.isPressed) digit = "6";
            else if (keyboard.digit7Key.isPressed) digit = "7";
            else if (keyboard.digit8Key.isPressed) digit = "8";
            else if (keyboard.digit9Key.isPressed) digit = "9";

            if (!string.IsNullOrEmpty(letter) && !string.IsNullOrEmpty(digit))
            {
                // Если комбинация не была уже обработана в текущем зажатии клавиш:
                if (!comboTriggered)
                {
                    string targetName = letter + digit; // Например, "a1"
                    bool found = false;
                    for (int i = 0; i < textures.Count; i++)
                    {
                        // Приводим имя текстуры к нижнему регистру для корректного сравнения.
                        if (textures[i].name.ToLower() == targetName)
                        {
                            currentIndex = i;
                            ApplyTexture(currentIndex);
                            Debug.Log("Смена Skybox на: " + targetName);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        Debug.LogWarning("Панорама с именем " + targetName + " не найдена!");
                    }
                    comboTriggered = true;
                }
            }

            else
            {
                // Если комбинация не зажата, сбрасываем флаг для следующего срабатывания
                comboTriggered = false;
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

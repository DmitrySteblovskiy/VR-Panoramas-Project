using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using Oculus;

public class SkyboxSwitcher : MonoBehaviour
{
    public Material skyboxMaterial;
    public string folderName = "Content";

    private List<Texture2D> localTextures = new List<Texture2D>();
    private int currentIndex = 0;
    private bool comboTriggered = false;

    private TcpClient client;
    private NetworkStream netStream;
    private Thread receiveThread;
    private bool isRunning = false;

    // Таймаут, через который делаем повторное подключение (в мс)
    private const int reconnectTimeout = 5000;

    // Буфер для данных, принятых в фоновом потоке
    private byte[] networkThreadData = null;
    private object lockObject = new object();

    private Texture2D currentNetworkTexture = null;

    //---------------------------------------------------------------------
    // Метод проверки доступности порта на заданном IP
    //---------------------------------------------------------------------
    static bool IsPortOpen(string host, int port, int timeout)
    {
        try
        {
            using (TcpClient testClient = new TcpClient())
            {
                var asyncResult = testClient.BeginConnect(host, port, null, null);
                bool success = asyncResult.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeout));
                if (!success)
                    return false;

                testClient.EndConnect(asyncResult);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    //---------------------------------------------------------------------
    // Получаем префикс локального IP-адреса (например, 192.168.31.)
    //---------------------------------------------------------------------
    string GetLocalIpPrefix()
    {
        try
        {
            // Берём все IP-адреса текущей машины
            var addresses = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (var addr in addresses)
            {
                // Ищем IPv4-адрес, не являющийся loopback
                if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                {
                    // Разделим по точкам и возьмём первые три октета
                    string[] parts = addr.ToString().Split('.');
                    if (parts.Length == 4)
                    {
                        return parts[0] + "." + parts[1] + "." + parts[2] + ".";
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Ошибка при получении локального IP: " + e);
        }

        // Если определить не удалось, предположим 192.168.0.
        return "192.168.0.";
    }

    //---------------------------------------------------------------------
    // Сканируем в подсети (1..254), ищем открыт ли порт
    //---------------------------------------------------------------------
    string FindServerIp(int port, int timeoutMs)
    {
        string prefix = GetLocalIpPrefix();

        Debug.Log("Сканируем в подсети с префиксом: " + prefix);
        for (int i = 1; i < 255; i++)
        {
            string testIp = prefix + i;
            if (IsPortOpen(testIp, port, timeoutMs))
            {
                Debug.Log("Найден сервер: " + testIp + ":" + port);
                return testIp;
            }
        }
        Debug.LogWarning("Сервер в подсети " + prefix + " не найден!");
        return "";
    }

    //---------------------------------------------------------------------
    // Точка входа: загружаем локальные текстуры, пытаемся подключиться
    //---------------------------------------------------------------------
    void Start()
    {
        LoadLocalTextures();
        if (localTextures.Count > 0)
        {
            ApplyTexture(localTextures[currentIndex]);
        }

        // Попробуем найти и подключиться
        TryConnect();
    }

    //---------------------------------------------------------------------
    // Пытаемся найти сервер и установить TCP-подключение
    // Если не удаётся, повторяем раз в 5 секунд
    //---------------------------------------------------------------------
    void TryConnect()
    {
        isRunning = false;

        // Чтобы не блокировать главный поток, запускаем в новом
        Thread connectThread = new Thread(() =>
        {
            while (!isRunning)
            {
                string host = FindServerIp(63508, 500);
                if (!string.IsNullOrEmpty(host))
                {
                    try
                    {
                        client = new TcpClient();
                        client.Connect(host, 63508);  // Порт 63508 (или 65308, если нужно)
                        netStream = client.GetStream();
                        isRunning = true;

                        Debug.Log($"Успешно подключены к {host}:63508");
                        // Запускаем поток чтения данных
                        receiveThread = new Thread(ReceivePanorama);
                        receiveThread.Start();

                        // Выходим из цикла, так как подключение установлено
                        break;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Не удалось подключиться: " + e);
                    }
                }
                else
                {
                    Debug.LogWarning("Сервер не найден, пробуем снова через 5 секунд...");
                }

                // Ждём 5 секунд перед повтором
                Thread.Sleep(reconnectTimeout);
            }
        });
        connectThread.Start();
    }


    //---------------------------------------------------------------------
    // Метод Update обрабатывает ввод и изменение текстуры в главном потоке
    //---------------------------------------------------------------------
    void Update()
    {
        // Проверяем, не пришли ли новые данные в фоновом потоке
        byte[] localCopy = null;
        lock (lockObject)
        {
            if (networkThreadData != null)
            {
                localCopy = networkThreadData;
                networkThreadData = null;
            }
        }
        if (localCopy != null)
        {
            // Создаём текстуру только в главном потоке
            Texture2D panoTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            panoTexture.LoadImage(localCopy);

            currentNetworkTexture = panoTexture;
            ApplyTexture(panoTexture);

            Debug.Log("Панорама обновлена!");
        }

        // Управление Skybox
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.wasPressedThisFrame)
            {
                ChangeLocalSkybox(-1);
            }
            if (keyboard.rightArrowKey.wasPressedThisFrame)
            {
                ChangeLocalSkybox(1);
            }
            if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            {
                ChangeLocalSkybox(1);
            }
            if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            {
                ChangeLocalSkybox(-1);
            }

            // Клавишная комбинация a1, b2 и т.д.
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
                if (!comboTriggered)
                {
                    string targetName = letter + digit;
                    bool found = false;
                    for (int i = 0; i < localTextures.Count; i++)
                    {
                        if (localTextures[i].name.ToLower() == targetName)
                        {
                            currentIndex = i;
                            ApplyTexture(localTextures[currentIndex]);
                            Debug.Log("Переключено на Skybox: " + targetName);
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        Debug.LogWarning("Skybox с именем " + targetName + " не найден!");
                    }
                    comboTriggered = true;
                }
            }
            else
            {
                comboTriggered = false;
            }
        }
    }

    //---------------------------------------------------------------------
    // Метод вызова при уничтожении объекта
    //---------------------------------------------------------------------
    private void OnDestroy()
    {
        isRunning = false;
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join();
        }
        if (netStream != null) netStream.Close();
        if (client != null) client.Close();
    }

    //---------------------------------------------------------------------
    // Загрузка локальных текстур
    //---------------------------------------------------------------------
    void LoadLocalTextures()
    {
        Texture2D[] loadedTextures = Resources.LoadAll<Texture2D>(folderName);
        localTextures.AddRange(loadedTextures);

        if (localTextures.Count == 0)
        {
            Debug.LogError("Нет текстур в папке Resources/" + folderName);
        }
    }

    //---------------------------------------------------------------------
    // Переключение на следующую / предыдущую локальную панораму
    //---------------------------------------------------------------------
    void ChangeLocalSkybox(int direction)
    {
        if (localTextures.Count == 0) return;

        currentIndex += direction;
        if (currentIndex < 0) currentIndex = localTextures.Count - 1;
        if (currentIndex >= localTextures.Count) currentIndex = 0;

        ApplyTexture(localTextures[currentIndex]);
    }

    //---------------------------------------------------------------------
    // Применение текстуры к Skybox
    //---------------------------------------------------------------------
    void ApplyTexture(Texture2D tex)
    {
        if (skyboxMaterial != null)
        {
            skyboxMaterial.SetTexture("_MainTex", tex);
            RenderSettings.skybox = skyboxMaterial;
            Debug.Log("Skybox обновлён: " + tex.name);
        }
        else
        {
            Debug.LogError("Skybox Material не назначен!");
        }
    }

    //---------------------------------------------------------------------
    // Фоновый поток, ожидающий данные о панораме от сервера
    //---------------------------------------------------------------------
    void ReceivePanorama()
    {
        try
        {
            Debug.Log("Поток приёма данных запущен. Ожидаем данные...");
            while (isRunning)
            {
                // Считываем 4 байта длины
                byte[] lengthBytes = new byte[4];
                int received = 0;

                while (received < 4)
                {
                    int r = netStream.Read(lengthBytes, received, 4 - received);
                    if (r <= 0) throw new Exception("Socket closed while reading length");
                    received += r;
                }

                int rawValue = BitConverter.ToInt32(lengthBytes, 0);
                int dataSize = IPAddress.NetworkToHostOrder(rawValue);
                if (dataSize <= 0) continue;

                // Читаем dataSize байт файла
                byte[] data = new byte[dataSize];
                int totalRead = 0;
                while (totalRead < dataSize)
                {
                    int r = netStream.Read(data, totalRead, dataSize - totalRead);
                    if (r <= 0)
                        throw new Exception("Socket closed while reading data");
                    totalRead += r;
                }

                // Передаём массив в Update
                lock (lockObject)
                {
                    networkThreadData = data;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Receive thread error: " + e);
            isRunning = false;

            // Если произошла ошибка — пробуем переподключиться
            Debug.LogWarning("Пробуем переподключиться через 5 секунд...");
            Thread.Sleep(reconnectTimeout);
            TryConnect();
        }
    }
}

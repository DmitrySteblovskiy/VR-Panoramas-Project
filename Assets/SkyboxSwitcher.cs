using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Oculus;
using System.Net;

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
    private bool isConnecting = false;
    private int reconnectTimeoutMs = 5000;

    private byte[] networkThreadData = null;
    private object lockObject = new object();
    private Texture2D currentNetworkTexture = null;

    void Start()
    {
        LoadLocalTextures();
        if (localTextures.Count > 0)
        {
            ApplyTexture(localTextures[currentIndex]);
        }
        // Запускаем соединение в отдельном потоке с ретраями
        StartConnection();
    }

    void StartConnection()
    {
        if (isConnecting) return;
        isConnecting = true;
        Thread connectThread = new Thread(ConnectionLoop);
        connectThread.IsBackground = true;
        connectThread.Start();
    }

    void ConnectionLoop()
    {
        int timeout = 50;
        int port = 63508;
        while (true)
        {
            string host = FindServerIp(port, timeout);
            if (string.IsNullOrEmpty(host))
            {
                Debug.LogWarning("Не удалось найти сервер, повторная попытка через 5 секунд.");
                Thread.Sleep(reconnectTimeoutMs);
                continue;
            }

            Debug.Log("Starting connection");
            bool connected = false;
            try
            {
                client = new TcpClient();
                client.Connect(host, port);
                netStream = client.GetStream();
                isRunning = true;
                connected = true;

                Debug.Log("Before receive");

                // Запускаем поток для получения панорамы
                receiveThread = new Thread(ReceivePanorama);
                receiveThread.IsBackground = true;
                receiveThread.Start();

                Debug.Log("Connected to " + host + ":" + port);

                // Ждём завершения потока чтения (он завершится при ошибке/разрыве)
                receiveThread.Join();
            }
            catch (Exception e)
            {
                Debug.LogError("Connection failed: " + e);
            }

            isRunning = false;
            if (netStream != null)
            {
                try { netStream.Close(); } catch { }
                netStream = null;
            }
            if (client != null)
            {
                try { client.Close(); } catch { }
                client = null;
            }
            if (connected)
                Debug.LogWarning("Потеря соединения, переподключение через 5 секунд...");

            Thread.Sleep(reconnectTimeoutMs);
        }
    }

    // Метод проверки открытого порта и поиска сервера (остальное — без изменений)
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
    string GetLocalIpPrefix()
    {
        try
        {
            var addresses = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (var addr in addresses)
            {
                if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                {
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
        return "";
    }
    string FindServerIp(int port, int timeoutMs)
    {
        string prefix = GetLocalIpPrefix();
        if (string.IsNullOrEmpty(prefix))
        {
            Debug.LogWarning("Не удалось определить префикс для локального IP");
            prefix = "192.168.0.";
        }
        Debug.Log("Сканируем с префиксом: " + prefix);
        for (int i = 1; i < 255; i++)
        {
            string testIp = prefix + i;
            if (IsPortOpen(testIp, port, timeoutMs))
            {
                Debug.Log("Найден сервер: " + testIp + ":" + port);
                return testIp;
            }
        }
        Debug.LogWarning("Сервер в подсети " + prefix + " не найден");
        return "";
    }

    void Update()
    {
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
            Texture2D panoTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            panoTexture.LoadImage(localCopy);
            currentNetworkTexture = panoTexture;
            ApplyTexture(panoTexture);
            Debug.Log("Панорама обновлена!");
        }

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

    void LoadLocalTextures()
    {
        Texture2D[] loadedTextures = Resources.LoadAll<Texture2D>(folderName);
        localTextures.AddRange(loadedTextures);

        if (localTextures.Count == 0)
        {
            Debug.LogError("Нет текстур в папке Resources/" + folderName);
        }
    }

    void ChangeLocalSkybox(int direction)
    {
        if (localTextures.Count == 0) return;

        currentIndex += direction;

        if (currentIndex < 0) currentIndex = localTextures.Count - 1;
        if (currentIndex >= localTextures.Count) currentIndex = 0;

        ApplyTexture(localTextures[currentIndex]);
    }

    void ApplyTexture(Texture2D tex)
    {
        if (skyboxMaterial != null)
        {
            skyboxMaterial.SetTexture("_MainTex", tex);
            RenderSettings.skybox = skyboxMaterial;
            Debug.Log("Skybox: " + tex.name);
        }
        else
        {
            Debug.LogError("Skybox Material не назначен!");
        }
    }

    void ReceivePanorama()
    {
        try
        {
            Debug.Log("Server says: ");
            while (isRunning)
            {
                byte[] lengthBytes = new byte[4];
                int received = 0;
                Debug.Log("Before the length: ");
                while (received < 4)
                {
                    int r = netStream.Read(lengthBytes, received, 4 - received);
                    if (r <= 0) throw new Exception("Socket closed while reading length");
                    received += r;
                    Debug.Log("In the length: " + r + " bytes read");
                }

                Debug.Log("After the length: ");
                int rawValue = BitConverter.ToInt32(lengthBytes, 0);
                int dataSize = System.Net.IPAddress.NetworkToHostOrder(rawValue);
                if (dataSize <= 0) continue;

                Debug.Log("Reading data: " + dataSize);
                byte[] data = new byte[dataSize];
                int totalRead = 0;
                while (totalRead < dataSize)
                {
                    int r = netStream.Read(data, totalRead, dataSize - totalRead);
                    if (r <= 0) throw new Exception("Socket closed while reading data");
                    totalRead += r;
                    Debug.Log("Read chunk: " + r + " total: " + totalRead);
                }

                Debug.Log("We received panorama!");
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
        }
    }
}

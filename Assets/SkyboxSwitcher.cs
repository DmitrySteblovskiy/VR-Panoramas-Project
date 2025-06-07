using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using System.Text;

public class SkyboxSwitcher : MonoBehaviour
{
    
    public Material skyboxMaterial;
    public string folderName = "Content";
    public TextMeshProUGUI connectionStatusText;

    
    public int crmPort = 63508;
    private const string WS_PATH = "/panoramas";
    private const int scanTimeoutMs = 50;
    private const int reconnectTimeoutMs = 5000;

    private readonly List<Texture2D> localTextures = new List<Texture2D>();
    private int currentIndex;

    private ClientWebSocket ws;
    private CancellationTokenSource cts;
    private Thread connectThread;

    private readonly object imgLock = new object();
    private byte incomingImage;
    private string incomingName;                 // имя панорамы для логов

    private string cacheFilePath;   // будет заполнено в Start
    private readonly object sendLock = new object();     // синхронизация отправок
                                                        
    private Texture2D _activeSkyTex;  // поле для хранения текущего skybox-текста



    private void Start()
    {
        //LoadLocalTextures();
        //if (localTextures.Count > 0) ApplyTexture(localTextures);

        cacheFilePath = Path.Combine(Application.persistentDataPath, "current_ip.txt");
        Debug.Log($"cache → файл будет храниться по пути: {cacheFilePath}");

        connectThread = new Thread(ConnectionLoop) { IsBackground = true };
        connectThread.Start();
    }

    private void Update()
    {
        byte img = null;
        string name = null;

        lock (imgLock)
        {
            if (incomingImage != null)
            {
                img = incomingImage;
                name = incomingName;
                incomingImage = null;
                incomingName = null;
            }
        }

        if (img == null) return;

        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        bool ok = tex.LoadImage(img);
        if (!ok)
        {
            Debug.LogError("unity → Texture2D.LoadImage() returned false");
            return;
        }

        tex.name = name;
        ApplyTexture(tex);
        Debug.Log($"unity → panorama \"{name}\" applied to skybox");

        // асинхронно шлём ACK не блокируя главный поток
        _ = Task.Run(async () =>
        {
            try
            {
                if (ws != null && ws.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
                    await ws.SendAsync(new ArraySegment<byte>(bytes),
                                       WebSocketMessageType.Text,
                                       true,
                                       CancellationToken.None);
                    Debug.Log($"unity → ACK отправлен: \"{name}\"");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"unity → ошибка отправки ACK: {e.Message}");
            }
        });
    }

    private void OnDestroy()
    {
        cts?.Cancel();
        ws?.Abort();
        connectThread?.Join();
    }

    private void ConnectionLoop()
    {
        while (true)
        {
            string host = LoadIpFromCache();
            if (!string.IsNullOrEmpty(host))
            {
                Debug.Log($"cache → пробуем IP из cache: {host}");
                if (!IsPortOpen(host, crmPort, scanTimeoutMs))
                {
                    Debug.Log("cache → IP недоступен, запускаем сканирование");
                    host = string.Empty;
                }
            }

            if (string.IsNullOrEmpty(host))
            {
                host = FindServerIp(crmPort, scanTimeoutMs);
                if (string.IsNullOrEmpty(host))
                {
                    Debug.LogWarning("CRM не найден, повтор через 5 сек.");
                    Thread.Sleep(reconnectTimeoutMs);
                    continue;
                }
                Debug.Log($"scan → найден CRM: {host}");
            }

            var uri = new Uri($"ws://{host}:{crmPort}{WS_PATH}");
            Debug.Log($"ws → попытка подключения: {uri}");

            using (ws = new ClientWebSocket())
            {
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                cts = new CancellationTokenSource();
                try
                {
                    ws.ConnectAsync(uri, cts.Token).Wait(cts.Token);

                    if (ws.State == WebSocketState.Open)
                    {
                        Debug.Log("ws → подключено!");
                        SaveIpToCache(host);
                        ReceiveLoop(ws, cts.Token).Wait();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"ws → error: {e}");
                }
            }

            Thread.Sleep(reconnectTimeoutMs);
        }
    }

    private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new ArraySegment<byte>(new byte);
        int msgIdx = 0;

        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;

            Debug.Log($"unity → awaiting frame #{msgIdx}");
            do
            {
                result = await socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                                             "Closed by client", token);
                    return;
                }
                ms.Write(buffer.Array, 0, result.Count);
                Debug.Log($"unity → chunk {result.Count} / fin={result.EndOfMessage}");
            } while (!result.EndOfMessage);

            var data = ms.ToArray();
            msgIdx++;

            if (data.Length < 4)
            {
                Debug.LogWarning("unity → получено меньше 4-х байт, игнор.");
                continue;
            }

            int nameLen = (data << 24) |
                           (data << 16) |
                           (data << 8) |
                           (data);         // big-endian int

            if (nameLen < 0 || 4 + nameLen > data.Length)
            {
                Debug.LogWarning($"unity → странная длина имени: {nameLen}");
                continue;
            }

            string panoName = Encoding.UTF8.GetString(data, 4, nameLen);
            int imgOffset = 4 + nameLen;
            int imgBytesLn = data.Length - imgOffset;

            if (imgBytesLn <= 0)
            {
                Debug.LogWarning("unity → в пакете нет байт картинки.");
                continue;
            }

            var imgBytes = new byte;
            Buffer.BlockCopy(data, imgOffset, imgBytes, 0, imgBytesLn);

            Debug.Log($"unity → frame {msgIdx - 1}: name=\"{panoName}\", bytes={imgBytesLn}");

            lock (imgLock)
            {
                incomingImage = imgBytes;
                incomingName = panoName;
            }
        }
    }

    private void SaveIpToCache(string ip)
    {
        if (string.IsNullOrEmpty(cacheFilePath)) return;
        try
        {
            File.WriteAllText(cacheFilePath, ip);
            Debug.Log($"cache → IP сохранён: {ip}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"cache → ошибка записи: {e.Message}");
        }
    }

    private string LoadIpFromCache()
    {
        if (string.IsNullOrEmpty(cacheFilePath)) return string.Empty;
        try
        {
            if (File.Exists(cacheFilePath))
            {
                string ip = File.ReadAllText(cacheFilePath).Trim();
                Debug.Log($"cache → из файла получен IP: {ip}");
                return ip;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"cache → ошибка чтения: {e.Message}");
        }
        return string.Empty;
    }

    private static bool IsPortOpen(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var ar = client.BeginConnect(host, port, null, null);
            return ar.AsyncWaitHandle.WaitOne(timeoutMs) && client.Connected;
        }
        catch { return false; }
    }

    private static string GetLocalIpPrefix()
    {
        foreach (var addr in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
            {
                var p = addr.ToString().Split('.');
                return $"{p}.{p}.{p}.";
            }
        }
        return string.Empty;
    }

    private static string FindServerIp(int port, int timeoutMs)
    {
        string prefix = GetLocalIpPrefix();
        if (string.IsNullOrEmpty(prefix)) prefix = "192.168.0.";

        for (int i = 1; i < 255; i++)
        {
            var ip = prefix + i;
            if (IsPortOpen(ip, port, timeoutMs)) return ip;
        }
        return string.Empty;
    }

    private void LoadLocalTextures()
    {
        localTextures.AddRange(Resources.LoadAll<Texture2D>(folderName));
        if (localTextures.Count == 0)
            Debug.LogWarning($"Нет текстур в Resources/{folderName}");
    }

    private void ApplyTexture(Texture2D tex)
    {
        if (_activeSkyTex != null)
            Destroy(_activeSkyTex);            // освобождаем старый GPU-объект

        _activeSkyTex = tex;

        if (skyboxMaterial == null)
        {
            Debug.LogError("Skybox material not set");
            return;
        }
        skyboxMaterial.SetTexture("_MainTex", tex);
        RenderSettings.skybox = skyboxMaterial;

        // убираем копию из системной памяти
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
    }
}

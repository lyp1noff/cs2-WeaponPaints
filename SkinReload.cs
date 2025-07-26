using System.Net;
using System.Text;
using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace WeaponPaints;

internal class SkinReloadListener
{
    private readonly ConcurrentQueue<string> _pendingSteamIds = new();
    private HttpListener? _listener;

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://*:8080/");
        _listener.Start();
        Task.Run(() => ListenAsync());
        Console.WriteLine("[SkinReload] HTTP listener started on port 8080.");
        WeaponPaints.Instance.RegisterListener<Listeners.OnTick>(OnTick);
    }

    public void Stop()
    {
        _listener?.Stop();
        _listener = null;
    }

    private void OnTick()
    {
        while (_pendingSteamIds.TryDequeue(out var steamId))
        {
            ReloadForSteamId(steamId);
        }
    }

    private void ReloadForSteamId(string steamId)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.PlayerConnected)
                continue;

            if (player.SteamID.ToString() == steamId)
            {
                Console.WriteLine($"[SkinReload] Executing refresh for {player.PlayerName}");
                WeaponPaints.Instance.OnCommandRefresh(player, null);
                return;
            }
        }

        Console.WriteLine($"[SkinReload] No player found with SteamID: {steamId}");
    }

    private async Task ListenAsync()
    {
        while (_listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/update_skins")
                {
                    string steamId = request.QueryString["steamid"] ?? "";
                    if (string.IsNullOrEmpty(steamId))
                    {
                        response.StatusCode = 400;
                        byte[] errorMsg = Encoding.UTF8.GetBytes("Missing steamid parameter.");
                        await response.OutputStream.WriteAsync(errorMsg);
                        response.OutputStream.Close();
                        continue;
                    }

                    _pendingSteamIds.Enqueue(steamId);

                    string responseText = $"Queued !wp for {steamId}\n";
                    byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer);
                    response.OutputStream.Close();
                }
                else
                {
                    response.StatusCode = 404;
                    response.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SkinReload] Error: " + ex.Message);
            }
        }
    }
}

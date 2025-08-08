using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fleck;

namespace frogger_mars.Model;

public class DataVisualizationServer
{
    public volatile int CurrentTick = 1;
    private static IWebSocketConnection _client;
    private static WebSocketServer _server;
    private static CancellationTokenSource _cts;
    private static Task _serverTask;
    private static string _lastMessage = string.Empty;
    public frogAgent Frog { set; get; }
    private int _lastInputTick = -1;


    public DataVisualizationServer()
    {
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _server = new WebSocketServer("ws://127.0.0.1:8181");

        _server.Start(socket =>
        {
            socket.OnOpen = () => { _client = socket; };

            socket.OnMessage = message =>
            {
                try
                {
                    var s = message?.Trim();
                    Console.WriteLine($"[WS] raw: {s}");

                    // 1) Bare or quoted number (ACK)
                    if (TryParseTick(s, out var tick))
                    {
                        if (tick != CurrentTick + 1)
                        {
                            _client?.Send(_lastMessage);
                            return;
                        }

                        CurrentTick = tick;
                        return;
                    }

                    // 2) JSON object (input)
                    var json = JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(s);

                    if (json.TryGetValue("type", out var typeEl) && typeEl.GetString() == "input")
                    {
                        var agentId = json["agent_id"].GetInt32();
                        var direction = json["direction"].GetString();

                        // one-input-per-tick guard (optional)
                        if (_lastInputTick == CurrentTick) return;
                        _lastInputTick = CurrentTick;

                        Console.WriteLine($"📥 Input received: Agent {agentId}, Direction: {direction}");

                        switch (direction)
                        {
                            case "up": Frog.Position.Y -= 1; break;
                            case "down": Frog.Position.Y += 1; break;
                            case "left": Frog.Position.X -= 1; break;
                            case "right": Frog.Position.X += 1; break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("❌ Error parsing WebSocket message: " + ex.Message);
                }
            };

            bool TryParseTick(string s, out int tick)
            {
                tick = default;

                // bare int
                if (int.TryParse(s, out tick)) return true;

                // bare float, z.B. 2.0
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var dbl))
                {
                    tick = (int)dbl;
                    return true;
                }

                // quoted int oder float
                if (s?.Length >= 2 && s[0] == '"' && s[^1] == '"')
                {
                    var inner = s.Substring(1, s.Length - 2);
                    if (int.TryParse(inner, out tick)) return true;
                    if (double.TryParse(inner, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out dbl))
                    {
                        tick = (int)dbl;
                        return true;
                    }
                }

                return false;
            }


            socket.OnClose = () => { _client = null; };
        });

        while (!token.IsCancellationRequested)
        {
            Thread.Sleep(100);
        }
    }

    public void RunInBackground()
    {
        _serverTask = Task.Run(() => Start());
    }

    public void Stop()
    {
        if (_cts == null)
            return;

        if (_client != null && _client.IsAvailable)
        {
            _client.Send("close");
            Thread.Sleep(100);
        }

        _cts.Cancel();
        _serverTask?.Wait();
        _server?.Dispose();

        _cts.Dispose();
        _cts = null;
        _serverTask = null;
        _server = null;
        _client = null;
    }

    public void SendData()
    {
        // Build a tiny payload with only the next tick number
        //TODO: send actual data to client
        var payload = new
        {
            expectingTick = CurrentTick + 1,
            agents = new object[]
            {
                new
                {
                    id = Frog.AgentId,
                    breed = "frog",
                    x = Frog.Position.X,
                    y = Frog.Position.Y,
                    heading = 0
                },
                new
                {
                    id = 2,
                    breed = "car",
                    x = 1,
                    y = 14,
                    heading = 90
                },
                new
                {
                    id = 3,
                    breed = "car",
                    x = 2,
                    y = 13,
                    heading = -90
                },
                new
                {
                    id = 5,
                    breed = "car",
                    x = 1,
                    y = 12,
                    heading = 90
                },
                new
                {
                    id = 6,
                    breed = "car",
                    x = 1,
                    y = 11,
                    heading = 90
                },
                new
                {
                    id = 7,
                    breed = "car",
                    x = 1,
                    y = 10,
                    heading = 90
                },
            }
        };
        _lastMessage = JsonSerializer.Serialize(payload);
        _client?.Send(_lastMessage);
    }


    public bool Connected()
    {
        return _client != null && _client.IsAvailable;
    }
}
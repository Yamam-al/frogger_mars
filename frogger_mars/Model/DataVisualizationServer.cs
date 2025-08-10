﻿using System;
using System.Collections.Generic;
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

    public FrogAgent Frog { set; get; }
    public List<CarAgent> Cars { set; get; }
    public List<TruckAgent> Trucks { set; get; }
    public List<LogAgent> Logs { set; get; }
    public List<TurtleAgent> Turtles { set; get; }
    public List<PadAgent> Pads { set; get; }

    // --- Anti-double-input Guards ---
    private int  _lastInputTick = -1;           // legacy guard (belassen)
    private bool _acceptedInputThisTick = false; // NEW: hartes „nur 1 Input pro Tick“

    // --- Start-Gate ---
    private readonly ManualResetEventSlim _startGate = new(false);
    public bool Started => _startGate.IsSet;
    public void WaitForStart(CancellationToken? token = null)
    {
        if (!Started) _startGate.Wait(token ?? CancellationToken.None);
    }
    public void ResetStartGate() => _startGate.Reset();

    // --- Pause/Resume ---
    private readonly ManualResetEventSlim _pauseGate = new(true); // true = nicht pausiert
    public bool Paused => !_pauseGate.IsSet;
    public void WaitWhilePaused() => _pauseGate.Wait();
    private void Pause()  => _pauseGate.Reset();
    private void Resume() => _pauseGate.Set();

    // --- UI-State ---
    private readonly List<int> _removeIds = new();
    private int _lives = 5;
    private bool _gameOverFlag = false;

    public void EnqueueRemoval(int id) { lock (_removeIds) _removeIds.Add(id); }
    public void SetLives(int v) => _lives = v;
    public void SetGameOver(bool v) => _gameOverFlag = v;
    public void ClearPendingRemovals(){ lock (_removeIds) _removeIds.Clear(); }

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

                    // 1) Bare/quoted number (ACK)
                    if (TryParseTick(s, out var tick))
                    {
                        if (tick != CurrentTick + 1)
                        {
                            _client?.Send(_lastMessage);
                            return;
                        }
                        CurrentTick = tick;
                        // NEW: neuer Tick -> wieder genau 1 Input erlauben
                        _acceptedInputThisTick = false;
                        return;
                    }

                    // 2) JSON object (input/control)
                    var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(s);

                    if (json.TryGetValue("type", out var typeEl))
                    {
                        var t = typeEl.GetString();

                        if (t == "control" && json.TryGetValue("cmd", out var cmdEl))
                        {
                            var cmd = cmdEl.GetString();
                            switch (cmd)
                            {
                                case "start":
                                    _startGate.Set();
                                    Console.WriteLine("[Viz] START received");
                                    _client?.Send("{\"ack\":\"started\"}");
                                    return;

                                case "pause":
                                    Pause();
                                    Console.WriteLine("[Viz] PAUSE received");
                                    _client?.Send("{\"ack\":\"paused\"}");
                                    return;

                                case "resume":
                                    Resume();
                                    Console.WriteLine("[Viz] RESUME received");
                                    _client?.Send("{\"ack\":\"resumed\"}");
                                    return;

                                case "restart":
                                    _gameOverFlag = false;
                                    ResetStartGate(); // PreTick blockt wieder
                                    Console.WriteLine("[Viz] RESTART requested");
                                    return;
                            }
                        }

                        if (t == "input")
                        {
                            var direction = json["direction"].GetString();

                            // NEW: harte Sperre – nur 1 Input pro Tick
                            if (_acceptedInputThisTick) return;

                            // legacy guard (zusätzlich ok)
                            if (_lastInputTick == CurrentTick) return;

                            _lastInputTick = CurrentTick;
                            _acceptedInputThisTick = true;

                            if (Frog == null) return;

                            // Queue „entmüllen“: max. 1 Input im Puffer
                            while (Frog.InputQueue.Count > 0) Frog.InputQueue.TryDequeue(out _);

                            switch (direction)
                            {
                                case "up":    Frog.InputQueue.Enqueue(FrogInput.Up);    break;
                                case "down":  Frog.InputQueue.Enqueue(FrogInput.Down);  break;
                                case "left":  Frog.InputQueue.Enqueue(FrogInput.Left);  break;
                                case "right": Frog.InputQueue.Enqueue(FrogInput.Right); break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("❌ Error parsing WebSocket message: " + ex.Message);
                }

                bool TryParseTick(string s, out int tick)
                {
                    tick = default;

                    if (int.TryParse(s, out tick)) return true;

                    if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var dbl))
                    {
                        tick = (int)dbl; return true;
                    }

                    if (s?.Length >= 2 && s[0] == '"' && s[^1] == '"')
                    {
                        var inner = s.Substring(1, s.Length - 2);
                        if (int.TryParse(inner, out tick)) return true;
                        if (double.TryParse(inner, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out dbl))
                        { tick = (int)dbl; return true; }
                    }
                    return false;
                }
            };

            socket.OnClose = () => { _client = null; };
        });

        while (!token.IsCancellationRequested)
            Thread.Sleep(100);
    }

    public void RunInBackground() => _serverTask = Task.Run(() => Start());

    public void Stop()
    {
        if (_cts == null) return;

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
        var list = new List<object>();

        // Frog nur wenn vorhanden – inkl. Jumps
        if (Frog != null)
        {
            list.Add(new {
                id = Frog.AgentId,
                breed = Frog.Breed,
                x = Frog.Position.X,
                y = Frog.Position.Y,
                heading = 0,
                jumps = Frog.Jumps
            });
        }

        foreach (var car in Cars)
            list.Add(new { id = car.AgentId, breed = car.Breed, x = car.Position.X, y = car.Position.Y, heading = car.Heading });

        foreach (var truck in Trucks)
            list.Add(new { id = truck.AgentId, breed = truck.Breed, x = truck.Position.X, y = truck.Position.Y, heading = truck.Heading });

        foreach (var log in Logs)
            list.Add(new { id = log.AgentId, breed = log.Breed, x = log.Position.X, y = log.Position.Y, heading = log.Heading });

        foreach (var turtle in Turtles)
            list.Add(new { id = turtle.AgentId, breed = turtle.Breed, x = turtle.Position.X, y = turtle.Position.Y, heading = turtle.Heading, hidden = turtle.Hidden });

        foreach (var pad in Pads)
            list.Add(new { id = pad.AgentId, breed = pad.Breed, x = pad.Position.X, y = pad.Position.Y, heading = pad.Heading, occupied = pad.Occupied });

        int[] removeIds;
        lock (_removeIds){ removeIds = _removeIds.ToArray(); _removeIds.Clear(); }

        var payload = new
        {
            expectingTick = CurrentTick + 1,
            lives = _lives,
            gameOver = _gameOverFlag,
            removeIds,
            agents = list
        };

        _lastMessage = JsonSerializer.Serialize(payload);
        Console.WriteLine($"[WS] Sending data: {_lastMessage}");
        _client?.Send(_lastMessage);
    }

    public bool Connected() => _client != null && _client.IsAvailable;
}

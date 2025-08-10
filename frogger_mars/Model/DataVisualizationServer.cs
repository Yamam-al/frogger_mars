﻿using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fleck;

namespace frogger_mars.Model;

/// <summary>
/// WebSocket bridge between the simulation (MARS) and the Godot frontend.
/// Handles start/pause/resume/restart, input events, and streams the full state each tick.
/// </summary>
public class DataVisualizationServer
{
    /// <summary>Tick the client should ACK with (server expects +1 each loop).</summary>
    public volatile int CurrentTick = 1;

    private static IWebSocketConnection _client;
    private static WebSocketServer _server;
    private static CancellationTokenSource _cts;
    private static Task _serverTask;
    private static string _lastMessage = string.Empty;

    /// <summary>Reference to the single active frog (null if game over).</summary>
    public FrogAgent Frog { set; get; }

    /// <summary>References to all car agents.</summary>
    public List<CarAgent> Cars { set; get; }

    /// <summary>References to all truck agents.</summary>
    public List<TruckAgent> Trucks { set; get; }

    /// <summary>References to all log agents.</summary>
    public List<LogAgent> Logs { set; get; }

    /// <summary>References to all turtle agents.</summary>
    public List<TurtleAgent> Turtles { set; get; }

    /// <summary>References to all pad agents (goal slots).</summary>
    public List<PadAgent> Pads { set; get; }

    // --- Anti-double-input Guards ---
    private int  _lastInputTick = -1;            // legacy guard
    private bool _acceptedInputThisTick = false; // only one input per tick

    // --- Time ---
    private int _timeLeft = 0;                           // shown to Godot
    /// <summary>Configurable initial time limit per life (settable via Godot control).</summary>
    public  int StartTimeSeconds { get; private set; } = 60; // set from Godot
    /// <summary>Update the remaining time that the client will display.</summary>
    public void SetTimeLeft(int v) => _timeLeft = Math.Max(0, v);

    // --- Lives/Level ---
    /// <summary>Configurable number of lives to start with (settable via Godot).</summary>
    public int StartLives { get; private set; } = 5;     // set from Godot
    /// <summary>Configurable level index to spawn (1-based; settable via Godot).</summary>
    public int StartLevel { get; private set; } = 1;     // set from Godot (1-based)

    // --- Start-Gate ---
    private readonly ManualResetEventSlim _startGate = new(false);
    /// <summary>True once a "start" control message has been received.</summary>
    public bool Started => _startGate.IsSet;

    /// <summary>
    /// Blocks the caller until a "start" command has been received from the client.
    /// </summary>
    /// <param name="token">Optional cancellation token.</param>
    public void WaitForStart(CancellationToken? token = null)
    {
        if (!Started) _startGate.Wait(token ?? CancellationToken.None);
    }

    /// <summary>
    /// Resets the start gate so the simulation waits for another "start" signal.
    /// </summary>
    public void ResetStartGate() => _startGate.Reset();

    // --- Pause/Resume ---
    private readonly ManualResetEventSlim _pauseGate = new(true); // true = not paused
    /// <summary>True if the simulation is paused.</summary>
    public bool Paused => !_pauseGate.IsSet;

    /// <summary>Blocks while paused until a "resume" command arrives.</summary>
    public void WaitWhilePaused() => _pauseGate.Wait();

    /// <summary>Sets the pause state.</summary>
    private void Pause()  => _pauseGate.Reset();

    /// <summary>Clears the pause state.</summary>
    private void Resume() => _pauseGate.Set();

    // --- UI-State ---
    private readonly List<int> _removeIds = new();
    private int _lives = 5;

    /// <summary>Adds an id to be removed by the Godot client on the next frame.</summary>
    public void EnqueueRemoval(int id) { lock (_removeIds) _removeIds.Add(id); }

    /// <summary>Updates the life counter the client shows.</summary>
    public void SetLives(int v) => _lives = v;

    private bool _gameOverFlag = false;
    private bool _gameWonFlag = false;

    /// <summary>Sets the game-over flag (read by Godot HUD).</summary>
    public void SetGameOver(bool v) => _gameOverFlag = v;

    /// <summary>Sets the game-won flag (read by Godot HUD).</summary>
    public void SetGameWon(bool v)  => _gameWonFlag  = v;

    /// <summary>Clears the removal queue.</summary>
    public void ClearPendingRemovals(){ lock (_removeIds) _removeIds.Clear(); }

    /// <summary>
    /// Starts the WebSocket server and processes client messages (blocking).
    /// </summary>
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
                        _acceptedInputThisTick = false; // new tick => allow 1 input again
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
                                    _gameWonFlag  = false;
                                    ResetStartGate(); // PreTick blocks again
                                    Console.WriteLine("[Viz] RESTART requested]");
                                    // mirror lives in HUD on restart
                                    SetLives(StartLives);
                                    return;

                                case "set_start_time":
                                    if (json.TryGetValue("value", out var v) && v.ValueKind == JsonValueKind.Number)
                                    {
                                        var seconds = Math.Clamp(v.GetInt32(), 1, 999);
                                        StartTimeSeconds = seconds;
                                        Console.WriteLine($"[Viz] Start time set to {StartTimeSeconds}s");
                                    }
                                    return;

                                case "set_start_lives":
                                    if (json.TryGetValue("value", out var liv) && liv.ValueKind == JsonValueKind.Number)
                                    {
                                        var lives = Math.Clamp(liv.GetInt32(), 1, 99);
                                        StartLives = lives;
                                        SetLives(StartLives); // optionally mirror directly to HUD
                                        Console.WriteLine($"[Viz] Start lives set to {StartLives}");
                                    }
                                    return;

                                case "set_start_level":
                                    if (json.TryGetValue("value", out var lvl) && lvl.ValueKind == JsonValueKind.Number)
                                    {
                                        var level = Math.Clamp(lvl.GetInt32(), 1, 99);
                                        StartLevel = level;
                                        Console.WriteLine($"[Viz] Start level set to {StartLevel}");
                                    }
                                    return;
                            }
                        }

                        if (t == "input")
                        {
                            var direction = json["direction"].GetString();

                            // one input per tick max
                            if (_acceptedInputThisTick) return;
                            if (_lastInputTick == CurrentTick) return;

                            _lastInputTick = CurrentTick;
                            _acceptedInputThisTick = true;

                            if (Frog == null) return;

                            // keep queue tight: allow only the latest input
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

                // Local function that parses ACK-like tick values (number or quoted number).
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

        // Keep the thread alive while running.
        while (!token.IsCancellationRequested)
            Thread.Sleep(100);
    }

    /// <summary>
    /// Starts the WebSocket loop on a background task.
    /// </summary>
    public void RunInBackground() => _serverTask = Task.Run(() => Start());

    /// <summary>
    /// Stops the WebSocket server and cleans up resources.
    /// </summary>
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

    /// <summary>
    /// Serializes the current agent state and pushes it to the client.
    /// Also caches the payload so we can re-send it when the ACK is unexpected.
    /// </summary>
    public void SendData()
    {
        var list = new List<object>();

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
            timeLeft = _timeLeft,
            gameOver = _gameOverFlag,
            gameWon  = _gameWonFlag,
            removeIds,
            agents = list
        };

        _lastMessage = JsonSerializer.Serialize(payload);
        Console.WriteLine($"[WS] Sending data: {_lastMessage}");
        _client?.Send(_lastMessage);
    }

    /// <summary>
    /// Returns whether a WebSocket client is currently connected.
    /// </summary>
    public bool Connected() => _client != null && _client.IsAvailable;
}

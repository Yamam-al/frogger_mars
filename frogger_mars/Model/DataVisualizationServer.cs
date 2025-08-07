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
            socket.OnOpen = () =>
            {
                _client = socket; 
            };

            socket.OnMessage = message =>
            {
                try
                {
                    if (int.TryParse(message, out var tick))
                    {
                        if (tick != CurrentTick + 1)
                        {
                            _client?.Send(_lastMessage);
                            return;
                        }
                        CurrentTick = tick;
                        return;
                    }

                    // Versuche, JSON zu parsen
                    var json = JsonSerializer.Deserialize<Dictionary<string, object>>(message);

                    if (json != null && json.ContainsKey("type") && json["type"]?.ToString() == "input")
                    {
                        if (_lastInputTick == CurrentTick)
                        {
                            Console.WriteLine("⚠️ Already received input this tick – ignoring.");
                            return;
                        }

                        _lastInputTick = CurrentTick;
                        
                        var agentElement = (JsonElement)json["agent_id"];
                        var directionElement = (JsonElement)json["direction"];

                        int agentId = agentElement.GetInt32();
                        string direction = directionElement.GetString();


                        // TODO: Hier deinen Input an das Agenten-Objekt weiterleiten
                        Console.WriteLine($"📥 Input received: Agent {agentId}, Direction: {direction}");

                        switch (direction)
                        {
                            case "up" :
                                Frog.Position.Y -= 1;
                                break;
                            case "down" :
                                Frog.Position.Y += 1;
                                break;
                            case "left" :
                                Frog.Position.X -= 1;
                                break;
                            case "right" :
                                Frog.Position.X += 1;
                                break;
                        }
                        
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("❌ Error parsing WebSocket message: " + ex.Message);
                }
            };

            socket.OnClose = () =>
            {
                _client = null;
            };
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
                    id      = Frog.AgentId,
                    breed   = "frog",    
                    x       = Frog.Position.X,          
                    y       = Frog.Position.Y,
                    heading = 0
                },
                new
                {
                    id      = 2,
                    breed   = "car",    
                    x       = 1,          
                    y       = 14,
                    heading = 90
                },
                new
                {
                    id      = 3,
                    breed   = "car",    
                    x       = 2,          
                    y       = 13,
                    heading = -90
                },
                new
                {
                    id      = 5,
                    breed   = "car",    
                    x       = 1,          
                    y       = 12,
                    heading = 90
                },
                new
                {
                    id      = 6,
                    breed   = "car",    
                    x       = 1,          
                    y       = 11,
                    heading = 90
                },
                new
                {
                    id      = 7,
                    breed   = "car",    
                    x       = 1,          
                    y       = 10,
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
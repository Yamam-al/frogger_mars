using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mars.Components.Layers;
using Mars.Core.Data;
using Mars.Interfaces.Annotations;
using Mars.Interfaces.Data;
using Mars.Interfaces.Environments;
using Mars.Interfaces.Layers;

namespace frogger_mars.Model
{
    public class MyGridLayer : RasterLayer, ISteppedActiveLayer
    {
        [PropertyDescription] public bool Visualization { get; set; }
        [PropertyDescription] public int VisualizationTimeout { get; set; }

        public List<CarAgent> Cars = new();
        public List<TruckAgent> Trucks = new();
        public List<TurtleAgent> Turtles = new();
        public List<LogAgent> Logs = new();
        public List<PadAgent> Pads = new();

        private readonly DataVisualizationServer _dataVisualizationServer = new();

        private List<FrogAgent> _frogs;
        public FrogAgent ActiveFrog;
        private int _activeFrogIndex = 0;
        private Position _frogStart;
        private int _lives = 5;

        private bool _gameOver = false;

        public override bool InitLayer(LayerInitData layerInitData, RegisterAgent registerAgentHandle, UnregisterAgent unregisterAgentHandle)
        {
            // base init
            base.InitLayer(layerInitData, registerAgentHandle, unregisterAgentHandle);

            // spawn
            var agentManager = layerInitData.Container.Resolve<IAgentManager>();
            _frogs  = agentManager.Spawn<FrogAgent, MyGridLayer>().ToList();
            Cars    = agentManager.Spawn<CarAgent, MyGridLayer>().ToList();
            Trucks  = agentManager.Spawn<TruckAgent, MyGridLayer>().ToList();
            Turtles = agentManager.Spawn<TurtleAgent, MyGridLayer>().ToList();
            Logs    = agentManager.Spawn<LogAgent, MyGridLayer>().ToList();
            Pads    = agentManager.Spawn<PadAgent, MyGridLayer>().ToList();

            // IDs
            var id = 0;
            foreach (var f in _frogs)  f.AgentId = id++;
            foreach (var c in Cars)    c.AgentId = id++;
            foreach (var t in Trucks)  t.AgentId = id++;
            foreach (var tu in Turtles)tu.AgentId = id++;
            foreach (var lg in Logs)   lg.AgentId = id++;
            foreach (var p in Pads)    p.AgentId = id++;

            Console.WriteLine($"We created {_frogs.Count} frog agents.");
            Console.WriteLine($"We created {Cars.Count} car agents.");
            Console.WriteLine($"We created {Trucks.Count} truck agents.");
            Console.WriteLine($"We created {Turtles.Count} turtle agents.");
            Console.WriteLine($"We created {Logs.Count} log agents.");
            Console.WriteLine($"We created {Pads.Count} pad agents.");

            ActiveFrog = _frogs[_activeFrogIndex];

            // Grid lesen
            var frogPlaced   = false;
            var carSpots0    = new List<Position>();
            var carSpots180  = new List<Position>();
            var truckSpots0  = new List<Position>();
            var truckSpots180= new List<Position>();
            var turtleSpots  = new List<Position>();
            var logSpots     = new List<Position>();
            var padSpots     = new List<Position>();

            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var value = this[x, y];
                switch (value)
                {
                    case 1.0:
                        if (!frogPlaced)
                        {
                            ActiveFrog.Position = Position.CreatePosition(x, y);
                            _frogStart = Position.CreatePosition(x, y);
                            frogPlaced = true;
                            Console.WriteLine($"Frog start position set to: ({x}, {y})");
                        }
                        break;
                    case 2.0: truckSpots0.Add(Position.CreatePosition(x, y)); break;
                    case 3.0: truckSpots180.Add(Position.CreatePosition(x, y)); break;
                    case 4.0: carSpots0.Add(Position.CreatePosition(x, y)); break;
                    case 5.0: carSpots180.Add(Position.CreatePosition(x, y)); break;
                    case 6.0: logSpots.Add(Position.CreatePosition(x, y)); break;
                    case 7.0: turtleSpots.Add(Position.CreatePosition(x, y)); break;
                    case 8.0: padSpots.Add(Position.CreatePosition(x, y)); break;
                }
            }

            // assign cars
            var ci = 0;
            foreach (var p in carSpots0)
            {
                if (ci >= Cars.Count) break;
                Cars[ci].Position = p; Cars[ci].Heading = 90; ci++;
            }
            foreach (var p in carSpots180)
            {
                if (ci >= Cars.Count) break;
                Cars[ci].Position = p; Cars[ci].Heading = -90; ci++;
            }

            // assign trucks
            var ti = 0;
            foreach (var p in truckSpots0)
            {
                if (ti >= Trucks.Count) break;
                Trucks[ti].Position = p; Trucks[ti].Heading = 0; ti++;
            }
            foreach (var p in truckSpots180)
            {
                if (ti >= Trucks.Count) break;
                Trucks[ti].Position = p; Trucks[ti].Heading = 180; ti++;
            }

            // assign logs
            var li = 0;
            foreach (var p in logSpots)
            {
                if (li >= Logs.Count) break;
                Logs[li].Position = p; li++;
            }

            // assign turtles
            var tri = 0;
            foreach (var p in turtleSpots)
            {
                if (tri >= Turtles.Count) break;
                Turtles[tri].Position = p; tri++;
            }

            // assign pads
            var pi = 0;
            foreach (var p in padSpots)
            {
                if (pi >= Pads.Count) break;
                Pads[pi].Position = p; pi++;
            }

            Console.WriteLine($"Placed {ci}/{Cars.Count} cars, {ti}/{Trucks.Count} trucks, {li}/{Logs.Count} logs, {tri}/{Turtles.Count} turtles and {pi}/{Pads.Count} pads, from grid.");

            // Viz server
            _dataVisualizationServer.Frog   = ActiveFrog;
            _dataVisualizationServer.Cars   = Cars;
            _dataVisualizationServer.Trucks = Trucks;
            _dataVisualizationServer.Logs   = Logs;
            _dataVisualizationServer.Turtles= Turtles;
            _dataVisualizationServer.Pads   = Pads;
            _dataVisualizationServer.RunInBackground();

            return true;
        }

        public void PreTick()
        {
            // Nach GameOver: wir haben das StartGate bei GameOver geschlossen.
            // Hier warten wir erneut auf START, setzen danach den Spielzustand frisch.
            if (!_dataVisualizationServer.Started || _gameOver)
            {
                Console.WriteLine("[Viz] Waiting for START from client…");
                _dataVisualizationServer.WaitForStart();

                ResetGameState();

                Console.WriteLine("[Viz] START received, beginning simulation.");
            }

            if (_dataVisualizationServer.Paused)
                Console.WriteLine("[Viz] Simulation paused — waiting for RESUME…");

            _dataVisualizationServer.WaitWhilePaused();
        }

        public void Tick()
        {
            if (_gameOver || ActiveFrog == null) return;

            if (CollidesWithVehicle(ActiveFrog.Position))
            {
                _lives--;
                var oldId = ActiveFrog.AgentId;

                _activeFrogIndex++;
                if (_activeFrogIndex < _frogs.Count)
                {
                    ActiveFrog = _frogs[_activeFrogIndex];
                    ActiveFrog.Position = _frogStart;

                    _dataVisualizationServer.EnqueueRemoval(oldId);
                    _dataVisualizationServer.Frog = ActiveFrog;
                    _dataVisualizationServer.SetLives(_lives);
                }
                else
                {
                    // --- GAME OVER ---
                    Console.WriteLine("[Game] No frogs left — game over!");
                    _gameOver = true;

                    _dataVisualizationServer.EnqueueRemoval(oldId);
                    _dataVisualizationServer.SetLives(_lives);
                    _dataVisualizationServer.SetGameOver(true);
                    _dataVisualizationServer.Frog = null;   // nichts mehr senden

                    // WICHTIG: Start-Gate schließen → nächster PreTick blockt bis neuer START
                    _dataVisualizationServer.ResetStartGate();
                }
            }
        }

        public void PostTick()
        {
            Console.WriteLine($"[Viz] Waiting for client at tick {Context.CurrentTick}");
            while (!_dataVisualizationServer.Connected())
                Thread.Sleep(1000);

            Console.WriteLine($"[Viz] Sending data for tick {Context.CurrentTick}");
            _dataVisualizationServer.SendData();

            while (_dataVisualizationServer.CurrentTick != Context.CurrentTick + 1)
                Thread.Sleep(VisualizationTimeout);
        }

        private bool CollidesWithVehicle(Position p)
        {
            return Cars.Any(c => c.Position.Equals(p)) ||
                   Trucks.Any(t => t.Position.Equals(p));
        }

        // Setzt Spiel zurück, nachdem ein neuer START kam
        private void ResetGameState()
        {
            Console.WriteLine("[Game] ResetGameState()");

            _lives = 5;
            _gameOver = false;

            _activeFrogIndex = 0;
            ActiveFrog = _frogs[_activeFrogIndex];

            // zurück auf Startfeld
            ActiveFrog.Position = _frogStart;

            // Visualisierung säubern/setzen
            _dataVisualizationServer.ClearPendingRemovals();
            _dataVisualizationServer.SetGameOver(false);
            _dataVisualizationServer.SetLives(_lives);
            _dataVisualizationServer.Frog = ActiveFrog;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mars.Components.Layers;
using Mars.Core.Data;
using Mars.Interfaces.Annotations;
using Mars.Interfaces.Layers;
using Mars.Interfaces.Data;
using Mars.Interfaces.Environments;

namespace frogger_mars.Model
{
    /// <summary>
    ///     A simple grid layer that access to the values of a grid.
    /// </summary>
    public class MyGridLayer : RasterLayer, ISteppedActiveLayer
    {
        [PropertyDescription] public bool Visualization { get; set; }
        [PropertyDescription] public int VisualizationTimeout { get; set; }

        public List<CarAgent> Cars = new();
        public List<TruckAgent> Trucks = new();
        public List<TurtleAgent> Turtles = new();
        public List<LogAgent> Logs = new();
        public List<PadAgent> Pads = new();
        DataVisualizationServer _dataVisualizationServer = new DataVisualizationServer();
        
        private List<FrogAgent> _frogs;
        public FrogAgent ActiveFrog;
        private int _activeFrogIndex = 0;
        private Position _frogStart;
        private int _lives = 5;
        
        private bool _gameOver = false;
        private LayerInitData _initDataCache; // speichert Init-Daten für Restart


        public override bool InitLayer(LayerInitData layerInitData, RegisterAgent registerAgentHandle,
            UnregisterAgent unregisterAgentHandle)
        {
            _initDataCache = layerInitData;
            // the layer initialization requires a register and unregister agent handle
            if (registerAgentHandle == null) throw new ArgumentNullException(nameof(registerAgentHandle));
            if (unregisterAgentHandle == null) throw new ArgumentNullException(nameof(unregisterAgentHandle));
            
            // the base class requires initialization, too
            base.InitLayer(layerInitData, registerAgentHandle, unregisterAgentHandle);
            
            // the agent manager can create agents and initializes them as defined in the sim config
            var agentManager = layerInitData.Container.Resolve<IAgentManager>(); // resolve the agent manager
            _frogs = agentManager.Spawn<FrogAgent, MyGridLayer>().ToList(); // the agents are instantiated on MyGridLayer
            Cars = agentManager.Spawn<CarAgent, MyGridLayer>().ToList();
            Trucks = agentManager.Spawn<TruckAgent, MyGridLayer>().ToList();
            Turtles = agentManager.Spawn<TurtleAgent, MyGridLayer>().ToList();
            Logs = agentManager.Spawn<LogAgent, MyGridLayer>().ToList();
            Pads = agentManager.Spawn<PadAgent, MyGridLayer>().ToList();
            int id = 0;
            foreach (var frog in _frogs)
            {
                frog.AgentId = id++;
            }

            foreach (var car in Cars)
            {
                car.AgentId = id++;
            }

            foreach (var truck in Trucks)
            {
                truck.AgentId = id++;           
            }

            foreach (var turtle in Turtles)
            {
                turtle.AgentId = id++;
            }

            foreach (var log in Logs)
            {
                log.AgentId = id++;
            }

            foreach (var pad in Pads)
            {
                pad.AgentId = id++;           
            }
            Console.WriteLine($"We created {_frogs.Count} frog agents.");
            Console.WriteLine($"We created {Cars.Count} car agents.");
            Console.WriteLine($"We created {Trucks.Count} truck agents.");
            Console.WriteLine($"We created {Turtles.Count} turtle agents.");
            Console.WriteLine($"We created {Logs.Count} log agents.");
            Console.WriteLine($"We created {Pads.Count} pad agents.");
            ActiveFrog = _frogs[_activeFrogIndex];
            
            // --- Sammel-Listen für Spots aus dem Grid ---
            var frogPlaced = false;
            var carSpots0   = new List<Position>();
            var carSpots180 = new List<Position>();
            var truckSpots0   = new List<Position>();
            var truckSpots180 = new List<Position>();
            var turtleSpots   = new List<Position>();
            var logSpots      = new List<Position>();
            var padSpots      = new List<Position>();
            
            // Startpositionen aus Grid auslesen
            
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var value = this[x, y];

                    switch (value)
                    {
                        case 1.0: // Frog
                            if (!frogPlaced)
                            {
                                ActiveFrog.Position = Position.CreatePosition(x, y);
                                _frogStart = Position.CreatePosition(x, y);
                                frogPlaced = true;
                                Console.WriteLine($"Frog start position set to: ({x}, {y})");
                            }
                            break;

                        case 2.0: // Truck heading 0
                            truckSpots0.Add(Position.CreatePosition(x, y));
                            break;

                        case 3.0: // Truck heading 180
                            truckSpots180.Add(Position.CreatePosition(x, y));
                            break;

                        case 4.0: // Car heading 0
                            carSpots0.Add(Position.CreatePosition(x, y));
                            break;

                        case 5.0: // Car heading 180
                            carSpots180.Add(Position.CreatePosition(x, y));
                            break;
                        case 6.0: // Log
                            logSpots.Add(Position.CreatePosition(x, y));
                            break;
                        case 7.0: // Turtle
                            turtleSpots.Add(Position.CreatePosition(x, y));
                            break;
                        case 8.0: // Pad
                            padSpots.Add(Position.CreatePosition(x, y));
                            break;
                    }
                }
            }
            // --- Cars zuweisen (erst 0°, dann 180°) ---
            int ci = 0;
            foreach (var p in carSpots0)
            {
                if (ci >= Cars.Count) { Console.WriteLine("⚠️ Mehr Car-Spots als Cars."); break; }
                Cars[ci].Position = p;
                Cars[ci].Heading  = 90;
                ci++;
            }
            foreach (var p in carSpots180)
            {
                if (ci >= Cars.Count) { Console.WriteLine("⚠️ Mehr Car-Spots als Cars."); break; }
                Cars[ci].Position = p;
                Cars[ci].Heading  = -90;
                ci++;
            }

            // --- Trucks zuweisen (erst 0°, dann 180°) ---
            int ti = 0;
            foreach (var p in truckSpots0)
            {
                if (ti >= Trucks.Count) { Console.WriteLine("⚠️ Mehr Truck-Spots als Trucks."); break; }
                Trucks[ti].Position = p;
                Trucks[ti].Heading  = 0;
                ti++;
            }
            foreach (var p in truckSpots180)
            {
                if (ti >= Trucks.Count) { Console.WriteLine("⚠️ Mehr Truck-Spots als Trucks."); break; }
                Trucks[ti].Position = p;
                Trucks[ti].Heading  = 180;
                ti++;
            }
            
            // --- Logs zuweisen
            int li = 0;
            foreach (var p in logSpots)
            {
                if (li >= Logs.Count) {Console.WriteLine("⚠️ Mehr Logs-Spots als Trucks.");}
                Logs[li].Position = p;
                li++;
            }
            // --- Tutrles zuweisen
            int tri = 0;
            foreach (var p in turtleSpots)
            {
                if (tri >= Turtles.Count) {Console.WriteLine("⚠️ Mehr Turtles-Spots als Trucks.");}
                Turtles[tri].Position = p;
                tri++;
            }
            // --- Pads zuweisen
            int pi = 0;
            foreach (var p in padSpots)
            {
                if (pi >= Pads.Count) {Console.WriteLine("⚠️ Mehr Pads-Spots als Trucks.");}
                Pads[pi].Position = p;
                pi++;
            }

            Console.WriteLine($"Placed {ci}/{Cars.Count} cars, {ti}/{Trucks.Count} trucks, {li}/{Logs.Count} logs, {tri}/{Turtles.Count} turtles and {pi}/{Pads.Count} pads,from grid.");
            
            // run server in background
            _dataVisualizationServer.Frog = ActiveFrog;
            _dataVisualizationServer.Cars = Cars;
            _dataVisualizationServer.Trucks = Trucks;  
            _dataVisualizationServer.Logs = Logs;
            _dataVisualizationServer.Turtles = Turtles;
            _dataVisualizationServer.Pads = Pads;
            _dataVisualizationServer.RestartRequested += RestartSimulation;
            _dataVisualizationServer.RunInBackground();
            
            return true; // the layer initialization was successful
        }

        public void Tick()
        {
            if (_gameOver || ActiveFrog == null) return;
            
            if (CollidesWithVehicle(ActiveFrog.Position))
            {
                _lives--;
                var oldId = ActiveFrog.AgentId;

                // nächsten aktiven Frog wählen (falls vorhanden)
                _activeFrogIndex++;
                if (_activeFrogIndex < _frogs.Count)
                {
                    ActiveFrog = _frogs[_activeFrogIndex];
                    ActiveFrog.Position = _frogStart;

                    // dem Viz-Server sagen: alten löschen, neuen verwenden & Lives updaten
                    _dataVisualizationServer.EnqueueRemoval(oldId);
                    _dataVisualizationServer.Frog = ActiveFrog;
                    _dataVisualizationServer.SetLives(_lives);
                }
                else
                {
                    // Game over 
                    Console.WriteLine("[Game] No frogs left.  — game over!");
                    _gameOver = true;
                    _dataVisualizationServer.EnqueueRemoval(oldId);
                    _dataVisualizationServer.SetLives(_lives);
                    _dataVisualizationServer.SetGameOver(true);
                    _dataVisualizationServer.Frog = null;   
                    ActiveFrog = null; 
                }
            }
        }

        public void PreTick()
        {
            // Warte bis Godot "Start" geschickt hat
            if (!_dataVisualizationServer.Started || _gameOver)
            {
                Console.WriteLine("[Viz] Waiting for START from client…");
                _dataVisualizationServer.WaitForStart();
                Console.WriteLine("[Viz] START received, beginning simulation.");
                _gameOver = false; //Reset
            }
            // 2) Pro Tick ggf. auf Resume warten
            if (_dataVisualizationServer.Paused)
                Console.WriteLine("[Viz] Simulation paused — waiting for RESUME…");

            _dataVisualizationServer.WaitWhilePaused();
           
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
        private void RestartSimulation()
        {
            Console.WriteLine("[Game] Restarting simulation...");
            _gameOver = false;
            _lives = 5;
            _activeFrogIndex = 0;

            // Agents komplett neu spawnen:
            InitLayer(_initDataCache, RegisterAgent, UnregisterAgent);

            // Wieder auf Start warten
            _dataVisualizationServer.ResetStartGate();
        }

    }
}
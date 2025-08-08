using System;
using System.Collections.Generic;
using System.Drawing;
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

        public FrogAgent Frog;
        public List<CarAgent> Cars = new();
        public List<TruckAgent> Trucks = new();
        DataVisualizationServer _dataVisualizationServer = new DataVisualizationServer();

        public override bool InitLayer(LayerInitData layerInitData, RegisterAgent registerAgentHandle,
            UnregisterAgent unregisterAgentHandle)
        {
            // the layer initialization requires a register and unregister agent handle
            if (registerAgentHandle == null) throw new ArgumentNullException(nameof(registerAgentHandle));
            if (unregisterAgentHandle == null) throw new ArgumentNullException(nameof(unregisterAgentHandle));
            
            // the base class requires initialization, too
            base.InitLayer(layerInitData, registerAgentHandle, unregisterAgentHandle);
            
            // the agent manager can create agents and initializes them as defined in the sim config
            var agentManager = layerInitData.Container.Resolve<IAgentManager>(); // resolve the agent manager
            var frogAgents = agentManager.Spawn<FrogAgent, MyGridLayer>().ToList(); // the agents are instantiated on MyGridLayer
            Cars = agentManager.Spawn<CarAgent, MyGridLayer>().ToList();
            Trucks = agentManager.Spawn<TruckAgent, MyGridLayer>().ToList();
            int ID = 0;
            foreach (var frog in frogAgents)
            {
                frog.AgentId = ID++;
            }

            foreach (var car in Cars)
            {
                car.AgentId = ID++;
            }

            foreach (var truck in Trucks)
            {
                truck.AgentId = ID++;           
            }
            Console.WriteLine($"We created {frogAgents.Count} frog agents.");
            Console.WriteLine($"We created {Cars.Count} car agents.");
            Console.WriteLine($"We created {Trucks.Count} truck agents.");
            Frog = frogAgents.First();
            
            // --- Sammel-Listen für Spots aus dem Grid ---
            var frogPlaced = false;
            var carSpots0   = new List<Position>();
            var carSpots180 = new List<Position>();
            var truckSpots0   = new List<Position>();
            var truckSpots180 = new List<Position>();
            
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
                                Frog.Position = Position.CreatePosition(x, y);
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

            Console.WriteLine($"Placed {ci}/{Cars.Count} cars and {ti}/{Trucks.Count} trucks from grid.");
            
            // run server in background
            _dataVisualizationServer.Frog = Frog;
            _dataVisualizationServer.Cars = Cars;
            _dataVisualizationServer.Trucks = Trucks;  
            _dataVisualizationServer.RunInBackground();
            
            return true; // the layer initialization was successful
        }

        public void Tick()
        {
            
        }

        public void PreTick()
        {
            // Warte bis Godot "Start" geschickt hat
            if (!_dataVisualizationServer.Started)
            {
                Console.WriteLine("[Viz] Waiting for START from client…");
                _dataVisualizationServer.WaitForStart();
                Console.WriteLine("[Viz] START received, beginning simulation.");
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
    }
}
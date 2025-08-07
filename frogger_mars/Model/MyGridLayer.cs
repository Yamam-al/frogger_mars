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

        private frogAgent frog;
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
            var agents = agentManager.Spawn<frogAgent, MyGridLayer>().ToList(); // the agents are instantiated on MyGridLayer
            Console.WriteLine($"We created {agents.Count} agents.");
            frog = agents.First();
            // Startposition aus Grid auslesen
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    // Lies Wert aus dem Grid
                    var value = this[x, y]; // this[,] funktioniert dank Vererbung von RasterLayer

                    if (value == 1.0) // 1.0 steht für Startfeld
                    {
                        var startPos = Position.CreatePosition(x, y);
                        frog.Position = startPos;
                        Console.WriteLine($"Frog start position set to: ({x}, {y})");
                        break;
                    }
                }
            }
            
            // run server in background
            _dataVisualizationServer.Frog = frog;
            _dataVisualizationServer.RunInBackground();
            
            return true; // the layer initialization was successful
        }

        public void Tick()
        {
            
        }

        public void PreTick()
        {
           
        }

        public void PostTick()
        {
            Console.WriteLine($"[Viz] Waiting for client at tick {Context.CurrentTick}");
            while (!_dataVisualizationServer.Connected())
                Thread.Sleep(1000);

            Console.WriteLine($"[Viz] Sending data for tick {Context.CurrentTick}");
            _dataVisualizationServer.SendData(); //TODO send data to client

            while (_dataVisualizationServer.CurrentTick != Context.CurrentTick + 1)
                Thread.Sleep(VisualizationTimeout);
        }
    }
}
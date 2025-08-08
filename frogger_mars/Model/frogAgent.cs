using System;
using System.Collections.Concurrent;
using Mars.Components.Services.Planning;
using Mars.Interfaces.Agents;
using Mars.Interfaces.Environments;

namespace frogger_mars.Model
{
    /// <summary>
    ///  A simple agent stub that has an Init() method for initialization and a
    ///  Tick() method for acting in every tick of the simulation.
    /// </summary>
    public class frogAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
    {
       
        // Enqueued by the WebSocket thread, consumed in Tick()
        public readonly ConcurrentQueue<FrogInput> InputQueue = new();
        
        //  The Init() method is called by the agent manager after the agent is created.
        public void Init(MyGridLayer layer)
        {
            Layer = layer; // store layer for access within agent class
        }
        
        // The Tick() method is called by the agent manager in every tick of the simulation.
        public void Tick()
        {
            Console.WriteLine("Tick: " + Layer.GetCurrentTick()); // print the current tick
            Console.WriteLine("Frog " + this.ID + " says: Hello, World");
            while (InputQueue.TryDequeue(out var input))
            {
                switch (input)
                {
                    case FrogInput.Up:
                        Position = new Position(Position.X, Position.Y - 1);
                        break;
                    case FrogInput.Down:
                        Position = new Position(Position.X, Position.Y + 1);
                        break;
                    case FrogInput.Left:
                        Position = new Position(Position.X - 1, Position.Y);
                        break;
                    case FrogInput.Right:
                        Position = new Position(Position.X + 1, Position.Y);
                        break;
                }
            }
        }

        // The layer property is used to access the main layer of this agent.
        private MyGridLayer Layer { get; set; } // provides access to the main layer of this agent
        public Guid ID { get; set; } // the unique identifier of this agent
        
    }
    
    public enum FrogInput { Up, Down, Left, Right }
}
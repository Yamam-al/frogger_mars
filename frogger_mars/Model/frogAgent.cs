using System;
using Mars.Components.Services.Planning;
using Mars.Interfaces.Agents;

namespace frogger_mars.Model
{
    /// <summary>
    ///  A simple agent stub that has an Init() method for initialization and a
    ///  Tick() method for acting in every tick of the simulation.
    /// </summary>
    public class frogAgent : AbstractFroggerAgent
    {
        GoapAgentStates AgentStates = new GoapAgentStates();
        
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
        }

        // The layer property is used to access the main layer of this agent.
        private MyGridLayer Layer { get; set; } // provides access to the main layer of this agent
        public Guid ID { get; set; } // the unique identifier of this agent
    }
}
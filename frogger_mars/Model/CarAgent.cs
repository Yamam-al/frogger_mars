using System;
using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

/// <summary>
/// Car agent that moves horizontally and wraps around screen edges.
/// </summary>
public class CarAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    /// <summary>
    /// Initializes basic car data such as breed, heading and a default start position.
    /// </summary>
    /// <param name="layer">The hosting <see cref="MyGridLayer"/>.</param>
    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "car";
        Heading = 90;
        Position = Mars.Interfaces.Environments.Position.CreatePosition(0,0);
    }
    
    /// <summary>
    /// Per-tick behavior: advance one tile in heading direction and wrap.
    /// </summary>
    // The Tick() method is called by the agent manager in every tick of the simulation.
    public void Tick()
    {
        MoveForward();

    }

    /// <summary>
    /// Moves one step forward based on <see cref="Heading"/> and wraps at grid edges.
    /// </summary>
    private void MoveForward()
    {
        // Heading in the original object is up
        if (Heading == 90)
        {
            Position.X++;
            if (Position.X >= Layer.Width)
            {
                Position.X = 0;
            }
        }
        else
        {
            Position.X--;
            if (Position.X < 0)
            {
                Position.X = Layer.Width +1 ;
            }       
        }
    }
}
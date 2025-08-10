using System;
using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

/// <summary>
/// Truck agent that moves horizontally and wraps. Moves every other tick.
/// </summary>
public class TruckAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    /// <summary>
    /// Initializes truck kind and default heading/position.
    /// </summary>
    /// <param name="layer">The grid layer.</param>
    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "truck";
        Heading = 0;
        Position = Mars.Interfaces.Environments.Position.CreatePosition(0,0);
    }

    private int _everSecondTick = -1;

    /// <summary>
    /// Moves the truck forward every second tick.
    /// </summary>
    // The Tick() method is called by the agent manager in every tick of the simulation.
    public void Tick()
    {
        _everSecondTick= _everSecondTick * -1;
        if (_everSecondTick < 0)
        {
            MoveForward();
        }
    }

    /// <summary>
    /// Moves one step in the configured heading and wraps at edges.
    /// </summary>
    private void MoveForward()
    {
        if (Heading == 180)
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
            if (Position.X <= 0)
            {
                Position.X = Layer.Width;
            }       
        }
    }
    
    /// <summary>Facing direction used to decide flow.</summary>
    public int Heading { get; set; }
}
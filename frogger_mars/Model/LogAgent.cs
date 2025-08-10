using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

/// <summary>
/// Floating log agent that moves to the right at a fixed period and wraps around.
/// </summary>
public class LogAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    private const int MovePeriod = 2; // move every 2 ticks

    /// <summary>Set up the agent's breed and heading.</summary>
    /// <param name="layer">The grid layer.</param>
    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "log";
        Heading = 0;
    }

    /// <summary>
    /// Ticks at simulation rate; moves only on the defined period.
    /// </summary>
    // The Tick() method is called by the agent manager in every tick of the simulation.
    public void Tick()
    {
        if (Layer.Context.CurrentTick % MovePeriod != 0) return;

        MoveForward();
    }

    /// <summary>Moves one tile to the right and wraps horizontally.</summary>
    private void MoveForward()
    {
        Position.X++;
        if (Position.X >= Layer.Width)
        {
            Position.X = 0;
        }
    }
}
using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

/// <summary>
/// Turtle agent that moves to the left at a fixed period and can be hidden (diving).
/// Hidden turtles do not count as platforms for the frog.
/// </summary>
public class TurtleAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    /// <summary>True if the turtle is currently submerged (not a platform).</summary>
    public bool Hidden = false;

    private const int MovePeriod = 2;

    /// <summary>Initializes turtle kind and heading.</summary>
    /// <param name="layer">The hosting layer.</param>
    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "turtle";
        Heading = 0;
    }
    
    /// <summary>
    /// Moves left on its movement period; no movement on other ticks.
    /// </summary>
    // The Tick() method is called by the agent manager in every tick of the simulation.
    public void Tick()
    {
        if (Layer.Context.CurrentTick % MovePeriod != 0) return;
        MoveForward();
    }
    
    /// <summary>Moves one tile left and wraps horizontally.</summary>
    private void MoveForward()
    {
        Position.X--;
        if (Position.X < 0)
        {
            Position.X = Layer.Width -1;
        }   
    }
}
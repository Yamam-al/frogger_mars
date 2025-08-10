using System;
using Mars.Interfaces.Agents;
using Mars.Interfaces.Environments;

namespace frogger_mars.Model;

/// <summary>
/// Base class for all Frogger agents used in the simulation.
/// Provides common properties that the visualization and layer logic depend on.
/// </summary>
public class AbstractFroggerAgent : IAgent<MyGridLayer>
{
    /// <summary>
    /// Called once after the agent was created and registered.
    /// </summary>
    /// <param name="layer">The grid layer the agent lives on.</param>
    public void Init(MyGridLayer layer)
    {
        // Intentionally left blank in the base class.
        // Concrete agents assign Layer/Breed/Heading/Position in their own Init().
    }

    /// <summary>
    /// Called every simulation tick. 
    /// </summary>
    public void Tick()
    {
        // Intentionally empty in the base class.
        // Concrete agents implement their own behavior.
    }

    /// <summary>Unique identifier assigned by the MARS runtime.</summary>
    public Guid ID { get; set; }

    /// <summary>The hosting layer. Set by concrete agents in <see cref="Init(MyGridLayer)"/>.</summary>
    public MyGridLayer Layer { get; set; }

    /// <summary>Discrete tile position in the grid coordinate system.</summary>
    public Position Position { get; set; }

    /// <summary>
    /// Integer id used by the Godot frontend (stable across agents to simplify rendering).
    /// </summary>
    public int AgentId { get; set; }

    /// <summary>
    /// Logical kind of the agent (e.g., "frog", "car", "truck", "log", "turtle", "pad").
    /// </summary>
    public string Breed { get; set; }

    /// <summary>
    /// Facing/flow direction in degrees when relevant (e.g., 0, 90, 180, -90).
    /// </summary>
    public int Heading { get; set; }
}
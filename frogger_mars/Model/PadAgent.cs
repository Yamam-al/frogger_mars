using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

/// <summary>
/// Goal pad (home slot). Becomes occupied once the frog lands on it.
/// </summary>
public class PadAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    /// <summary>Whether the pad has already been filled by a frog.</summary>
    public bool Occupied { get; set; }

    /// <summary>Agent id of the frog that filled this pad (if any).</summary>
    public int? OccupiedByFrogId { get; set; }

    /// <summary>Initializes base properties.</summary>
    /// <param name="layer">The hosting layer.</param>
    public void Init(MyGridLayer layer)
    {
        Layer = layer;
        Breed = "pad";
        Heading = 0;
        Occupied = false;
        OccupiedByFrogId = null;
    }

    /// <summary>No per-tick behavior for pads.</summary>
    public void Tick() { }

    /// <summary>Marks this pad as occupied by the given frog.</summary>
    public void SetOccupied(int frogAgentId)
    {
        Occupied = true;
        OccupiedByFrogId = frogAgentId;
    }

    /// <summary>Clears the occupied state so the pad becomes free again (used on reset).</summary>
    public void Clear()
    {
        Occupied = false;
        OccupiedByFrogId = null;
    }
}
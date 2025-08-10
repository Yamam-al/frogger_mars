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
}
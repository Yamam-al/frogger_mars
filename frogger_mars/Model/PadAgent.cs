using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

public class PadAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    public bool Occupied { get; set; }
    public int? OccupiedByFrogId { get; set; }

    public void Init(MyGridLayer layer)
    {
        Layer = layer;
        Breed = "pad";
        Heading = 0;
        Occupied = false;
        OccupiedByFrogId = null;
    }

    public void Tick() { }
}
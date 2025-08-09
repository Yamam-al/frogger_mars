using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

public class TurtleAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    public bool Hidden = false;
    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "turtle";
        Heading = 180;
    }
        
    // The Tick() method is called by the agent manager in every tick of the simulation.
    public void Tick()
    {
    }
}
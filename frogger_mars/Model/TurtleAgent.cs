using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

public class TurtleAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    public bool Hidden = false;
    private const int MovePeriod = 3;

    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "turtle";
        Heading = 0;
    }
    
        
    // The Tick() method is called by the agent manager in every tick of the simulation.
    public void Tick()
    {
        if (Layer.Context.CurrentTick % MovePeriod != 0) return;
        MoveForward();
    }
    
    private void MoveForward()
    {
        Position.X--;
        if (Position.X < 0)
        {
            Position.X = Layer.Width -1;
        }   
    }
    
}
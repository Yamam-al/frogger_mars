using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

public class LogAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    private const int MovePeriod = 3; // nur alle 3 Ticks

    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "log";
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
        Position.X++;
        if (Position.X >= Layer.Width)
        {
            Position.X = 0;
        }
    }
}
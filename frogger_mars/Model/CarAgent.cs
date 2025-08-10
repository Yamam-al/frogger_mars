using System;
using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

public class CarAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "car";
        Heading = 90;
        Position = Mars.Interfaces.Environments.Position.CreatePosition(0,0);
    }
    

    // The Tick() method is called by the agent manager in every tick of the simulation.
    public void Tick()
    {
        MoveForward();

    }
    private void MoveForward()
    {
        // Heading in the original object is up
        if (Heading == 90)
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
            if (Position.X < 0)
            {
                Position.X = Layer.Width +1 ;
            }       
        }
    }
    
}
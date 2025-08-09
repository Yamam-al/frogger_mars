using System;
using Mars.Interfaces.Agents;

namespace frogger_mars.Model;

public class TruckAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
{
    //  The Init() method is called by the agent manager after the agent is created.
    public void Init(MyGridLayer layer)
    {
        Layer = layer; // store layer for access within agent class
        Breed = "truck";
        Heading = 0;
        Position = Mars.Interfaces.Environments.Position.CreatePosition(0,0);
    }


    private int _everSecondTick = -1;
    // The Tick() method is called by the agent manager in every tick of the simulation.
    public void Tick()
    {
        _everSecondTick= _everSecondTick * -1;
        if (_everSecondTick < 0)
        {
            MoveForward();
        }
    }

    private void MoveForward()
    {
        if (Heading == 180)
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
            if (Position.X <= 0)
            {
                Position.X = Layer.Width;
            }       
        }
    }
    
    public int Heading { get; set; }
}
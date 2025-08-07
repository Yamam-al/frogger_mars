using System;
using Mars.Interfaces.Agents;
using Mars.Interfaces.Environments;

namespace frogger_mars.Model;

public class AbstractFroggerAgent : IAgent<MyGridLayer>
{
    public void Init(MyGridLayer layer)
    {
        
    }

    public void Tick()
    {
        
    }

    public Guid ID { get; set; }
    public Position Position { get; set; }
    public int AgentId { get; set; }
}
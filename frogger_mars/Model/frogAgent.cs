using System;
using System.Collections.Concurrent;
using Mars.Interfaces.Agents;
using Mars.Interfaces.Environments;

namespace frogger_mars.Model
{
    /// <summary>
    ///  Aktiver Frog-Agent; verarbeitet Eingaben, zählt Sprünge.
    /// </summary>
    public class FrogAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
    {
        // Eingaben aus Godot (WebSocket-Thread enqueued, Tick dequeued)
        public readonly ConcurrentQueue<FrogInput> InputQueue = new();

        // Anzahl der getätigten Sprünge seit letztem Reset (Tod/Pad)
        public int Jumps { get; set; }

        public void Init(MyGridLayer layer)
        {
            Layer = layer;
            Breed = "frog";
            Jumps = 0;
        }

        public void Tick()
        {
            // Eingaben abarbeiten; jeder gültige Schritt zählt als Jump
            while (InputQueue.TryDequeue(out var input))
            {
                switch (input)
                {
                    case FrogInput.Up:
                        Position = new Position(Position.X, Position.Y - 1);
                        Jumps++;
                        break;
                    case FrogInput.Down:
                        Position = new Position(Position.X, Position.Y + 1);
                        Jumps++;
                        break;
                    case FrogInput.Left:
                        Position = new Position(Position.X - 1, Position.Y);
                        Jumps++;
                        break;
                    case FrogInput.Right:
                        Position = new Position(Position.X + 1, Position.Y);
                        Jumps++;
                        break;
                }
            }
        }

        public Guid ID { get; set; } // unique identifier
    }

    public enum FrogInput { Up, Down, Left, Right }
}
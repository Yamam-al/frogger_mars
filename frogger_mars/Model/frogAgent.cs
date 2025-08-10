using System;
using System.Collections.Concurrent;
using Mars.Interfaces.Agents;
using Mars.Interfaces.Environments;

namespace frogger_mars.Model
{
    /// <summary>
    /// The active player-controlled frog agent.
    /// Processes input commands from the WebSocket bridge and counts jumps.
    /// </summary>
    public class FrogAgent : AbstractFroggerAgent, IAgent<MyGridLayer>
    {
        /// <summary>
        /// Input queue filled by <see cref="DataVisualizationServer"/> on the socket thread,
        /// drained during <see cref="Tick"/> to advance the frog.
        /// </summary>
        public readonly ConcurrentQueue<FrogInput> InputQueue = new();

        /// <summary>Total number of jumps since the last reset (death or reaching a pad).</summary>
        public int Jumps { get; set; }

        /// <summary>Initializes frog properties.</summary>
        /// <param name="layer">The hosting <see cref="MyGridLayer"/>.</param>
        public void Init(MyGridLayer layer)
        {
            Layer = layer;
            Breed = "frog";
            Jumps = 0;
        }

        /// <summary>
        /// Per-tick behavior: apply all queued inputs.
        /// Every valid step increments <see cref="Jumps"/>.
        /// </summary>
        public void Tick()
        {
            // Process all available inputs; each successful move counts as a jump.
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

        /// <summary>Unique MARS id for this agent instance.</summary>
        public Guid ID { get; set; }
    }

    /// <summary>Normalized input commands issued by the client.</summary>
    public enum FrogInput { Up, Down, Left, Right }
}

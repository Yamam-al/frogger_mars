using System;
using System.Diagnostics;
using System.IO;
using frogger_mars.Model;
using Mars.Components.Starter;
using Mars.Interfaces.Model;

namespace frogger_mars
{
    /// <summary>
    /// Application entry point that wires up the MARS model (layers and agents),
    /// loads the simulation configuration, runs the simulation, and prints a short summary.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Main entry point for the Frogger simulation.
        /// 
        /// Steps:
        /// 1) Build the <see cref="ModelDescription"/> by registering the layer and all agent types.
        /// 2) Load the simulation configuration from <c>config.json</c>.
        /// 3) Start and run the simulation, measuring wall-clock runtime.
        /// 4) Print the executed iteration count and elapsed time.
        /// </summary>
        /// <param name="args">Optional command line arguments (not used).</param>
        public static void Main(string[] args)
        {
            // 1) Build the model description:
            //    - MyGridLayer encapsulates grid simulation and visualization bindings.
            var description = new ModelDescription();
            description.AddLayer<MyGridLayer>();
            description.AddAgent<FrogAgent, MyGridLayer>();
            description.AddAgent<CarAgent, MyGridLayer>();
            description.AddAgent<TruckAgent, MyGridLayer>();
            description.AddAgent<TurtleAgent, MyGridLayer>();
            description.AddAgent<PadAgent, MyGridLayer>();
            description.AddAgent<LogAgent, MyGridLayer>();

            // 2) Load the configuration from config.json
            var file   = File.ReadAllText("config.json");
            var config = SimulationConfig.Deserialize(file);

            // 3) Start the simulation and measure elapsed time
            var watch   = Stopwatch.StartNew();
            var starter = SimulationStarter.Start(description, config);
            var result  = starter.Run();
            watch.Stop();

            // 4) Print a short summary
            Console.WriteLine(
                $"Executed iterations {result.Iterations} in {watch.Elapsed}"
            );
        }
    }
}

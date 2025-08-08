using System;
using System.Diagnostics;
using System.IO;
using frogger_mars.Model;
using Mars.Components.Starter;
using Mars.Interfaces.Model;

namespace frogger_mars
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            // 1) Baue die Modellbeschreibung auf: 
            //    - MyGridLayer kümmert sich intern um Simulation + Visualization
            var description = new ModelDescription();
            description.AddLayer<MyGridLayer>();
            description.AddAgent<FrogAgent, MyGridLayer>();
            description.AddAgent<CarAgent, MyGridLayer>();
            description.AddAgent<TruckAgent, MyGridLayer>();

            // 2) Lese die Konfiguration aus config.json
            var file   = File.ReadAllText("config.json");
            var config = SimulationConfig.Deserialize(file);

            // 3) Starte die Simulation und messe die Laufzeit
            var watch   = Stopwatch.StartNew();
            var starter = SimulationStarter.Start(description, config);
            var result  = starter.Run();
            watch.Stop();

            // 4) Gib Ergebnis aus
            Console.WriteLine(
                $"Executed iterations {result.Iterations} in {watch.Elapsed}"
            );
        }
    }
}
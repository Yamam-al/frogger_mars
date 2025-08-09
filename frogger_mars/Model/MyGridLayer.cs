using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mars.Components.Layers;
using Mars.Core.Data;
using Mars.Interfaces.Annotations;
using Mars.Interfaces.Data;
using Mars.Interfaces.Environments;
using Mars.Interfaces.Layers;

namespace frogger_mars.Model
{
    public class MyGridLayer : RasterLayer, ISteppedActiveLayer
    {
        [PropertyDescription] public bool Visualization { get; set; }
        [PropertyDescription] public int VisualizationTimeout { get; set; }

        public List<CarAgent> Cars = new();
        public List<TruckAgent> Trucks = new();
        public List<TurtleAgent> Turtles = new();
        public List<LogAgent> Logs = new();
        public List<PadAgent> Pads = new();

        private readonly DataVisualizationServer _dataVisualizationServer = new();

        private List<FrogAgent> _frogs; // wir nutzen nur den ersten als aktiven
        public FrogAgent ActiveFrog;
        private Position _frogStart;
        private int _lives = 5;
        private bool _gameOver = false;

        // Vorherige Positionen aus Tick T-1:
        private Dictionary<int, Position> _lastLogPos = new();
        private Dictionary<int, Position> _lastTurtlePos = new();
        private Position _lastFrogPos;


        public override bool InitLayer(LayerInitData layerInitData, RegisterAgent registerAgentHandle,
            UnregisterAgent unregisterAgentHandle)
        {
            base.InitLayer(layerInitData, registerAgentHandle, unregisterAgentHandle);

            var agentManager = layerInitData.Container.Resolve<IAgentManager>();
            _frogs = agentManager.Spawn<FrogAgent, MyGridLayer>().ToList();
            Cars = agentManager.Spawn<CarAgent, MyGridLayer>().ToList();
            Trucks = agentManager.Spawn<TruckAgent, MyGridLayer>().ToList();
            Turtles = agentManager.Spawn<TurtleAgent, MyGridLayer>().ToList();
            Logs = agentManager.Spawn<LogAgent, MyGridLayer>().ToList();
            Pads = agentManager.Spawn<PadAgent, MyGridLayer>().ToList();

            // IDs
            var id = 0;
            foreach (var f in _frogs) f.AgentId = id++;
            foreach (var c in Cars) c.AgentId = id++;
            foreach (var t in Trucks) t.AgentId = id++;
            foreach (var tu in Turtles) tu.AgentId = id++;
            foreach (var lg in Logs) lg.AgentId = id++;
            foreach (var p in Pads) p.AgentId = id++;

            Console.WriteLine($"We created {_frogs.Count} frog agents.");
            Console.WriteLine($"We created {Cars.Count} car agents.");
            Console.WriteLine($"We created {Trucks.Count} truck agents.");
            Console.WriteLine($"We created {Turtles.Count} turtle agents.");
            Console.WriteLine($"We created {Logs.Count} log agents.");
            Console.WriteLine($"We created {Pads.Count} pad agents.");

            ActiveFrog = _frogs.First(); // nur der erste ist aktiv

            // Grid lesen
            var frogPlaced = false;
            var carSpots0 = new List<Position>();
            var carSpots180 = new List<Position>();
            var truckSpots0 = new List<Position>();
            var truckSpots180 = new List<Position>();
            var turtleSpots = new List<Position>();
            var logSpots = new List<Position>();
            var padSpots = new List<Position>();

            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var value = this[x, y];
                switch (value)
                {
                    case 1.0:
                        if (!frogPlaced)
                        {
                            ActiveFrog.Position = Position.CreatePosition(x, y);
                            _frogStart = Position.CreatePosition(x, y);
                            frogPlaced = true;
                            Console.WriteLine($"Frog start position set to: ({x}, {y})");
                        }

                        break;
                    case 2.0: truckSpots0.Add(Position.CreatePosition(x, y)); break;
                    case 3.0: truckSpots180.Add(Position.CreatePosition(x, y)); break;
                    case 4.0: carSpots0.Add(Position.CreatePosition(x, y)); break;
                    case 5.0: carSpots180.Add(Position.CreatePosition(x, y)); break;
                    case 6.0: logSpots.Add(Position.CreatePosition(x, y)); break;
                    case 7.0: turtleSpots.Add(Position.CreatePosition(x, y)); break;
                    case 8.0: padSpots.Add(Position.CreatePosition(x, y)); break;
                }
            }

            // assign cars
            var ci = 0;
            foreach (var p in carSpots0)
            {
                if (ci >= Cars.Count) break;
                Cars[ci].Position = p;
                Cars[ci].Heading = 90;
                ci++;
            }

            foreach (var p in carSpots180)
            {
                if (ci >= Cars.Count) break;
                Cars[ci].Position = p;
                Cars[ci].Heading = -90;
                ci++;
            }

            // assign trucks
            var ti = 0;
            foreach (var p in truckSpots0)
            {
                if (ti >= Trucks.Count) break;
                Trucks[ti].Position = p;
                Trucks[ti].Heading = 0;
                ti++;
            }

            foreach (var p in truckSpots180)
            {
                if (ti >= Trucks.Count) break;
                Trucks[ti].Position = p;
                Trucks[ti].Heading = 180;
                ti++;
            }

            // assign logs
            var li = 0;
            foreach (var p in logSpots)
            {
                if (li >= Logs.Count) break;
                Logs[li].Position = p;
                li++;
            }

            // assign turtles
            var tri = 0;
            foreach (var p in turtleSpots)
            {
                if (tri >= Turtles.Count) break;
                Turtles[tri].Position = p;
                tri++;
            }

            // assign pads
            var pi = 0;
            foreach (var p in padSpots)
            {
                if (pi >= Pads.Count) break;
                Pads[pi].Position = p;
                Pads[pi].Occupied = false;
                Pads[pi].OccupiedByFrogId = null;
                pi++;
            }

            Console.WriteLine(
                $"Placed {ci}/{Cars.Count} cars, {ti}/{Trucks.Count} trucks, {li}/{Logs.Count} logs, {tri}/{Turtles.Count} turtles and {pi}/{Pads.Count} pads, from grid.");


            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);

            _lastLogPos.Clear();
            foreach (var lg in Logs)
                _lastLogPos[lg.AgentId] = Position.CreatePosition(lg.Position.X, lg.Position.Y);

            _lastTurtlePos.Clear();
            foreach (var tu in Turtles)
                _lastTurtlePos[tu.AgentId] = Position.CreatePosition(tu.Position.X, tu.Position.Y);


            // Viz server
            _dataVisualizationServer.Frog = ActiveFrog;
            _dataVisualizationServer.Cars = Cars;
            _dataVisualizationServer.Trucks = Trucks;
            _dataVisualizationServer.Logs = Logs;
            _dataVisualizationServer.Turtles = Turtles;
            _dataVisualizationServer.Pads = Pads;
            _dataVisualizationServer.RunInBackground();

            return true;
        }

        public void PreTick()
        {
            if (!_dataVisualizationServer.Started || _gameOver)
            {
                Console.WriteLine("[Viz] Waiting for START from client…");
                _dataVisualizationServer.WaitForStart();

                ResetGameState();

                Console.WriteLine("[Viz] START received, beginning simulation.");
            }

            if (_dataVisualizationServer.Paused)
                Console.WriteLine("[Viz] Simulation paused — waiting for RESUME…");

            _dataVisualizationServer.WaitWhilePaused();
        }

        public void Tick()
        {
            if (_gameOver || ActiveFrog == null) return;

            // ===== 1) Frog ggf. mit Plattform mitnehmen (robust per Δ, ohne Wrap; Rand = Tod) =====
            bool carried = false;

            // --- Logs (rechts) ---
            foreach (var lg in Logs)
            {
                if (!_lastLogPos.TryGetValue(lg.AgentId, out var prev)) continue;

                // Steht der Frog auf der aktuellen oder der vorherigen Log-Zelle?
                bool onPrevNow = prev.Equals(ActiveFrog.Position);
                bool onCurrent = lg.Position.Equals(ActiveFrog.Position);

                if (onPrevNow || onCurrent)
                {
                    int curX = (int)lg.Position.X;
                    int prevX = (int)prev.X;

                    // ΔX der Plattform (mit Wrap-Korrektur auf ±1)
                    int dx = curX - prevX;
                    if (dx > 1) dx = -1; // z. B. 0 - (Width-1) => +1 Bewegung nach rechts
                    if (dx < -1) dx = 1;

                    if (dx != 0)
                    {
                        int nx = (int)ActiveFrog.Position.X + dx;

                        // Kein Wrap beim Frog: Rand = Tod
                        if (nx < 0 || nx >= Width)
                        {
                            KillActiveFrog();
                            goto UPDATE_LAST;
                        }

                        ActiveFrog.Position = Position.CreatePosition(nx, ActiveFrog.Position.Y);
                    }

                    carried = true;
                    break; // eine Log reicht
                }
            }

            // --- Turtles (links) ---
            if (!carried)
            {
                foreach (var tu in Turtles)
                {
                    if (!_lastTurtlePos.TryGetValue(tu.AgentId, out var prev)) continue;

                    bool onPrevNow = prev.Equals(ActiveFrog.Position);
                    bool onCurrent = tu.Position.Equals(ActiveFrog.Position);

                    if (onPrevNow || onCurrent)
                    {
                        // Abtauchen -> sofort tot (auch wenn wir „drauf sind“)
                        if (tu.Hidden)
                        {
                            KillActiveFrog();
                            goto UPDATE_LAST;
                        }

                        int curX = (int)tu.Position.X;
                        int prevX = (int)prev.X;

                        int dx = curX - prevX;
                        if (dx > 1) dx = -1; // Wrap-Korrektur auf ±1
                        if (dx < -1) dx = 1;

                        if (dx != 0)
                        {
                            int nx = (int)ActiveFrog.Position.X + dx;

                            // Kein Wrap beim Frog: Rand = Tod
                            if (nx < 0 || nx >= Width)
                            {
                                KillActiveFrog();
                                goto UPDATE_LAST;
                            }

                            ActiveFrog.Position = Position.CreatePosition(nx, ActiveFrog.Position.Y);
                        }

                        carried = true;
                        break; // eine Turtle reicht
                    }
                }
            }

            // ===== 2) Wasser-Check (erst NACH dem Mittragen!) =====
            // Wenn wir nicht getragen wurden und im Wasserbereich sind -> tot
            if (!carried && IsWaterTile(ActiveFrog.Position))
            {
                KillActiveFrog();
                goto UPDATE_LAST;
            }

            // ===== 3) Pad-Landung: Pad besetzen, Frog zurück zum Start (kein Leben verlieren) =====
            {
                var pad = Pads.FirstOrDefault(pd => pd.Position.Equals(ActiveFrog.Position));
                if (pad != null)
                {
                    if (!pad.Occupied)
                    {
                        pad.Occupied = true;
                        pad.OccupiedByFrogId = ActiveFrog.AgentId;

                        ResetActiveFrogToStart(); // setzt Position & Jumps = 0

                        // Win: alle Pads belegt?
                        if (Pads.All(pd => pd.Occupied))
                        {
                            _gameOver = true; // Win über gleichen Kanal
                            _dataVisualizationServer.SetGameOver(true);
                            _dataVisualizationServer.Frog = null;
                            _dataVisualizationServer.ResetStartGate();
                        }
                    }

                    goto UPDATE_LAST;
                }
            }

            // ===== 4) Fahrzeug-Kollision =====
            if (CollidesWithVehicle(ActiveFrog.Position))
            {
                KillActiveFrog();
                // danach UPDATE_LAST
            }

            UPDATE_LAST:
            // ===== 5) Tick-Ende: „Last“-Positionen für T+1 aktualisieren =====
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);

            _lastLogPos.Clear();
            foreach (var lg in Logs)
                _lastLogPos[lg.AgentId] = Position.CreatePosition(lg.Position.X, lg.Position.Y);

            _lastTurtlePos.Clear();
            foreach (var tu in Turtles)
                _lastTurtlePos[tu.AgentId] = Position.CreatePosition(tu.Position.X, tu.Position.Y);
        }


        public void PostTick()
        {
            Console.WriteLine($"[Viz] Waiting for client at tick {Context.CurrentTick}");
            while (!_dataVisualizationServer.Connected())
                Thread.Sleep(1000);

            Console.WriteLine($"[Viz] Sending data for tick {Context.CurrentTick}");
            _dataVisualizationServer.SendData();

            while (_dataVisualizationServer.CurrentTick != Context.CurrentTick + 1)
                Thread.Sleep(VisualizationTimeout);
        }

        // ---------- Helpers ----------

        private void CarryFrogWithLog()
        {
            // Log bewegt sich alle 2 Ticks nach rechts (Tick 2,4,6,...) -> Frog folgt dann mit
            if (Context.CurrentTick % 2 == 0)
            {
                var x = ActiveFrog.Position.X + 1;
                if (x >= Width) x = 0;
                ActiveFrog.Position = Position.CreatePosition(x, ActiveFrog.Position.Y);
            }
        }

        private void CarryFrogWithTurtle()
        {
            // Turtle bewegt sich jeden Tick nach links
            var x = ActiveFrog.Position.X - 1;
            if (x < 0) x = Width - 1;
            ActiveFrog.Position = Position.CreatePosition(x, ActiveFrog.Position.Y);
        }

        private void KillActiveFrog()
        {
            _lives--;

            // Reset & Jumps zurücksetzen
            ResetActiveFrogToStart();

            _dataVisualizationServer.SetLives(_lives);

            if (_lives <= 0)
            {
                Console.WriteLine("[Game] No lives left — game over!");
                _gameOver = true;

                _dataVisualizationServer.SetGameOver(true);
                _dataVisualizationServer.Frog = null;
                _dataVisualizationServer.ResetStartGate();
            }
        }

        private void ResetActiveFrogToStart()
        {
            ActiveFrog.Position = _frogStart;
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);
            ActiveFrog.Jumps = 0;
        }

        private bool IsWaterTile(Position pos)
        {
            // Wasserbereich (Y-Zeilen) je nach Level anpassen – hier Beispiel: 0..6
            if (pos.Y >= 0 && pos.Y <= 6)
            {
                bool onLog = Logs.Any(log => log.Position.Equals(pos));
                bool onTurtle = Turtles.Any(tut => tut.Position.Equals(pos));
                bool onPad = Pads.Any(pad => pad.Position.Equals(pos));
                return !onLog && !onTurtle && !onPad;
            }

            return false;
        }

        private bool CollidesWithVehicle(Position p)
        {
            return Cars.Any(c => c.Position.Equals(p)) ||
                   Trucks.Any(t => t.Position.Equals(p));
        }

        // Setzt Spiel zurück, nachdem ein neuer START kam
        private void ResetGameState()
        {
            Console.WriteLine("[Game] ResetGameState()");

            _lives = 5;
            _gameOver = false;

            // Pads freigeben
            foreach (var pd in Pads)
            {
                pd.Occupied = false;
                pd.OccupiedByFrogId = null;
            }

            // aktiven Frog laden (falls config >1 Frog hat, nimm den ersten)
            ActiveFrog = _frogs.First();
            ActiveFrog.Position = _frogStart;
            ActiveFrog.Jumps = 0;

            // … nachdem ActiveFrog.Position gesetzt ist:
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);

            _lastLogPos.Clear();
            foreach (var lg in Logs)
                _lastLogPos[lg.AgentId] = Position.CreatePosition(lg.Position.X, lg.Position.Y);

            _lastTurtlePos.Clear();
            foreach (var tu in Turtles)
                _lastTurtlePos[tu.AgentId] = Position.CreatePosition(tu.Position.X, tu.Position.Y);


            _dataVisualizationServer.ClearPendingRemovals();
            _dataVisualizationServer.SetGameOver(false);
            _dataVisualizationServer.SetLives(_lives);
            _dataVisualizationServer.Frog = ActiveFrog;
        }
    }
}
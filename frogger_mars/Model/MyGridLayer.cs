using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        [PropertyDescription] public int TicksPerSecondDivisor { get; set; } = 3; // “every 3 ticks is one second”

        // Liste der CSVs (Reihenfolge = Levelnummer, 1-basiert)
        [PropertyDescription] public string LevelFilesCsv { get; set; } = "Resources/level1Grid.csv";

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
        private int _startLives = 5;
        private bool _gameOver = false;

        // Vorherige Positionen aus Tick T-1:
        private readonly Dictionary<int, Position> _lastLogPos = new();
        private readonly Dictionary<int, Position> _lastTurtlePos = new();
        private Position _lastFrogPos;

        // Time
        private int _startTimeSeconds = 60;
        private int _timeLeft;

        // ---------- Level-Layout ----------

        private class LevelLayout
        {
            public Position FrogStart;
            public List<Position> Car0 = new();
            public List<Position> Car180 = new();
            public List<Position> Truck0 = new();
            public List<Position> Truck180 = new();
            public List<Position> Logs = new();
            public List<Position> Turtles = new();
            public List<Position> Pads = new();
        }

        private readonly Dictionary<int, LevelLayout> _levelLayouts = new(); // 1-basiert

        // --- Pfadauflösung für relative Dateien ---
        private static string ResolvePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return p;
            if (Path.IsPathRooted(p)) return p;

            var baseDir = AppContext.BaseDirectory;
            var cand1 = Path.Combine(baseDir, p);
            if (File.Exists(cand1)) return cand1;

            var cand2 = Path.Combine(Directory.GetCurrentDirectory(), p);
            return cand2; // Parse prüft Existenz erneut
        }

        private LevelLayout ParseGridCsv(string path)
        {
            var full = ResolvePath(path);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Grid CSV not found: {path} (resolved: {full})");

            var layout = new LevelLayout();
            var lines  = File.ReadAllLines(full);
            int rows   = lines.Length;

            for (int y = 0; y < rows; y++)
            {
                var row = lines[y].Trim();
                if (string.IsNullOrWhiteSpace(row)) continue;

                var parts = row.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

                // CSV ist top->down, Grid ist bottom->up
                int yy = (rows - 1) - y;

                for (int x = 0; x < parts.Length; x++)
                {
                    if (!double.TryParse(parts[x],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var v))
                        continue;

                    var pos = Position.CreatePosition(x, yy);

                    switch (v)
                    {
                        case 1.0: layout.FrogStart ??= pos; break;
                        case 2.0: layout.Truck0.Add(   pos); break;
                        case 3.0: layout.Truck180.Add( pos); break;
                        case 4.0: layout.Car0.Add(     pos); break;
                        case 5.0: layout.Car180.Add(   pos); break;
                        case 6.0: layout.Logs.Add(     pos); break;
                        case 7.0: layout.Turtles.Add(  pos); break;
                        case 8.0: layout.Pads.Add(     pos); break;
                    }
                }
            }

            if (layout.FrogStart == null)
                throw new InvalidOperationException($"No frog start (1.0) found in grid {path}");

            return layout;
        }

        private void LoadLevelLayouts()
        {
            _levelLayouts.Clear();

            var csvListRaw = LevelFilesCsv;
            if (string.IsNullOrWhiteSpace(csvListRaw))
                throw new InvalidOperationException("LevelFilesCsv is null/empty.");

            var files = csvListRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (files.Length == 0)
                throw new InvalidOperationException(
                    "No level layouts configured/loaded (LevelFilesCsv produced 0 files).");

            for (int i = 0; i < files.Length; i++)
            {
                var idx = i + 1; // 1-basiert
                var path = files[i];
                var layout = ParseGridCsv(path);
                _levelLayouts[idx] = layout;
                Console.WriteLine($"[Level] Loaded layout {idx} from {ResolvePath(path)}");
            }
        }

        private void ApplyLevel(int levelIndex)
        {
            int applied = levelIndex;
            if (!_levelLayouts.TryGetValue(levelIndex, out var L))
            {
                Console.WriteLine($"[Level] Requested {levelIndex} not found. Falling back to 1.");
                L = _levelLayouts[1];
                applied = 1;
            }

            // Frog
            ActiveFrog = _frogs.First();
            ActiveFrog.Position = L.FrogStart;
            _frogStart = Position.CreatePosition(L.FrogStart.X, L.FrogStart.Y);
            ActiveFrog.Jumps = 0;

            // --- Cars ---
            int ci = 0;
            foreach (var p in L.Car0)
            {
                if (ci >= Cars.Count) break;
                Cars[ci].Position = p;
                Cars[ci].Heading = 90;
                ci++;
            }
            foreach (var p in L.Car180)
            {
                if (ci >= Cars.Count) break;
                Cars[ci].Position = p;
                Cars[ci].Heading = -90;
                ci++;
            }
            // übrige Cars off-grid parken
            for (int i = ci; i < Cars.Count; i++)
                Cars[i].Position = Position.CreatePosition(-1000, -1000);

            // --- Trucks ---
            int ti = 0;
            foreach (var p in L.Truck0)
            {
                if (ti >= Trucks.Count) break;
                Trucks[ti].Position = p;
                Trucks[ti].Heading = 0;
                ti++;
            }
            foreach (var p in L.Truck180)
            {
                if (ti >= Trucks.Count) break;
                Trucks[ti].Position = p;
                Trucks[ti].Heading = 180;
                ti++;
            }
            // übrige Trucks off-grid parken
            for (int i = ti; i < Trucks.Count; i++)
                Trucks[i].Position = Position.CreatePosition(-1000, -1000);

            // --- Logs ---
            int li = 0;
            foreach (var p in L.Logs)
            {
                if (li >= Logs.Count) break;
                Logs[li].Position = p;
                li++;
            }
            for (int i = li; i < Logs.Count; i++)
                Logs[i].Position = Position.CreatePosition(-1000, -1000);

            // --- Turtles ---
            int tri = 0;
            foreach (var p in L.Turtles)
            {
                if (tri >= Turtles.Count) break;
                Turtles[tri].Position = p;
                tri++;
            }
            for (int i = tri; i < Turtles.Count; i++)
                Turtles[i].Position = Position.CreatePosition(-1000, -1000);

            // --- Pads ---
            int pi = 0;
            foreach (var p in L.Pads)
            {
                if (pi >= Pads.Count) break;
                var pd = Pads[pi];
                pd.Position = p;
                pd.Occupied = false;
                pd.OccupiedByFrogId = null;
                pi++;
            }
            for (int i = pi; i < Pads.Count; i++)
            {
                Pads[i].Position = Position.CreatePosition(-1000, -1000);
                Pads[i].Occupied = false;
                Pads[i].OccupiedByFrogId = null;
            }

            // Last-Positions neu initialisieren
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);
            _lastLogPos.Clear();
            foreach (var lg in Logs) _lastLogPos[lg.AgentId] = Position.CreatePosition(lg.Position.X, lg.Position.Y);
            _lastTurtlePos.Clear();
            foreach (var tu in Turtles)
                _lastTurtlePos[tu.AgentId] = Position.CreatePosition(tu.Position.X, tu.Position.Y);

            Console.WriteLine($"[Level] Applied layout {applied}");
        }

        // ---------- Init / Loop ----------

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

            // ----- Fallback/Normalisierung LevelFilesCsv -----
            if (string.IsNullOrWhiteSpace(LevelFilesCsv))
            {
                LevelFilesCsv = "Resources/level1Grid.csv";
                Console.WriteLine($"[Level] LevelFilesCsv fallback => {LevelFilesCsv}");
            }

            // --- Level-Layouts laden & aktuelles Startlevel anwenden ---
            LoadLevelLayouts();
            var lvl = Math.Max(1, _dataVisualizationServer.StartLevel);
            ApplyLevel(lvl);

            if (TicksPerSecondDivisor <= 0) TicksPerSecondDivisor = 3;

            // Viz server
            _dataVisualizationServer.Frog = ActiveFrog;
            _dataVisualizationServer.Cars = Cars;
            _dataVisualizationServer.Trucks = Trucks;
            _dataVisualizationServer.Logs = Logs;
            _dataVisualizationServer.Turtles = Turtles;
            _dataVisualizationServer.Pads = Pads;

            _startTimeSeconds = _dataVisualizationServer.StartTimeSeconds; // falls Godot vor Start gesetzt hat
            _timeLeft = _startTimeSeconds;

            _startLives = _dataVisualizationServer.StartLives;
            _lives = _startLives;
            _dataVisualizationServer.SetLives(_lives);

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

            // === TIMER ===
            if (Context.CurrentTick % TicksPerSecondDivisor == 0)
            {
                if (_timeLeft > 0) _timeLeft--;
                if (_timeLeft == 0)
                {
                    Console.WriteLine("[Timer] Time out → lose 1 life");
                    KillActiveFrog();
                    return;
                }
            }

            // ===== 1) Plattform-Mittragen =====
            bool carried = false;

            // Logs (rechts)
            foreach (var lg in Logs)
            {
                if (!_lastLogPos.TryGetValue(lg.AgentId, out var prev)) continue;

                int frogX = (int)ActiveFrog.Position.X;
                int frogY = (int)ActiveFrog.Position.Y;

                int curX = (int)lg.Position.X;
                int curY = (int)lg.Position.Y;
                int prevX = (int)prev.X;
                int prevY = (int)prev.Y;

                bool onPrev = (frogX == prevX && frogY == prevY);
                bool onCur = (frogX == curX && frogY == curY);

                if (onPrev || onCur)
                {
                    int dx = curX - prevX;
                    if (dx > 1) dx = -1; // Wrap-Korrektur auf ±1
                    if (dx < -1) dx = 1;

                    if (dx != 0)
                    {
                        int nx = frogX + dx;

                        // Kein Wrap beim Frog: Rand = Tod
                        if (nx < 0 || nx >= Width)
                        {
                            KillActiveFrog();
                            goto UPDATE_LAST;
                        }

                        ActiveFrog.Position = Position.CreatePosition(nx, frogY);
                    }

                    carried = true;
                    break;
                }
            }

            // Turtles (links)
            if (!carried)
            {
                foreach (var tu in Turtles)
                {
                    if (!_lastTurtlePos.TryGetValue(tu.AgentId, out var prev)) continue;

                    int frogX = (int)ActiveFrog.Position.X;
                    int frogY = (int)ActiveFrog.Position.Y;

                    int curX = (int)tu.Position.X;
                    int curY = (int)tu.Position.Y;
                    int prevX = (int)prev.X;
                    int prevY = (int)prev.Y;

                    bool onPrev = (frogX == prevX && frogY == prevY);
                    bool onCur = (frogX == curX && frogY == curY);

                    if (onPrev || onCur)
                    {
                        if (tu.Hidden)
                        {
                            KillActiveFrog();
                            goto UPDATE_LAST;
                        }

                        int dx = curX - prevX;
                        if (dx > 1) dx = -1; // Wrap-Korrektur auf ±1
                        if (dx < -1) dx = 1;

                        if (dx != 0)
                        {
                            int nx = frogX + dx;

                            if (nx < 0 || nx >= Width)
                            {
                                KillActiveFrog();
                                goto UPDATE_LAST;
                            }

                            ActiveFrog.Position = Position.CreatePosition(nx, frogY);
                        }

                        carried = true;
                        break;
                    }
                }
            }

            // ===== 2) Wasser-Check (erst nach Mittragen) =====
            if (!carried && IsWaterTile(ActiveFrog.Position))
            {
                KillActiveFrog();
                goto UPDATE_LAST;
            }

            // ===== 3) Pad-Landung =====
            {
                var pad = Pads.FirstOrDefault(pd => pd.Position.Equals(ActiveFrog.Position));
                if (pad != null)
                {
                    if (!pad.Occupied)
                    {
                        pad.Occupied = true;
                        pad.OccupiedByFrogId = ActiveFrog.AgentId;

                        ResetActiveFrogToStart(); // Position & Jumps = 0

                        if (Pads.All(pd => pd.Occupied))
                        {
                            _gameOver = true; // treat as win
                            _dataVisualizationServer.SetGameOver(true);
                            _dataVisualizationServer.SetGameWon(true);
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
            }

        UPDATE_LAST:
            // ===== 5) Tick-Ende: „Last“-Positionen für T+1 =====
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
            _dataVisualizationServer.SetTimeLeft(_timeLeft);
            _dataVisualizationServer.SendData();

            while (_dataVisualizationServer.CurrentTick != Context.CurrentTick + 1)
                Thread.Sleep(VisualizationTimeout);
        }

        // ---------- Helpers ----------

        private void KillActiveFrog()
        {
            _lives--;

            ResetActiveFrogToStart();
            _dataVisualizationServer.SetLives(_lives);

            if (_lives <= 0)
            {
                Console.WriteLine("[Game] No lives left — game over!");
                _gameOver = true;

                _dataVisualizationServer.SetGameWon(false);
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
            ResetTimer();
        }

        private bool IsWaterTile(Position pos)
        {
            // Wasserbereich (Y-Zeilen) ggf. je Level anpassen – hier Beispiel: 0..6
            if (pos.Y >= 0 && pos.Y <= 6)
            {
                bool onLog    = Logs.Any(log => log.Position.Equals(pos));
                bool onTurtle = Turtles.Any(tut => tut.Position.Equals(pos));
                bool onPad    = Pads.Any(pad => pad.Position.Equals(pos));
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
            Console.WriteLine("[Game] ResetGameState()]");

            // Level aus Godot wählen und anwenden
            var lvl = Math.Max(1, _dataVisualizationServer.StartLevel);
            ApplyLevel(lvl);

            _startTimeSeconds = _dataVisualizationServer.StartTimeSeconds; // ggf. geändert
            ResetTimer();

            _startLives = _dataVisualizationServer.StartLives;
            _lives = _startLives;
            _gameOver = false;

            // Pads freigeben (ApplyLevel macht das schon pro Platzierung; schadet nicht)
            foreach (var pd in Pads)
            {
                pd.Occupied = false;
                pd.OccupiedByFrogId = null;
            }

            _dataVisualizationServer.ClearPendingRemovals();
            _dataVisualizationServer.SetGameOver(false);
            _dataVisualizationServer.SetGameWon(false);
            _dataVisualizationServer.SetLives(_lives);
            _dataVisualizationServer.Frog = ActiveFrog;
        }

        private void ResetTimer() => _timeLeft = _startTimeSeconds;
    }
}

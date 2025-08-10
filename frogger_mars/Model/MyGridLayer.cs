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
        [PropertyDescription] public string LevelFilesCsv { get; set; } = "Resources/level1Grid.csv";

        public List<CarAgent> Cars = new();
        public List<TruckAgent> Trucks = new();
        public List<TurtleAgent> Turtles = new();
        public List<LogAgent> Logs = new();
        public List<PadAgent> Pads = new();

        private readonly DataVisualizationServer _dataVisualizationServer = new();

        private List<FrogAgent> _frogs;
        public FrogAgent ActiveFrog;
        private Position _frogStart;
        private int _lives = 5;
        private int _startLives = 5;
        private bool _gameOver = false;

        // Snapshots
        private Position _lastFrogPos;
        private readonly Dictionary<int, Position> _lastLogPos = new();

        private readonly Dictionary<int, Position> _lastTurtlePos = new();

        // pro Row -> prevX -> exakter dx dieses Ticks (kann -2, -1, 0, +1, +2 sein)
        private readonly Dictionary<int, Dictionary<int, int>> _dxByRowPrevX = new();

        // Fallback, falls exakte prevX nicht gefunden: Median der dx in der Row
        private readonly Dictionary<int, int> _rowFallbackDx = new();

        // welche prevX (pro Row) gehören zu Turtles, die JETZT hidden sind (für Sofort-Tod)
        private readonly Dictionary<int, HashSet<int>> _turtleHiddenNow = new();


        // Conveyor-Lanes: pro Y-Reihe -> Set<X> der Plattformkacheln aus T-1 und Delta -1/0/+1 in T
        private readonly Dictionary<int, HashSet<int>> _platformsPrevByRow = new();
        private readonly Dictionary<int, int> _laneDeltaByRow = new();

        private const int CARRY_DX_CLAMP = 1;

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

        // --- Pfadauflösung ---
        private static string ResolvePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return p;
            if (Path.IsPathRooted(p)) return p;
            var baseDir = AppContext.BaseDirectory;
            var cand1 = Path.Combine(baseDir, p);
            if (File.Exists(cand1)) return cand1;
            var cand2 = Path.Combine(Directory.GetCurrentDirectory(), p);
            return cand2;
        }

        private LevelLayout ParseGridCsv(string path)
        {
            var full = ResolvePath(path);
            if (!File.Exists(full))
                throw new FileNotFoundException($"Grid CSV not found: {path} (resolved: {full})");

            var layout = new LevelLayout();
            var lines = File.ReadAllLines(full);
            int rows = lines.Length;

            for (int y = 0; y < rows; y++)
            {
                var row = lines[y].Trim();
                if (string.IsNullOrWhiteSpace(row)) continue;
                var parts = row.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

                int yy = (rows - 1) - y; // CSV top->down, Grid bottom->up

                for (int x = 0; x < parts.Length; x++)
                {
                    if (!double.TryParse(parts[x], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        continue;

                    var pos = Position.CreatePosition(x, yy);
                    switch (v)
                    {
                        case 1.0: layout.FrogStart ??= pos; break;
                        case 2.0: layout.Truck0.Add(pos); break;
                        case 3.0: layout.Truck180.Add(pos); break;
                        case 4.0: layout.Car0.Add(pos); break;
                        case 5.0: layout.Car180.Add(pos); break;
                        case 6.0: layout.Logs.Add(pos); break;
                        case 7.0: layout.Turtles.Add(pos); break;
                        case 8.0: layout.Pads.Add(pos); break;
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
            var files = LevelFilesCsv.Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (files.Length == 0) throw new InvalidOperationException("No level layouts configured/loaded.");
            for (int i = 0; i < files.Length; i++)
            {
                var idx = i + 1;
                var layout = ParseGridCsv(files[i]);
                _levelLayouts[idx] = layout;
                Console.WriteLine($"[Level] Loaded layout {idx} from {ResolvePath(files[i])}");
            }
        }

        private void ApplyLevel(int levelIndex)
        {
            if (!_levelLayouts.TryGetValue(levelIndex, out var L))
            {
                Console.WriteLine($"[Level] Requested {levelIndex} not found. Falling back to 1.");
                L = _levelLayouts[1];
            }

            ActiveFrog = _frogs.First();
            ActiveFrog.Position = L.FrogStart;
            _frogStart = Position.CreatePosition(L.FrogStart.X, L.FrogStart.Y);
            ActiveFrog.Jumps = 0;

            // Cars
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

            for (int i = ci; i < Cars.Count; i++) Cars[i].Position = Position.CreatePosition(-1000, -1000);

            // Trucks
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

            for (int i = ti; i < Trucks.Count; i++) Trucks[i].Position = Position.CreatePosition(-1000, -1000);

            // Logs
            int li = 0;
            foreach (var p in L.Logs)
            {
                if (li >= Logs.Count) break;
                Logs[li].Position = p;
                li++;
            }

            for (int i = li; i < Logs.Count; i++) Logs[i].Position = Position.CreatePosition(-1000, -1000);

            // Turtles
            int tri = 0;
            foreach (var p in L.Turtles)
            {
                if (tri >= Turtles.Count) break;
                Turtles[tri].Position = p;
                tri++;
            }

            for (int i = tri; i < Turtles.Count; i++) Turtles[i].Position = Position.CreatePosition(-1000, -1000);

            // Pads
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

            // Snapshots / Lane-Maps initialisieren
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);

            _lastLogPos.Clear();
            foreach (var lg in Logs) _lastLogPos[lg.AgentId] = Position.CreatePosition(lg.Position.X, lg.Position.Y);

            _lastTurtlePos.Clear();
            foreach (var tu in Turtles)
                _lastTurtlePos[tu.AgentId] = Position.CreatePosition(tu.Position.X, tu.Position.Y);

            RebuildPlatformsPrevMap(); // aus aktuellem Zustand
            _laneDeltaByRow.Clear(); // noch kein Delta beim Levelstart

            Console.WriteLine($"[Level] Applied layout {levelIndex}");
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

            if (string.IsNullOrWhiteSpace(LevelFilesCsv))
                LevelFilesCsv = "Resources/level1Grid.csv";

            LoadLevelLayouts();
            ApplyLevel(Math.Max(1, _dataVisualizationServer.StartLevel));

            // Timer/Leben
            _startTimeSeconds = _dataVisualizationServer.StartTimeSeconds;
            _timeLeft = _startTimeSeconds;
            _startLives = _dataVisualizationServer.StartLives;
            _lives = _startLives;
            _dataVisualizationServer.SetLives(_lives);

            // Viz server binding
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
                _dataVisualizationServer.WaitForStart();
                ResetGameState();
            }

            if (_dataVisualizationServer.Paused)
                _dataVisualizationServer.WaitWhilePaused();
        }

        public void Tick()
        {
            if (_gameOver || ActiveFrog == null) return;

            // === 1) exakte Lane-Bewegung nur wenn Frosch NICHT die Row gewechselt hat ===
            ComputeLaneMotionMaps();

            int yPrev = (int)_lastFrogPos.Y;
            int yNow = (int)ActiveFrog.Position.Y;

            bool sameRow = (yNow == yPrev);
            bool wasOnPrevPlatformSameRow =
                sameRow &&
                _platformsPrevByRow.TryGetValue(yPrev, out var prevXs) &&
                prevXs.Contains((int)_lastFrogPos.X);

            // Carry nur, wenn gleiche Row & er stand in T-1 auf Plattform
            if (wasOnPrevPlatformSameRow)
            {
                if (_dxByRowPrevX.TryGetValue(yPrev, out var map) &&
                    map.TryGetValue((int)_lastFrogPos.X, out var dx))
                {
                    // Turtle jetzt getaucht? -> sofort Tod
                    if (_turtleHiddenNow.TryGetValue(yPrev, out var hiddenSet) &&
                        hiddenSet.Contains((int)_lastFrogPos.X))
                    {
                        KillActiveFrog();
                        goto AFTER_MOVES;
                    }

                    if (dx != 0)
                    {
                        int nx = (int)ActiveFrog.Position.X + dx;
                        if (nx < 0 || nx >= Width)
                        {
                            KillActiveFrog();
                            goto AFTER_MOVES;
                        } // Rand=Tod

                        ActiveFrog.Position = Position.CreatePosition(nx, yNow);
                    }
                }
                else if (_rowFallbackDx.TryGetValue(yPrev, out var mdx) && mdx != 0)
                {
                    int nx = (int)ActiveFrog.Position.X + mdx;
                    if (nx < 0 || nx >= Width)
                    {
                        KillActiveFrog();
                        goto AFTER_MOVES;
                    }

                    ActiveFrog.Position = Position.CreatePosition(nx, yNow);
                }
            }

            // === 2) Wasser/Pad/Collision ===
            // Gnadenfrist: war er in T-1 auf Plattform derselben Row? Dann kein Wasser-Tod in DIESEM Tick
            if (IsWaterTile(ActiveFrog.Position))
            {
                if (!wasOnPrevPlatformSameRow)
                {
                    KillActiveFrog();
                    goto AFTER_MOVES;
                }
                // sonst: 1-Tick Grace — nix tun, er „schwimmt“ diesen Tick mit
            }

            var pad = Pads.FirstOrDefault(pd => pd.Position.Equals(ActiveFrog.Position));
            if (pad != null)
            {
                if (!pad.Occupied)
                {
                    pad.Occupied = true;
                    pad.OccupiedByFrogId = ActiveFrog.AgentId;
                    ResetActiveFrogToStart();
                    if (Pads.All(pd => pd.Occupied))
                    {
                        _gameOver = true;
                        _dataVisualizationServer.SetGameOver(true);
                        _dataVisualizationServer.SetGameWon(true);
                        _dataVisualizationServer.Frog = null;
                        _dataVisualizationServer.ResetStartGate();
                    }
                }

                goto AFTER_MOVES;
            }

            if (CollidesWithVehicle(ActiveFrog.Position))
            {
                KillActiveFrog();
            }

            AFTER_MOVES:
            // === 4) Timer ===
            if (Context.CurrentTick % TicksPerSecondDivisor == 0)
            {
                if (_timeLeft > 0) _timeLeft--;
                if (_timeLeft == 0) KillActiveFrog();
            }

            // === 5) Snapshots & Plattform-Map für nächsten Tick ===
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);

            _lastLogPos.Clear();
            foreach (var lg in Logs) _lastLogPos[lg.AgentId] = Position.CreatePosition(lg.Position.X, lg.Position.Y);

            _lastTurtlePos.Clear();
            foreach (var tu in Turtles)
                _lastTurtlePos[tu.AgentId] = Position.CreatePosition(tu.Position.X, tu.Position.Y);

            RebuildPlatformsPrevMap();
        }

        public void PostTick()
        {
            while (!_dataVisualizationServer.Connected())
                Thread.Sleep(1000);

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
            // Beispiel: Zeilen 0..6 sind Wasser
            if (pos.Y >= 0 && pos.Y <= 6)
            {
                bool onPad = Pads.Any(pad => pad.Position.Equals(pos));
                bool onLog = Logs.Any(log => log.Position.Equals(pos));
                bool onTurtle = Turtles.Any(tut => !tut.Hidden && tut.Position.Equals(pos));
                return !onPad && !onLog && !onTurtle;
            }

            return false;
        }

        private bool CollidesWithVehicle(Position p)
        {
            return Cars.Any(c => c.Position.Equals(p)) ||
                   Trucks.Any(t => t.Position.Equals(p));
        }

        private void ResetGameState()
        {
            ApplyLevel(Math.Max(1, _dataVisualizationServer.StartLevel));

            _startTimeSeconds = _dataVisualizationServer.StartTimeSeconds;
            ResetTimer();

            _startLives = _dataVisualizationServer.StartLives;
            _lives = _startLives;
            _gameOver = false;

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

        // === Conveyor-Berechnungen ===
        private void RebuildPlatformsPrevMap()
        {
            _platformsPrevByRow.Clear();

            foreach (var lg in Logs)
            {
                int y = (int)lg.Position.Y;
                int x = (int)lg.Position.X;
                if (!_platformsPrevByRow.TryGetValue(y, out var set))
                {
                    set = new HashSet<int>();
                    _platformsPrevByRow[y] = set;
                }

                set.Add(x);
            }

            foreach (var tu in Turtles)
            {
                if (tu.Hidden) continue; // getauchte sind keine Plattform
                int y = (int)tu.Position.Y;
                int x = (int)tu.Position.X;
                if (!_platformsPrevByRow.TryGetValue(y, out var set))
                {
                    set = new HashSet<int>();
                    _platformsPrevByRow[y] = set;
                }

                set.Add(x);
            }
        }

        private void ComputeLaneMotionMaps()
        {
            _dxByRowPrevX.Clear();
            _rowFallbackDx.Clear();
            _turtleHiddenNow.Clear();

            var samplesByRow = new Dictionary<int, List<int>>();

            // Logs
            foreach (var lg in Logs)
            {
                if (!_lastLogPos.TryGetValue(lg.AgentId, out var prev)) continue;

                int yPrev = (int)prev.Y;
                int rawDx = TorusDelta((int)prev.X, (int)lg.Position.X, Width); // echter dx inkl. Wrap
                int dx = Math.Clamp(rawDx, -CARRY_DX_CLAMP, CARRY_DX_CLAMP);

                if (dx != 0)
                {
                    if (!_dxByRowPrevX.TryGetValue(yPrev, out var map))
                    {
                        map = new Dictionary<int, int>();
                        _dxByRowPrevX[yPrev] = map;
                    }

                    map[(int)prev.X] = dx;

                    if (!samplesByRow.TryGetValue(yPrev, out var list))
                    {
                        list = new List<int>();
                        samplesByRow[yPrev] = list;
                    }

                    list.Add(dx);
                }
            }

            // Turtles
            foreach (var tu in Turtles)
            {
                if (!_lastTurtlePos.TryGetValue(tu.AgentId, out var prev)) continue;

                int yPrev = (int)prev.Y;
                int rawDx = TorusDelta((int)prev.X, (int)tu.Position.X, Width);
                int dx = Math.Clamp(rawDx, -CARRY_DX_CLAMP, CARRY_DX_CLAMP);

                if (dx != 0)
                {
                    if (!_dxByRowPrevX.TryGetValue(yPrev, out var map))
                    {
                        map = new Dictionary<int, int>();
                        _dxByRowPrevX[yPrev] = map;
                    }

                    map[(int)prev.X] = dx;

                    if (!samplesByRow.TryGetValue(yPrev, out var list))
                    {
                        list = new List<int>();
                        samplesByRow[yPrev] = list;
                    }

                    list.Add(dx);
                }

                if (tu.Hidden)
                {
                    if (!_turtleHiddenNow.TryGetValue(yPrev, out var set))
                    {
                        set = new HashSet<int>();
                        _turtleHiddenNow[yPrev] = set;
                    }

                    set.Add((int)prev.X);
                }
            }

            // Fallback: Median der (schon) geklemmten dx
            foreach (var kv in samplesByRow)
            {
                var list = kv.Value;
                list.Sort();
                int medianDx = list[list.Count / 2];
                _rowFallbackDx[kv.Key] = medianDx;
            }
        }


        private static int TorusDelta(int prevX, int curX, int width)
        {
            int dx = curX - prevX;
            if (dx > width / 2) dx -= width;
            if (dx < -width / 2) dx += width;
            return dx;
        }
    }
}
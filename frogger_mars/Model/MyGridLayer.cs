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
    /// <summary>
    /// Main simulation layer hosting the grid, agents and game logic.
    /// Handles level loading, timing, lives, collision checks and the conveyor-attach rules.
    /// </summary>
    public class MyGridLayer : RasterLayer, ISteppedActiveLayer
    {
        /// <summary>Enable/disable visualization mode (forwarded by config).</summary>
        [PropertyDescription] public bool Visualization { get; set; }

        /// <summary>Wait time between sending and client ACK checks.</summary>
        [PropertyDescription] public int VisualizationTimeout { get; set; }

        /// <summary>Number of ticks that make up one in-game second.</summary>
        [PropertyDescription] public int TicksPerSecondDivisor { get; set; } = 3; // “every 3 ticks is one second”

        /// <summary>
        /// Semicolon-separated list of CSV grid files representing levels (1-based index).
        /// </summary>
        [PropertyDescription] public string LevelFilesCsv { get; set; } = "Resources/level1Grid.csv";

        // Agent collections (spawned at init)
        public List<CarAgent> Cars = new();
        public List<TruckAgent> Trucks = new();
        public List<TurtleAgent> Turtles = new();
        public List<LogAgent> Logs = new();
        public List<PadAgent> Pads = new();

        private readonly DataVisualizationServer _dataVisualizationServer = new();

        private List<FrogAgent> _frogs;
        /// <summary>The frog actively controlled by the client.</summary>
        public FrogAgent ActiveFrog;

        private Position _frogStart;
        private int _lives = 5;
        private int _startLives = 5;
        private bool _gameOver = false;

        // Snapshots for previous tick (used by conveyor & wrap detection)
        private Position _lastFrogPos;
        private readonly Dictionary<int, Position> _lastLogPos = new();
        private readonly Dictionary<int, Position> _lastTurtlePos = new();

        // Per-row mapping: prevX -> exact dx this tick (after optional clamp)
        private readonly Dictionary<int, Dictionary<int, int>> _dxByRowPrevX = new();

        // Row-level fallback if a precise prevX is missing (median of dx samples)
        private readonly Dictionary<int, int> _rowFallbackDx = new();

        // For turtles that just became hidden in this tick (instant death zones)
        private readonly Dictionary<int, HashSet<int>> _turtleHiddenNow = new();

        // Conveyor lanes: per Y row -> set of platform tiles from T-1
        private readonly Dictionary<int, HashSet<int>> _platformsPrevByRow = new();

        // Currently unused but kept for clarity if per-lane deltas are needed later
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

        private readonly Dictionary<int, LevelLayout> _levelLayouts = new(); // 1-based level index

        // --- Path resolving for relative CSV files ---
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

        /// <summary>
        /// Parses a grid CSV file (top-down order) into a <see cref="LevelLayout"/>.
        /// Supported tokens: 1=frog start, 2/3=trucks 0/180, 4/5=cars 0/180, 6=logs, 7=turtles, 8=pads.
        /// </summary>
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

        /// <summary>
        /// Loads all configured level layouts into memory.
        /// </summary>
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

        /// <summary>
        /// Applies the given level layout to all agents (positions, headings) and resets snapshots.
        /// Agents in excess are parked off-grid to keep ids stable.
        /// </summary>
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

            // Snapshots / lane maps
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);

            _lastLogPos.Clear();
            foreach (var lg in Logs) _lastLogPos[lg.AgentId] = Position.CreatePosition(lg.Position.X, lg.Position.Y);

            _lastTurtlePos.Clear();
            foreach (var tu in Turtles)
                _lastTurtlePos[tu.AgentId] = Position.CreatePosition(tu.Position.X, tu.Position.Y);

            RebuildPlatformsPrevMap(); // based on current positions
            _laneDeltaByRow.Clear(); // no delta at level start

            Console.WriteLine($"[Level] Applied layout {levelIndex}");
        }

        // ---------- Init / Loop ----------

        /// <summary>
        /// Creates agents, loads level layouts, applies initial level, binds the visualization server and starts it.
        /// </summary>
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

            // Assign stable ids across all agents for the renderer
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

            // Timer/lives
            _startTimeSeconds = _dataVisualizationServer.StartTimeSeconds;
            _timeLeft = _startTimeSeconds;
            _startLives = _dataVisualizationServer.StartLives;
            _lives = _startLives;
            _dataVisualizationServer.SetLives(_lives);

            // Bind viz server references
            _dataVisualizationServer.Frog = ActiveFrog;
            _dataVisualizationServer.Cars = Cars;
            _dataVisualizationServer.Trucks = Trucks;
            _dataVisualizationServer.Logs = Logs;
            _dataVisualizationServer.Turtles = Turtles;
            _dataVisualizationServer.Pads = Pads;

            _dataVisualizationServer.RunInBackground();
            return true;
        }

        /// <summary>
        /// Blocks until the client has sent "start"; also respects pause/resume.
        /// Resets the game state at each new start phase.
        /// </summary>
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

        /// <summary>
        /// Main per-tick update: computes conveyor motion, applies carry if conditions match,
        /// checks water/pads/vehicles and updates timers. Snapshots are updated afterward.
        /// </summary>
        public void Tick()
        {
            if (_gameOver || ActiveFrog == null) return;

            // 1) compute exact lane motion (dx) for rows based on T-1 -> T
            ComputeLaneMotionMaps();

            int yPrev = (int)_lastFrogPos.Y;
            int yNow = (int)ActiveFrog.Position.Y;

            bool sameRow = (yNow == yPrev);
            bool wasOnPrevPlatformSameRow =
                sameRow &&
                _platformsPrevByRow.TryGetValue(yPrev, out var prevXs) &&
                prevXs.Contains((int)_lastFrogPos.X);

            // Carry only if same row and frog stood on a platform in T-1
            if (wasOnPrevPlatformSameRow)
            {
                if (_dxByRowPrevX.TryGetValue(yPrev, out var map) &&
                    map.TryGetValue((int)_lastFrogPos.X, out var dx))
                {
                    // Turtle just dove? => instant death
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
                        } // Edge = death

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

            // 2) Water/Pad/Collision
            // Grace: if the frog was on a platform on the same row in T-1, do not drown this tick.
            if (IsWaterTile(ActiveFrog.Position))
            {
                if (!wasOnPrevPlatformSameRow)
                {
                    KillActiveFrog();
                    goto AFTER_MOVES;
                }
                // else: one-tick grace while being carried
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
            // 4) Timer
            if (Context.CurrentTick % TicksPerSecondDivisor == 0)
            {
                if (_timeLeft > 0) _timeLeft--;
                if (_timeLeft == 0) KillActiveFrog();
            }

            // 5) Snapshots & platform map for next tick
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);

            _lastLogPos.Clear();
            foreach (var lg in Logs) _lastLogPos[lg.AgentId] = Position.CreatePosition(lg.Position.X, lg.Position.Y);

            _lastTurtlePos.Clear();
            foreach (var tu in Turtles)
                _lastTurtlePos[tu.AgentId] = Position.CreatePosition(tu.Position.X, tu.Position.Y);

            RebuildPlatformsPrevMap();
        }

        /// <summary>
        /// Sends the current state to the client and waits for the ACK tick to advance.
        /// </summary>
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

        /// <summary>
        /// Decrements lives, resets the frog and signals game over if no lives remain.
        /// </summary>
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

        /// <summary>
        /// Moves the frog back to the level spawn and resets jump counter and timer.
        /// </summary>
        private void ResetActiveFrogToStart()
        {
            ActiveFrog.Position = _frogStart;
            _lastFrogPos = Position.CreatePosition(ActiveFrog.Position.X, ActiveFrog.Position.Y);
            ActiveFrog.Jumps = 0;
            ResetTimer();
        }

        /// <summary>
        /// Returns true if a position is in the water rows and not on a platform/pad tile.
        /// </summary>
        private bool IsWaterTile(Position pos)
        {
            // Example: rows 0..6 are water
            if (pos.Y >= 0 && pos.Y <= 6)
            {
                bool onPad = Pads.Any(pad => pad.Position.Equals(pos));
                bool onLog = Logs.Any(log => log.Position.Equals(pos));
                bool onTurtle = Turtles.Any(tut => !tut.Hidden && tut.Position.Equals(pos));
                return !onPad && !onLog && !onTurtle;
            }

            return false;
        }

        /// <summary>
        /// Returns true if a car or truck occupies the same tile.
        /// </summary>
        private bool CollidesWithVehicle(Position p)
        {
            return Cars.Any(c => c.Position.Equals(p)) ||
                   Trucks.Any(t => t.Position.Equals(p));
        }

        /// <summary>
        /// Resets lives, timer, pads and applies the selected start level as chosen by the client.
        /// </summary>
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

        /// <summary>Resets the countdown timer to the configured start value.</summary>
        private void ResetTimer() => _timeLeft = _startTimeSeconds;

        // === Conveyor calculations ===

        /// <summary>
        /// Builds the set of platform tiles per row from the current (T) positions
        /// to be used as "previous tiles" in the next tick.
        /// </summary>
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
                if (tu.Hidden) continue; // hidden turtles are not platforms
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

        /// <summary>
        /// Computes per-row exact dx samples from previous snapshot to current state (with clamping).
        /// Also tracks turtles that became hidden in this tick for instant-death handling.
        /// </summary>
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
                int rawDx = TorusDelta((int)prev.X, (int)lg.Position.X, Width); // true dx incl. wrap
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

            // Fallback: median dx per row
            foreach (var kv in samplesByRow)
            {
                var list = kv.Value;
                list.Sort();
                int medianDx = list[list.Count / 2];
                _rowFallbackDx[kv.Key] = medianDx;
            }
        }

        /// <summary>
        /// Returns the shortest horizontal delta on a torus (wrap-aware).
        /// </summary>
        private static int TorusDelta(int prevX, int curX, int width)
        {
            int dx = curX - prevX;
            if (dx > width / 2) dx -= width;
            if (dx < -width / 2) dx += width;
            return dx;
        }
    }
}

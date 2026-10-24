using System;
using CS2M.BaseGame.Commands;

namespace CS2M.BaseGame
{
    /// <summary>
    ///     Shape and range checks for every received game command, run by the handlers before anything is queued.
    ///     A check returns false to drop the whole command; oversized arrays are cut and invalid entries removed
    ///     in place. Apply systems can then index arrays without further length checks.
    /// </summary>
    internal static class CommandValidation
    {
        // ---------- shared parts ----------

        private static bool Rotation(float[] q) => q == null || Inbound.Vector(q, 4);

        private static bool Object(SpawnCommand o) =>
            o != null && Inbound.InMap(o.Position) && Inbound.OptionalVector(o.LocalPosition, 3) &&
            Inbound.OptionalVector(o.Scale, 3) && Rotation(o.Rotation) && Rotation(o.LocalRotation) &&
            Inbound.Small(o.Elevation) && Inbound.Small(o.Intensity) && Inbound.Small(o.Age);

        private static bool CoursePos(CoursePosData p) =>
            p != null && Inbound.Ref(p.Entity) && Inbound.InMap(p.Position) && Inbound.Vector(p.Rotation, 4) &&
            Inbound.Vector(p.Elevation, 2) && Inbound.Small(p.CourseDelta) && Inbound.Small(p.SplitPosition);

        private static bool Course(NetCourseData c) =>
            c != null && Inbound.PrefabName(c.Prefab) && Inbound.Ref(c.Original) && Inbound.Curve(c.Curve) &&
            Inbound.Vector(c.Elevation, 2) && Inbound.Small(c.Length) && CoursePos(c.Start) && CoursePos(c.End);

        private static bool AreaKey(AreaKey key) =>
            key != null && !string.IsNullOrEmpty(key.Prefab) && Inbound.PrefabName(key.Prefab) &&
            Inbound.Points(key.Nodes, 3, Inbound.MaxAreaNodes) && key.Nodes.Length >= 3;

        private static bool AreaData(AreaData data)
        {
            if (data == null || !Inbound.PrefabName(data.Prefab) || !Inbound.Ref(data.Owner) ||
                !Inbound.Points(data.Nodes, 4, Inbound.MaxAreaNodes))
            {
                return false;
            }

            if (data.Cache != null || data.CacheMesh != null)
            {
                // the node cache is optional: drop it unless both parts agree
                if (data.Cache == null || data.CacheMesh == null || data.CacheMesh.Length > Inbound.MaxAreaNodes ||
                    data.Cache.Length < data.CacheMesh.Length * 3 || !Inbound.Points(Trim(data.Cache, data.CacheMesh.Length * 3), 3, Inbound.MaxAreaNodes))
                {
                    data.Cache = null;
                    data.CacheMesh = null;
                }
            }

            return true;
        }

        private static float[] Trim(float[] a, int length)
        {
            if (a.Length == length)
            {
                return a;
            }

            var cut = new float[length];
            Array.Copy(a, cut, length);
            return cut;
        }

        private static bool LineRef(LineRef line) =>
            line != null && !string.IsNullOrEmpty(line.Prefab) && Inbound.PrefabName(line.Prefab) &&
            Inbound.Points(line.Waypoints, 3, Inbound.MaxWaypointsPerLine);

        // ---------- commands ----------

        public static bool Validate(AreaChangeCommand c)
        {
            c.Deleted = Inbound.Filter(c.Deleted, Inbound.MaxAreasPerCommand, AreaKey, "Area removals");
            c.Updated = Inbound.Filter(c.Updated, Inbound.MaxAreasPerCommand, u => AreaKey(u.Old) && AreaData(u.New), "Area changes");
            c.Created = Inbound.Filter(c.Created, Inbound.MaxAreasPerCommand, AreaData, "New areas");
            return true;
        }

        public static bool Validate(PolicyCommand c)
        {
            c.Adjustment = Inbound.Clamp(c.Adjustment, -Inbound.MaxValue, Inbound.MaxValue);
            return !string.IsNullOrEmpty(c.Policy) && Inbound.PrefabName(c.Policy) && (c.District == null || AreaKey(c.District));
        }

        public static bool Validate(DistrictNamesCommand c)
        {
            if (c.Districts == null || c.Indices == null)
            {
                return false;
            }

            int count = Math.Min(Math.Min(c.Districts.Length, c.Indices.Length), Inbound.MaxListEntries);
            c.Districts = Inbound.Cap(c.Districts, count, "District names");
            c.Indices = Inbound.Cap(c.Indices, count, "District names");
            for (int i = 0; i < count; i++)
            {
                if (c.Districts[i] != null && !AreaKey(c.Districts[i]))
                {
                    c.Districts[i] = null;
                }

                // an index into the district name list; never negative
                c.Indices[i] = Math.Max(0, c.Indices[i]);
            }

            return true;
        }

        public static bool Validate(SpawnCommand c) =>
            !string.IsNullOrEmpty(c.Prefab) && Inbound.PrefabName(c.Prefab) && Inbound.Ref(c.Attached) && Object(c);

        public static bool Validate(LevelUpCommand c) =>
            c.Building != null && c.Building.Kind == 3 && Inbound.Ref(c.Building) &&
            !string.IsNullOrEmpty(c.NewPrefab) && Inbound.PrefabName(c.NewPrefab);

        public static bool Validate(AbandonCommand c) => c.Building != null && c.Building.Kind == 3 && Inbound.Ref(c.Building);

        public static bool Validate(BuildingPolicyCommand c)
        {
            c.Adjustment = Inbound.Clamp(c.Adjustment, -Inbound.MaxValue, Inbound.MaxValue);
            return Inbound.Ref(c.Target) && !string.IsNullOrEmpty(c.Policy) && Inbound.PrefabName(c.Policy);
        }

        public static bool Validate(RenameCommand c)
        {
            c.Name = Inbound.CleanName(c.Name);
            return Inbound.Ref(c.Target);
        }

        public static bool Validate(BulldozeCommand c)
        {
            c.Targets = Inbound.Filter(c.Targets, Inbound.MaxRefsPerCommand, Inbound.Ref, "Bulldoze targets");
            return true;
        }

        public static bool Validate(ColorCommand c)
        {
            if (!Inbound.Ref(c.Target))
            {
                return false;
            }

            float[] colors = Inbound.Cap(c.Colors, Inbound.MaxColorSets * 12, "Colours");
            if (colors != null)
            {
                for (int i = 0; i < colors.Length; i++)
                {
                    colors[i] = Inbound.Clamp(colors[i], 0f, 16f);
                }
            }

            c.Colors = colors;
            return true;
        }

        public static bool Validate(TaxCommand c)
        {
            if (c.Rates == null || c.Rates.Length == 0 || c.Rates.Length > Inbound.MaxListEntries)
            {
                return false;
            }

            // the table holds offsets of a few percent; the game's own limits are applied when it is written
            for (int i = 0; i < c.Rates.Length; i++)
            {
                c.Rates[i] = Math.Max(-100, Math.Min(100, c.Rates[i]));
            }

            return true;
        }

        public static bool Validate(BudgetCommand c)
        {
            if (c.Services == null || c.Budgets == null)
            {
                return false;
            }

            int count = Math.Min(Math.Min(c.Services.Length, c.Budgets.Length), Inbound.MaxListEntries);
            c.Services = Inbound.Cap(c.Services, count, "Service budgets");
            c.Budgets = Inbound.Cap(c.Budgets, count, "Service budgets");
            for (int i = 0; i < count; i++)
            {
                if (!Inbound.PrefabName(c.Services[i]))
                {
                    c.Services[i] = null;
                }

                c.Budgets[i] = Math.Max(Systems.EconomySyncSystem.MinBudget, Math.Min(Systems.EconomySyncSystem.MaxBudget, c.Budgets[i]));
            }

            return true;
        }

        public static bool Validate(PopulationCommand c)
        {
            c.Population = Math.Max(0, c.Population);
            c.PopulationWithMoveIn = Math.Max(0, c.PopulationWithMoveIn);
            c.AverageHappiness = Math.Max(0, Math.Min(100, c.AverageHappiness));
            c.AverageHealth = Math.Max(0, Math.Min(100, c.AverageHealth));
            return true;
        }

        public static bool Validate(BudgetFiguresCommand c)
        {
            c.Income = Inbound.Cap(c.Income, Inbound.MaxListEntries, "Budget income");
            c.Expenses = Inbound.Cap(c.Expenses, Inbound.MaxListEntries, "Budget expenses");
            c.Services = Inbound.Cap(c.Services, Inbound.MaxListEntries, "Budget services");
            c.Upkeep = Inbound.Cap(c.Upkeep, Inbound.MaxListEntries, "Budget upkeep");
            c.FeeResources = Inbound.Cap(c.FeeResources, Inbound.MaxListEntries, "Budget fees");
            if (c.FeeResources != null && (c.FeeValues == null || c.FeeValues.Length != c.FeeResources.Length * 6 ||
                                           !Array.TrueForAll(c.FeeValues, Inbound.Finite)))
            {
                c.FeeResources = null;
                c.FeeValues = null;
            }

            if (c.Services != null)
            {
                for (int i = 0; i < c.Services.Length; i++)
                {
                    if (!Inbound.PrefabName(c.Services[i]))
                    {
                        c.Services[i] = null;
                    }
                }
            }

            return true;
        }

        public static bool Validate(NetDiffCommand c)
        {
            c.DeletedEdges = Inbound.Filter(c.DeletedEdges, Inbound.MaxRefsPerCommand, r => Inbound.Ref(r, 2), "Removed road segments");
            c.DeletedNodes = Inbound.Filter(c.DeletedNodes, Inbound.MaxRefsPerCommand, r => Inbound.Ref(r, 1), "Removed junctions");
            c.CreatedEdges = Inbound.Filter(c.CreatedEdges, Inbound.MaxRefsPerCommand,
                e => !string.IsNullOrEmpty(e.Prefab) && Inbound.PrefabName(e.Prefab) && Inbound.Curve(e.Curve) &&
                     Inbound.Vector(e.Elevation, 2) && Inbound.InMap(e.StartNode) && Inbound.InMap(e.EndNode) &&
                     Inbound.Vector(e.StartElevation, 2) && Inbound.Vector(e.EndElevation, 2),
                "New road segments");
            return true;
        }

        public static bool Validate(PlaceCommand c)
        {
            c.Definitions = Inbound.Filter(c.Definitions, Inbound.MaxDefinitionsPerCommand, Definition, "Placement definitions");
            return true;
        }

        private static bool Definition(DefinitionData d) =>
            Inbound.PrefabName(d.Prefab) && Inbound.PrefabName(d.SubPrefab) && Inbound.PrefabName(d.AttachedPrefab) &&
            Inbound.PrefabName(d.OwnerPrefab) && Inbound.Ref(d.Original) && Inbound.Ref(d.Owner) && Inbound.Ref(d.Attached) &&
            (d.Object == null || Object(d.Object)) &&
            (!d.HasOwnerDefinition || (Inbound.InMap(d.OwnerPosition) && Rotation(d.OwnerRotation))) &&
            (d.Course == null || Course(d.Course)) &&
            (d.AreaNodes == null || Inbound.Points(d.AreaNodes, 4, Inbound.MaxAreaNodes));

        public static bool Validate(ProgressionCommand c)
        {
            c.XP = Math.Max(0, c.XP);
            c.Milestone = Math.Max(0, Math.Min(1000, c.Milestone));
            c.DevTreePoints = Math.Max(0, c.DevTreePoints);
            c.LoanAmount = Math.Max(0, c.LoanAmount);
            c.Creditworthiness = Math.Max(0, c.Creditworthiness);
            return true;
        }

        public static bool Validate(DevTreeCommand c)
        {
            c.Nodes = Inbound.Filter(c.Nodes, Inbound.MaxListEntries, Inbound.PrefabName, "Development nodes");
            return c.Nodes != null;
        }

        public static bool Validate(LoanRequestCommand c)
        {
            c.Amount = Math.Max(0, c.Amount);
            return true;
        }

        public static bool Validate(DevTreeRequestCommand c) => !string.IsNullOrEmpty(c.Node) && Inbound.PrefabName(c.Node);

        private static bool Pairs(float[] values)
        {
            if (values == null || values.Length % 2 != 0)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i += 2)
            {
                if (!Inbound.InMap(values[i], 0f, values[i + 1]))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool Validate(MapTilesCommand c)
        {
            c.Owned = Inbound.Cap(c.Owned, Inbound.MaxListEntries * 2, "Owned map tiles");
            return Pairs(c.Owned);
        }

        public static bool Validate(TilePurchaseRequestCommand c)
        {
            c.Tiles = Inbound.Cap(c.Tiles, Inbound.MaxListEntries * 2, "Map tile purchase");
            return Pairs(c.Tiles);
        }

        public static bool Validate(RoadCommand c)
        {
            c.Courses = Inbound.Filter(c.Courses, Inbound.MaxCoursesPerCommand, Course, "Road courses");
            return true;
        }

        public static bool Validate(RoadUpgradeCommand c)
        {
            c.Edges = Inbound.Filter(c.Edges, Inbound.MaxRefsPerCommand,
                e => e.Edge != null && e.Edge.Kind == 2 && Inbound.Ref(e.Edge) && Inbound.PrefabName(e.Prefab) &&
                     Inbound.Curve(e.Curve) && Inbound.InMap(e.StartNode) && Inbound.OptionalVector(e.Elevation, 2) &&
                     (e.SubReplacements == null || e.SubReplacements.Length <= Inbound.MaxSubReplacements),
                "Road segment upgrades");
            c.Nodes = Inbound.Filter(c.Nodes, Inbound.MaxRefsPerCommand,
                n => n.Node != null && n.Node.Kind == 1 && Inbound.Ref(n.Node) && Inbound.PrefabName(n.Prefab),
                "Junction upgrades");
            return true;
        }

        public static bool Validate(SpeedCommand c)
        {
            // pause, 1x-3x; a little room for speed mods
            c.Speed = Inbound.Clamp(c.Speed, 0f, 16f, -1f);
            return c.Speed >= 0f;
        }

        public static bool Validate(TerrainBrushCommand c)
        {
            if (string.IsNullOrEmpty(c.Tool) || string.IsNullOrEmpty(c.Brush) || !Inbound.PrefabName(c.Tool) ||
                !Inbound.PrefabName(c.Brush) || c.Stamps == null)
            {
                return false;
            }

            const int size = TerrainBrushCommand.StampSize;
            int stamps = c.Stamps.Length / size;
            if (stamps > Inbound.MaxStampsPerCommand)
            {
                Inbound.Warn("cap:Terrain stamps", $"Terrain stamps: {stamps} received, only the first {Inbound.MaxStampsPerCommand} are used");
                stamps = Inbound.MaxStampsPerCommand;
            }

            // keep only whole, sane stamps, with size and strength within what the terrain tool allows
            var kept = new float[stamps * size];
            int count = 0;
            float[] s = c.Stamps;
            for (int n = 0; n < stamps; n++)
            {
                int i = n * size;
                if (!Inbound.InMap(s[i], s[i + 1], s[i + 2]) || !Inbound.InMap(s[i + 3], s[i + 4], s[i + 5]) ||
                    !Inbound.InMap(s[i + 6], s[i + 7], s[i + 8]) || !Inbound.Finite(s[i + 9]) ||
                    !Inbound.Finite(s[i + 10]) || !Inbound.Finite(s[i + 11]) || !Inbound.Finite(s[i + 12]) ||
                    !Inbound.Finite(s[i + 13]))
                {
                    continue;
                }

                int o = count * size;
                Array.Copy(s, i, kept, o, size);
                kept[o + 9] = Inbound.Clamp(s[i + 9], -1000f, 1000f);
                kept[o + 10] = Inbound.Clamp(s[i + 10], 1f, Game.Tools.TerrainToolSystem.kMaxBrushSizeInGame);
                kept[o + 11] = Inbound.Clamp(s[i + 11], -1f, 1f);
                kept[o + 12] = Inbound.Clamp(s[i + 12], 0f, 1f);
                kept[o + 13] = Inbound.Clamp(s[i + 13], 0f, 1f);
                count++;
            }

            c.Stamps = count == stamps ? kept : Trim(kept, count * size);
            return count > 0;
        }

        public static bool Validate(TransitLineCommand c)
        {
            if (string.IsNullOrEmpty(c.Prefab) || !Inbound.PrefabName(c.Prefab) || (c.Original != null && !LineRef(c.Original)) ||
                !Inbound.Ref(c.Owner) || c.Waypoints == null || c.Waypoints.Length > Inbound.MaxWaypointsPerLine)
            {
                return false;
            }

            foreach (LineWaypointData waypoint in c.Waypoints)
            {
                if (waypoint == null || !Inbound.InMap(waypoint.Position) || !Inbound.Ref(waypoint.Connection))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool Validate(TransitLineDeleteCommand c) => LineRef(c.Line);

        public static bool Validate(TransitLineSettingsCommand c)
        {
            if (!LineRef(c.Line))
            {
                return false;
            }

            c.Name = Inbound.CleanName(c.Name);
            c.Policies = Inbound.Filter(c.Policies, Inbound.MaxPoliciesPerCommand,
                p => !string.IsNullOrEmpty(p.Policy) && Inbound.PrefabName(p.Policy) && Inbound.Small(p.Adjustment),
                "Line policies");
            return true;
        }

        public static bool Validate(WaterSourceCommand c)
        {
            c.Changes = Inbound.Filter(c.Changes, Inbound.MaxWaterChangesPerCommand, Water, "Water source changes");
            return c.Changes != null;
        }

        private static bool Water(WaterSourceChange w)
        {
            if (w.Kind < 0 || w.Kind > 2 || (w.Kind != 0 && !Inbound.InMap(w.OldPosition)) ||
                (w.Kind != 2 && !Inbound.InMap(w.Position)))
            {
                return false;
            }

            w.Radius = Inbound.Clamp(w.Radius, 0f, Inbound.MapExtent);
            w.Height = Inbound.Clamp(w.Height, -Inbound.HeightExtent, Inbound.HeightExtent);
            w.Multiplier = Inbound.Clamp(w.Multiplier, 0f, 1000f);
            w.Polluted = Inbound.Clamp(w.Polluted, 0f, 1f);
            w.Modifier = Inbound.Clamp(w.Modifier, -1000f, 1000f);
            return true;
        }

        public static bool Validate(ZoneCellsCommand c)
        {
            if (!Inbound.InMap(c.PosX, c.PosY, c.PosZ) || !Inbound.Finite(c.DirX) || !Inbound.Finite(c.DirY) ||
                c.SizeX <= 0 || c.SizeY <= 0 || c.SizeX > 256 || c.SizeY > 256 || c.Cells == null || c.Zones == null)
            {
                return false;
            }

            int count = Math.Min(Math.Min(c.Cells.Length, c.Zones.Length), Math.Min(c.SizeX * c.SizeY, Inbound.MaxZoneCells));
            c.Cells = Inbound.Cap(c.Cells, count, "Zone cells");
            c.Zones = Inbound.Cap(c.Zones, count, "Zone cells");
            return true;
        }
    }
}

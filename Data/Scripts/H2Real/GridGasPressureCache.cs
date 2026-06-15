using System.Collections.Generic;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace TSUT.H2Real
{
    public class GridGasPressureCache
    {
        private static readonly Dictionary<long, GridGasPressureCache> _instances
            = new Dictionary<long, GridGasPressureCache>();

        public static GridGasPressureCache GetOrCreate(IMyCubeGrid grid)
        {
            GridGasPressureCache cache;
            if (!_instances.TryGetValue(grid.EntityId, out cache))
            {
                cache = new GridGasPressureCache(grid);
                _instances[grid.EntityId] = cache;
            }
            return cache;
        }

        public static bool TryGet(IMyCubeGrid grid, out GridGasPressureCache cache)
        {
            return _instances.TryGetValue(grid.EntityId, out cache);
        }

        private readonly IMyCubeGrid _grid;
        private readonly List<IMyConveyorSorter> _cachedSorters = new List<IMyConveyorSorter>();
        private readonly List<GasGeneratorController> _registeredGenerators = new List<GasGeneratorController>();

        private GridGasPressureCache(IMyCubeGrid grid)
        {
            _grid = grid;
            grid.OnBlockAdded += OnBlockAdded;
            grid.OnBlockRemoved += OnBlockRemoved;
            grid.OnClose += OnGridClose;

            var termSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
            var sorters = new List<IMyConveyorSorter>();
            termSystem?.GetBlocksOfType(sorters);
            foreach (var s in sorters)
            {
                _cachedSorters.Add(s);
                s.IsWorkingChanged += OnSorterWorkingChanged;
            }
        }

        public void Register(GasGeneratorController generator)
        {
            if (!_registeredGenerators.Contains(generator))
                _registeredGenerators.Add(generator);
        }

        public void Unregister(GasGeneratorController generator)
        {
            _registeredGenerators.Remove(generator);
        }

        private void InvalidateAll()
        {
            foreach (var gen in _registeredGenerators)
                gen.InvalidateTankCache();
        }

        private void OnGridClose(IMyEntity entity)
        {
            Cleanup();
            _instances.Remove(_grid.EntityId);
        }

        private void Cleanup()
        {
            _grid.OnBlockAdded -= OnBlockAdded;
            _grid.OnBlockRemoved -= OnBlockRemoved;
            _grid.OnClose -= OnGridClose;
            foreach (var s in _cachedSorters)
                s.IsWorkingChanged -= OnSorterWorkingChanged;
            _cachedSorters.Clear();
            _registeredGenerators.Clear();
        }

        private void OnBlockAdded(IMySlimBlock slim)
        {
            var fat = slim.FatBlock;
            if (fat is IMyGasTank || fat is IMyGasGenerator)
            {
                InvalidateAll();
            }
            else if (fat is IMyConveyorSorter)
            {
                var sorter = (IMyConveyorSorter)fat;
                if (!_cachedSorters.Contains(sorter))
                {
                    _cachedSorters.Add(sorter);
                    sorter.IsWorkingChanged += OnSorterWorkingChanged;
                }
                InvalidateAll();
            }
        }

        private void OnBlockRemoved(IMySlimBlock slim)
        {
            var fat = slim.FatBlock;
            if (fat is IMyGasTank || fat is IMyGasGenerator)
            {
                InvalidateAll();
            }
            else if (fat is IMyConveyorSorter)
            {
                var sorter = (IMyConveyorSorter)fat;
                sorter.IsWorkingChanged -= OnSorterWorkingChanged;
                _cachedSorters.Remove(sorter);
                InvalidateAll();
            }
        }

        private void OnSorterWorkingChanged(IMyCubeBlock block)
        {
            InvalidateAll();
        }
    }
}

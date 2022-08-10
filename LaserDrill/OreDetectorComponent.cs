using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ParallelTasks;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Generics;
using VRage.ModAPI;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;
using VRageRender.Utils;
using OreDepositMarker = VRage.MyTuple<VRageMath.Vector3D, Phoenix.LaserDrill.MyEntityOreDeposit>;

namespace Phoenix.LaserDrill
{
    public class MyEntityOreDeposit
    {
        public struct Data
        {
            public MyVoxelMaterialDefinition Material;
            public Vector3 AverageLocalPosition;
            public List<Vector3D> Positions;
            internal void ComputeWorldPosition(MyVoxelBase voxelMap, out Vector3D oreWorldPosition)
            {
                MyVoxelCoordSystems.LocalPositionToWorldPosition((voxelMap.PositionComp.GetPosition() - (Vector3D)voxelMap.StorageMin), ref AverageLocalPosition, out oreWorldPosition);
            }
        }

        public MyVoxelBase VoxelMap;
        public Vector3I CellCoord;
        public readonly List<Data> Materials = new List<Data>();

        public MyEntityOreDeposit(MyVoxelBase voxelMap, Vector3I cellCoord)
        {
            VoxelMap = voxelMap;
            CellCoord = cellCoord;
        }

        public class TypeComparer : IEqualityComparer<MyEntityOreDeposit>
        {
            bool IEqualityComparer<MyEntityOreDeposit>.Equals(MyEntityOreDeposit x, MyEntityOreDeposit y)
            {
                return x.VoxelMap.EntityId == y.VoxelMap.EntityId &&
                    x.CellCoord == y.CellCoord;
            }

            int IEqualityComparer<MyEntityOreDeposit>.GetHashCode(MyEntityOreDeposit obj)
            {
                return (int)(obj.VoxelMap.EntityId ^ obj.CellCoord.GetHashCode());
            }
        }

        public static readonly TypeComparer Comparer = new TypeComparer();
    }

    public class MyOreDepositGroup
    {
        private readonly MyVoxelBase m_voxelMap;
        private readonly Action<MyDepositQuery, ConcurrentCachingList<MyEntityOreDeposit>, List<Vector3I>> m_onDepositQueryComplete;

        private Dictionary<Vector3I, MyEntityOreDeposit> m_depositsByCellCoord = new Dictionary<Vector3I, MyEntityOreDeposit>(Vector3I.Comparer);
        private HashSet<Vector3I> m_emptyCellCoord = new HashSet<Vector3I>(Vector3I.Comparer);

        private Vector3I m_lastDetectionMin;
        private Vector3I m_lastDetectionMax;

        private int m_tasksRunning;
        private int m_completed = 0;

        public bool Scanning { get; private set; }

        public float Complete
        {
            get
            {
                return (float)(4 - m_tasksRunning)/4;
            }
        }

        public int Completed => m_completed;
        
        public ICollection<MyEntityOreDeposit> Deposits
        {
            get { return m_depositsByCellCoord.Values; }
        }

        public MyOreDepositGroup(MyVoxelBase voxelMap)
        {
            m_voxelMap = voxelMap;
            m_onDepositQueryComplete = OnDepositQueryComplete;
            m_lastDetectionMax = new Vector3I(int.MinValue);
            m_lastDetectionMin = new Vector3I(int.MaxValue);
        }

        private void OnDepositQueryComplete(MyDepositQuery query, ConcurrentCachingList<MyEntityOreDeposit> deposits, List<Vector3I> emptyCells)
        {
            foreach (var deposit in deposits)
            {
                Vector3I depositCell = deposit.CellCoord;
                m_depositsByCellCoord[depositCell] = deposit;
                //RegisterMarker(deposit);
                //MyHud.OreMarkers.RegisterMarker(deposit);
            }

            foreach (var emptyCell in emptyCells)
            {
                m_emptyCellCoord.Add(emptyCell);
            }

            m_tasksRunning--;

            var bb = new BoundingBoxI(query.Min, query.Max);
            m_completed += bb.Size.Size;
            Logger.Instance.LogDebugOnGameThread($"Completed: {m_completed}");

            if (m_tasksRunning == 0)
                Scanning = false;
        }

        public void UpdateDeposits(ref BoundingSphereD worldDetectionSphere)
        {
            if (m_tasksRunning != 0)
            {
                return;
            }

            var session = Sandbox.ModAPI.MyAPIGateway.Session;
            if (session == null)
            {
                return;
            }

            Vector3I min, max;
            var worldMin = worldDetectionSphere.Center - worldDetectionSphere.Radius;
            var worldMax = worldDetectionSphere.Center + worldDetectionSphere.Radius;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxelMap.PositionLeftBottomCorner, ref worldMin, out min);
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(m_voxelMap.PositionLeftBottomCorner, ref worldMax, out max);
            // mk:TODO Get rid of this computation. Might require a mechanism to figure out whether MyVoxelMap is subpart of MyPlanet or not. (Maybe third class for subparts?)
            min += m_voxelMap.StorageMin;
            max += m_voxelMap.StorageMin;

            (m_voxelMap.Storage as IMyStorage).ClampVoxelCoord(ref min);
            (m_voxelMap.Storage as IMyStorage).ClampVoxelCoord(ref max);
            min >>= (MyOreDetectorComponent.CELL_SIZE_IN_VOXELS_BITS + MyOreDetectorComponent.QUERY_LOD);
            max >>= (MyOreDetectorComponent.CELL_SIZE_IN_VOXELS_BITS + MyOreDetectorComponent.QUERY_LOD);

            if (min == m_lastDetectionMin && max == m_lastDetectionMax)
            {
                return;
            }

            Scanning = true;
            m_completed = 0;

            m_depositsByCellCoord.Clear();
            m_emptyCellCoord.Clear();

            m_lastDetectionMin = min;
            m_lastDetectionMax = max;

            int stepX = (max.X - min.X) / 2;
            int stepY = (max.Y - min.Y) / 2;

            Vector3I cmin;
            Vector3I cmax;
            cmin.Z = min.Z;
            cmax.Z = max.Z;

            // split to 4 parts, because this is still done on main thread and starting task eats alot of time
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    cmin.X = min.X + (x * stepX);
                    cmin.Y = min.Y + (y * stepY);
                    cmax.X = cmin.X + stepX;
                    cmax.Y = cmin.Y + stepY;

                    MyDepositQuery.Start(cmin, cmax, m_voxelMap, m_onDepositQueryComplete);
                    m_tasksRunning++;
                }
            }
        }
    }

    public class MyOreDetectorComponent
    {

        public const int QUERY_LOD = 1;
        public const int CELL_SIZE_IN_VOXELS_BITS = 3;
        public const int CELL_SIZE_IN_LOD_VOXELS = 1 << CELL_SIZE_IN_VOXELS_BITS;
        public const float CELL_SIZE_IN_METERS = MyVoxelConstants.VOXEL_SIZE_IN_METRES * (1 << (CELL_SIZE_IN_VOXELS_BITS + QUERY_LOD));
        public const float CELL_SIZE_IN_METERS_HALF = CELL_SIZE_IN_METERS * 0.5f;

        private static readonly List<MyVoxelBase> m_inRangeCache = new List<MyVoxelBase>();
        private static readonly List<MyVoxelBase> m_notInRangeCache = new List<MyVoxelBase>();

        public delegate bool CheckControlDelegate();

        public float DetectionRadius { get; set; }

        public CheckControlDelegate OnCheckControl;

        public bool BroadcastUsingAntennas { get; set; }

        public IMyEntity ReferenceEntity { get; set; }

        private readonly Dictionary<MyVoxelBase, MyOreDepositGroup> m_depositGroupsByEntity = new Dictionary<MyVoxelBase, MyOreDepositGroup>();
        public Dictionary<MyVoxelBase, MyOreDepositGroup> DepositGroupsByEntity => m_depositGroupsByEntity;

        //Object m_miningInformationLock = new object();
        ConcurrentCachingHashSet<MiningInformation> m_miningInformation = new ConcurrentCachingHashSet<MiningInformation>();
        public ConcurrentCachingHashSet<MiningInformation> MiningInformation => m_miningInformation;

        // Aggregated list is for PB and terminal display
        //Object m_aggregatedMiningInformationLock = new object();
        private ConcurrentCachingList<MiningInformationPB> m_aggregatedResult = new ConcurrentCachingList<MiningInformationPB>();
        public ConcurrentCachingList<MiningInformationPB> AggregatedMiningInformation => m_aggregatedResult;
        private ConcurrentDictionary<string, MiningInformationPB> m_aggregateCache = new ConcurrentDictionary<string, MiningInformationPB>();
        //private ConcurrentDictionary<IMyVoxelBase, ConcurrentDictionary<string, MiningInformationPB>> m_aggregateCache = new ConcurrentDictionary<IMyVoxelBase, ConcurrentDictionary<string, MiningInformationPB>>();

        public float Complete
        {
            get
            {
                if (m_depositGroupsByEntity.Count == 0)
                    return 0;
                return m_depositGroupsByEntity.Values.Average(g => g.Complete);
            }
        }

        public bool Scanning
        {
            get
            {
                if (m_depositGroupsByEntity.Count == 0 || m_aggregatedResult.Count == 0)
                    return true;
                return m_depositGroupsByEntity.Values.Any((g) => g.Scanning);
            }
        }

        public MyOreDetectorComponent()
        {
            DetectionRadius = 50;
            SetRelayedRequest = false;
            BroadcastUsingAntennas = false;
        }

        public bool SetRelayedRequest { get; set; }

        public void Update(Vector3D position, bool checkControl = true)
        {
            if (!SetRelayedRequest && checkControl && !OnCheckControl())
            {
                Clear();
                return;
            }

            Clear();

            SetRelayedRequest = false;

            var sphere = new BoundingSphereD(position, DetectionRadius);
            MyGamePruningStructure.GetAllVoxelMapsInSphere(ref sphere, m_inRangeCache);

            RemoveVoxelMapsOutOfRange();
            AddVoxelMapsInRange();

            UpdateDeposits(ref sphere);

            m_inRangeCache.Clear();
        }

        private void UpdateDeposits(ref BoundingSphereD sphere)
        {
            MySimpleProfiler.Begin("UpdateAndRegisterMarkers");

            var newlist = new List<MiningInformation>();
            foreach (var group in m_depositGroupsByEntity.Values)
            {
                group.UpdateDeposits(ref sphere);

                foreach (var deposit in group.Deposits)
                {
                    foreach (var material in deposit.Materials)
                    {
                        Vector3D pos;
                        material.ComputeWorldPosition(deposit.VoxelMap, out pos);
                        Logger.Instance.LogDebug($"Material: {material.Material.MinedOre}, Count: {material.Positions.Count}, Location: {pos}");
                        newlist.Add(new MiningInformation()
                        {
                            Location = pos,
                            Material = material.Material,
                            Voxel = deposit.VoxelMap,
                            Positions = material.Positions,
                        });
                    }
                    //MyOreDepositGroup.RegisterMarker(deposit);
                    //MyHud.OreMarkers.RegisterMarker(deposit);
                }
            }

            for(var index=0; index < newlist.Count-1; index++)
            {
                var first = newlist[index];
                var second = newlist[index + 1];

                if ((first.Location - sphere.Center).LengthSquared() > (second.Location - sphere.Center).LengthSquared())
                    newlist.Move(index, index + 1);
            }

            //lock (m_miningInformationLock)
            //{
                m_miningInformation.Clear();
                newlist.ForEach((i) => m_miningInformation.Add(i));
                m_miningInformation.ApplyChanges();

                Logger.Instance.LogDebugOnGameThread($"Mining Deposit count: {m_miningInformation.Count}");
            //}

            MyAPIGateway.Parallel.StartBackground(() => BuildAggregatedResult());
            //BuildAggregatedResult();
            MySimpleProfiler.End();
        }

        private void BuildAggregatedResult()
        {
            m_aggregateCache.Clear();

            //lock (m_miningInformationLock)
            //{
                foreach (var deposit in m_miningInformation)
                {
                    MiningInformationPB mi;
                    if (m_aggregateCache.TryGetValue(deposit.Material.MinedOre, out mi))
                    {
                        mi.Count += (ulong)deposit.Positions.Count;
                    }
                    else
                    {
                        m_aggregateCache[deposit.Material.MinedOre] = new MiningInformationPB(deposit);
                    }
                }
            //}

            //lock (m_aggregatedMiningInformationLock)
            //{
                m_aggregatedResult.ClearList();

                foreach (var info in m_aggregateCache.Values)
                    m_aggregatedResult.Add(info);

                m_aggregatedResult.ApplyChanges();

                Logger.Instance.LogDebugOnGameThread($"Aggregate count: {m_aggregatedResult.Count}");
            //}
        }

        //private void BuildAggregatedResult_()
        //{
        //    m_aggregateCache.Clear();

        //    lock (m_miningInformationLock)
        //    {
        //        foreach (var deposit in m_miningInformation)
        //        {
        //            ConcurrentDictionary<string, MiningInformationPB> dictionary;
        //            if(!m_aggregateCache.TryGetValue(deposit.Voxel, out dictionary))
        //            {
        //                m_aggregateCache[deposit.Voxel] = dictionary = new ConcurrentDictionary<string, MiningInformationPB>();
        //            }

        //            MiningInformationPB mi;
        //            if (dictionary.TryGetValue(deposit.Material.MinedOre, out mi))
        //            {
        //                mi.Count += (ulong)deposit.Positions.Count;
        //            }
        //            else
        //            {
        //                Logger.Instance.LogDebugOnGameThread($"Deposit: {deposit.Material.MinedOre}, {deposit.Location}");
        //                dictionary[deposit.Material.MinedOre] = new MiningInformationPB(deposit);
        //            }
        //        }
        //    }

        //    lock (m_aggregatedMiningInformationLock)
        //    {
        //        m_aggregatedResult.ClearList();

        //        foreach (var infobyvoxel in m_aggregateCache.Values)
        //        {
        //            foreach (var info in infobyvoxel.Values)
        //            {
        //                m_aggregatedResult.Add(info);
        //            }
        //        }

        //        m_aggregatedResult.ApplyChanges();

        //        Logger.Instance.LogDebugOnGameThread($"Aggregate count: {m_aggregatedResult.Count}");
        //    }
        //}

        private void AddVoxelMapsInRange()
        {
            MySimpleProfiler.Begin("AddVoxelMapsInRange");
            foreach (var voxelMap in m_inRangeCache)
            {
                if (!m_depositGroupsByEntity.ContainsKey(voxelMap.GetTopMostParent() as MyVoxelBase))   //GK: Get only topmost in order to ignore MyVoxelPhysics
                    m_depositGroupsByEntity.Add(voxelMap, new MyOreDepositGroup(voxelMap));
            }

            m_inRangeCache.Clear();
            MySimpleProfiler.End();
        }

        private void RemoveVoxelMapsOutOfRange()
        {
            MySimpleProfiler.Begin("RemoveVoxelMapsOutOfRange");
            foreach (var voxelMap in m_depositGroupsByEntity.Keys)
            {
                if (!m_inRangeCache.Contains(voxelMap.GetTopMostParent() as MyVoxelBase))   //GK: Get only topmost in order to ignore MyVoxelPhysics
                    m_notInRangeCache.Add(voxelMap);
            }

            foreach (var notInRange in m_notInRangeCache)
            {
                m_depositGroupsByEntity.Remove(notInRange);
            }

            m_notInRangeCache.Clear();
            MySimpleProfiler.End();
        }

        public void Clear()
        {
            MySimpleProfiler.Begin("Clear markers");
            foreach (var group in m_depositGroupsByEntity.Values)
            {
                foreach (var deposit in group.Deposits)
                {
                    //MyOreDepositGroup.UnregisterMarker(deposit);
                    //MyHud.OreMarkers.UnregisterMarker(deposit);
                }
            }

            MySimpleProfiler.End();
        }

    }

    class MyDepositQuery : IWork
    {
        struct MaterialPositionData
        {
            public Vector3 Sum;
            public int Count;
            public List<Vector3D> Positions;
        }

        private static readonly MyObjectsPool<MyDepositQuery> m_instancePool = new MyObjectsPool<MyDepositQuery>(16);

        public static void Start(Vector3I min, Vector3I max, MyVoxelBase voxelMap, Action<MyDepositQuery, ConcurrentCachingList<MyEntityOreDeposit>, List<Vector3I>> completionCallback)
        {
            MyDepositQuery query = null;
            m_instancePool.AllocateOrCreate(out query);
            if (query != null)
            {
                query.Min = min;
                query.Max = max;
                query.VoxelMap = voxelMap;
                query.CompletionCallback = completionCallback;
                MyAPIGateway.Parallel.Start(query, query.m_onComplete);
                
            }
        }

        //[ThreadStatic]
        private MyStorageData m_cache;
        private MyStorageData Cache
        {
            get
            {
                if (m_cache == null)
                    m_cache = new MyStorageData();
                return m_cache;
            }
        }

        //[ThreadStatic]
        private MaterialPositionData[] m_materialData;
        private MaterialPositionData[] MaterialData
        {
            get
            {
                if (m_materialData == null)
                    m_materialData = new MaterialPositionData[byte.MaxValue];
                return m_materialData;
            }
        }

        public Vector3I Min { get; set; }

        public Vector3I Max { get; set; }

        public MyVoxelBase VoxelMap { get; set; }

        public Action<MyDepositQuery, ConcurrentCachingList<MyEntityOreDeposit>, List<Vector3I>> CompletionCallback { get; set; }

        private ConcurrentCachingList<MyEntityOreDeposit> m_result;

        private List<Vector3I> m_emptyCells;

        private HashSet<MiningInformation> m_oreLocations;

        private readonly Action m_onComplete;

        public MyDepositQuery()
        {
            m_onComplete = OnComplete;
            m_result = new ConcurrentCachingList<MyEntityOreDeposit>();
        }

        private void OnComplete()
        {
            MySimpleProfiler.Begin("MyOreDetectorComponent - OnComplete");
            CompletionCallback(this, m_result, m_emptyCells);
            CompletionCallback = null;
            //m_result = null;
            m_instancePool.Deallocate(this);
            MySimpleProfiler.End();
        }

        //WorkPriority IPrioritizedWork.Priority
        //{
        //    get { return WorkPriority.VeryLow; }
        //}

        void IWork.DoWork(WorkData workData)
        {
            MySimpleProfiler.Begin("MyDepositQuery.DoWork");
            try
            {
                m_result.ClearList();
                m_emptyCells = new List<Vector3I>();
                var cache = Cache;
                cache.Resize(new Vector3I(MyOreDetectorComponent.CELL_SIZE_IN_LOD_VOXELS));
                var storage = VoxelMap.Storage as IMyStorage;
                if (storage == null || storage.Closed)
                {
                    return;
                }

                Vector3I c;
                for (c.Z = Min.Z; c.Z <= Max.Z; ++c.Z)
                {
                    for (c.Y = Min.Y; c.Y <= Max.Y; ++c.Y)
                    {
                        for (c.X = Min.X; c.X <= Max.X; ++c.X)
                        {
                            if (storage == null || storage.Closed)
                            {
                                break;
                            }

                            ProcessCell(cache, storage, c);
                        }
                    }
                }
                m_result.ApplyChanges();
            }
            finally
            {
                MySimpleProfiler.End();
            }
        }

        private void ProcessCell(MyStorageData cache, IMyStorage storage, Vector3I cell)
        {
            var min = cell << MyOreDetectorComponent.CELL_SIZE_IN_VOXELS_BITS;
            var max = min + (MyOreDetectorComponent.CELL_SIZE_IN_LOD_VOXELS - 1);

            storage.PinAndExecute(() =>
            {
                storage.ReadRange(cache, MyStorageDataTypeFlags.Content, MyOreDetectorComponent.QUERY_LOD, min, max);
                if (!cache.ContainsVoxelsAboveIsoLevel())
                    return;

                storage.ReadRange(cache, MyStorageDataTypeFlags.Material, MyOreDetectorComponent.QUERY_LOD, min, max);
            });

            var materialData = MaterialData;
            Vector3I c;
            for (c.Z = 0; c.Z < MyOreDetectorComponent.CELL_SIZE_IN_LOD_VOXELS; ++c.Z)
            {
                for (c.Y = 0; c.Y < MyOreDetectorComponent.CELL_SIZE_IN_LOD_VOXELS; ++c.Y)
                {
                    for (c.X = 0; c.X < MyOreDetectorComponent.CELL_SIZE_IN_LOD_VOXELS; ++c.X)
                    {
                        int i = cache.ComputeLinear(ref c);
                        if (cache.Content(i) > MyVoxelDataConstants.IsoLevel)
                        {
                            const float VOXEL_SIZE = MyVoxelConstants.VOXEL_SIZE_IN_METRES * (1 << MyOreDetectorComponent.QUERY_LOD);
                            const float VOXEL_SIZE_HALF = VOXEL_SIZE * 0.5f;
                            var material = cache.Material(i);

                            Vector3 localPos = (c + min) * VOXEL_SIZE + VOXEL_SIZE_HALF;
                            materialData[material].Sum += localPos;
                            materialData[material].Count += 1;

                            var pos = Vector3.Transform(localPos - VoxelMap.SizeInMetresHalf, Quaternion.CreateFromRotationMatrix(VoxelMap.WorldMatrix));
                            Vector3D worldpos;
                            MyVoxelCoordSystems.LocalPositionToWorldPosition((VoxelMap.PositionComp.GetPosition() - (Vector3D)VoxelMap.StorageMin), ref pos, out worldpos);

                            if (materialData[material].Positions == null)
                                materialData[material].Positions = new List<Vector3D>();
                            materialData[material].Positions.Add(worldpos);
                        }
                    }
                }
            }

            MyEntityOreDeposit result = null;
            for (int materialIdx = 0; materialIdx < materialData.Length; ++materialIdx)
            {
                if (materialData[materialIdx].Count == 0)
                    continue;

                var material = MyDefinitionManager.Static.GetVoxelMaterialDefinition((byte)materialIdx);
                if (material != null && material.IsRare)
                {
                    if (result == null)
                    {
                        result = new MyEntityOreDeposit(VoxelMap, cell);
                    }

                    result.Materials.Add(new MyEntityOreDeposit.Data()
                    {
                        Material = material,
                        AverageLocalPosition = Vector3.Transform((materialData[materialIdx].Sum / materialData[materialIdx].Count - VoxelMap.SizeInMetresHalf), Quaternion.CreateFromRotationMatrix(VoxelMap.WorldMatrix)),
                        Positions = materialData[materialIdx].Positions,
                    });
                }
            }

            if (result != null)
            {
                m_result.Add(result);
            }
            else
            {
                m_emptyCells.Add(cell);
            }

            Array.Clear(materialData, 0, materialData.Length);

            return;
        }

        WorkOptions IWork.Options
        {
            get { return MyAPIGateway.Parallel.DefaultOptions; }
        }
    }
}

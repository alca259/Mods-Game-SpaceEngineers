using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;
using VRage.Voxels;

namespace Phoenix.LaserDrill
{
    public static class VoxelConstants
    {
        //  Size of a voxel in metres
        public const float VOXEL_SIZE_IN_METRES = 1f;
        public const float VOXEL_VOLUME_IN_METERS = VOXEL_SIZE_IN_METRES * VOXEL_SIZE_IN_METRES * VOXEL_SIZE_IN_METRES;
        public const float VOXEL_SIZE_IN_METRES_HALF = VOXEL_SIZE_IN_METRES / 2.0f;
        public static readonly Vector3 VOXEL_SIZE_VECTOR = new Vector3(VOXEL_SIZE_IN_METRES, VOXEL_SIZE_IN_METRES, VOXEL_SIZE_IN_METRES);
        public static readonly Vector3 VOXEL_SIZE_VECTOR_HALF = VOXEL_SIZE_VECTOR / 2.0f;
        public static readonly float VOXEL_RADIUS = VOXEL_SIZE_VECTOR_HALF.Length();
    }

    public class OreDeposit
    {
        public string Name;
        public Vector3D Location;
        public long OreCount;
    }

    public static class IMyStorageExtensions
    {
        public static void ClampVoxel(this VRage.ModAPI.IMyStorage self, ref Vector3I voxelCoord, int distance = 1)
        {
            if (self == null) return;
            var sizeMinusOne = self.Size - distance;
            Vector3I.Clamp(ref voxelCoord, ref Vector3I.Zero, ref sizeMinusOne, out voxelCoord);
        }
    }

    /// <summary>
    /// Size of the ore deposit. Taking skipped voxels into account. Voxels divided by 2^3.
    /// </summary>
    public enum DepositSize : long
    {
        None = 0,
        Minimal = 1,
        Tiny = 200,
        Small = 500,
        Medium = 2000,
        Large = 10000,
        Massive = 50000,
    }

    public static class OreConstants
    {
        public const string GPS_DESCRIPTION = "-- Added by Phoenix Ore Detector, DO NOT MODIFY -- ";
    }

    class OreDetector
    {
        public static OreDetector Instance { get { return _instance; } }
        static OreDetector _instance = null;

        static OreDetector()
        {
            _instance = new OreDetector();
        }

        public static void UnloadStatic()
        {
            _instance = null;
        }

        private List<IMyOreDetector> m_detectorsToMonitor = new List<IMyOreDetector>();
        //private FastResourceLock m_detectorLock = new FastResourceLock();

        //private List<IMyOreDetector> m_cachedDetectors = new List<IMyOreDetector>();
        //public List<IMyOreDetector> Detectors
        //{
        //    get
        //    {
        //        using (m_detectorLock.AcquireSharedUsing())
        //        {
        //            m_cachedDetectors.Clear();
        //            m_detectorsToMonitor.ForEach((b) => m_cachedDetectors.Add(b));
        //        }
        //        return m_cachedDetectors;
        //    }
        //}

        //public void AddDetector(IMyOreDetector detector)
        //{
        //    using (m_detectorLock.AcquireExclusiveUsing())
        //        m_detectorsToMonitor.Add(detector);
        //    detector.OnClose += detector_OnClose;
        //    detector.PositionComp.OnPositionChanged += detector_OnPositionChanged;
        //}

        private void detector_OnPositionChanged(VRage.Game.Components.MyPositionComponentBase obj)
        {
            //if (obj.Entity.Physics == null || obj.Entity.Physics.LinearVelocity == Vector3D.Zero)
            //    OreDetector.Instance.NeedsUpdate = true;
        }

        //void detector_OnClose(IMyEntity obj)
        //{
        //    using (m_detectorLock.AcquireExclusiveUsing())
        //        m_detectorsToMonitor.Remove(obj as IMyOreDetector);
        //    obj.OnClose -= detector_OnClose;
        //    obj.PositionComp.OnPositionChanged -= detector_OnPositionChanged;
        //}

        FastResourceLock m_voxelCacheStaticLock = new FastResourceLock();
        HashSet<IMyVoxelBase> m_voxelCacheStatic = new HashSet<IMyVoxelBase>();
        public void UpdateVoxelCache()
        {
            MyAPIGateway.Parallel.Start(() =>
            {
                var tempHashCache = new HashSet<IMyEntity>();
                //m_voxelCacheStatic.Clear();
                MyAPIGateway.Entities.GetEntities(tempHashCache, (x) => x is IMyVoxelBase);
                using (m_voxelCacheStaticLock.AcquireExclusiveUsing())
                {
                    //if( Globals.Debug )
                    //    MyAPIGateway.Utilities.ShowNotification("voxels: " + tempHashCache.Count);
                    tempHashCache.ToList().ForEach(x => m_voxelCacheStatic.Add(x as MyVoxelBase));
                    //Logger.Instance.LogDebug("UpdateVoxelCache, Voxels: " + m_voxelCacheStatic.Count);
                }
                tempHashCache.Clear();
            });
        }

        /// <summary>
        /// Returns the world location of the last position of the ore at the starting point, in a continuous line.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="dir"></param>
        /// <returns></returns>
        public Vector3D GetLastMatchingVoxelInDirection(Vector3D start, Vector3D dir)
        {
            //var startmat = GetSingleMaterialAtPoint(start);
            var previous = start;
            var incrementLength = 2;
            byte content, material;
            GetVoxelContent(start, out content, out material);
            var startmat = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);

            if (startmat == null || (material == 0 && content == MyVoxelConstants.VOXEL_CONTENT_EMPTY))
                return start;

            MyVoxelMaterialDefinition currentmat = startmat;
            dir.Normalize();                                                // Normalize, just in case it's not already
            Logger.Instance.LogDebug(string.Format("Start Mat: {2}; {0}; Pos: {1}", startmat.MinedOre, start.ToString(), material.ToString()));

            do
            {
                previous = start;
                start = start + (dir * incrementLength);
                GetVoxelContent(start, out content, out material);
                currentmat = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                //Logger.Instance.LogDebug(string.Format("Current Mat: {2}; {0}; Pos: {1}", currentmat.MinedOre, start.ToString(), material.ToString()));
                //currentmat = GetSingleMaterialAtPoint(start);
            } while (currentmat != null && currentmat.MinedOre == startmat.MinedOre && content != MyVoxelConstants.VOXEL_CONTENT_EMPTY && material != 0);
            return previous;
        }

        public MyVoxelMaterialDefinition GetSingleMaterialAtPoint(Vector3D world)
        {
            byte content, material;
            GetVoxelContent(world, out content, out material);
            return MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
        }

        public IMyVoxelBase GetVoxelContainingPoint(Vector3D world, float radius = 0.866f)
        {
            var sphere = new BoundingSphereD(world, radius);

            using (m_voxelCacheStaticLock.AcquireSharedUsing())
            {
                foreach (var voxelmap in m_voxelCacheStatic)
                {
                    //Logger.Instance.LogMessage(string.Format("Scanning voxel {0} for point: {1}", voxelmap.GetType(), world));
                    if (IsInsideVoxel(voxelmap, world, radius))
                        return voxelmap;
                }
            }
            return null;
        }

        public bool GetVoxelContent(Vector3D position, out byte content, out byte material, MyStorageData cache = null)
        {
            var voxel = GetVoxelContainingPoint(position);
            return GetVoxelContent(voxel, position, out content, out material, cache);
        }

        public bool GetVoxelContent(IMyVoxelBase voxel, Vector3D position, out byte content, out byte material, MyStorageData cache = null, Vector3D? endpoint = null, MyVoxelRequestFlags flags = 0)
        {
            if (endpoint == null)
                endpoint = position;

            content = 0;
            material = 0;

            if (voxel == null || voxel.Storage == null)
                return false;

            if (cache == null)
            {
                cache = new MyStorageData();
            }

            var targetMin = position;
            Vector3D targetMax = endpoint.Value;
            Vector3I minVoxel, maxVoxel;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref targetMin, out minVoxel);
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref targetMax, out maxVoxel);

            MyVoxelBase voxelBase = voxel as MyVoxelBase;
            minVoxel += voxelBase.StorageMin;
            maxVoxel += voxelBase.StorageMin;

            voxel.Storage.ClampVoxel(ref minVoxel);
            voxel.Storage.ClampVoxel(ref maxVoxel);

            cache.Resize(minVoxel, maxVoxel);
            voxel.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, 0, minVoxel, maxVoxel, ref flags);

            // Grab content and material
            content = cache.Content(0);
            material = cache.Material(0);

            return cache.ContainsVoxelsAboveIsoLevel();
        }

        private bool IsInsideVoxel_ALt(IMyVoxelBase voxelmap, Vector3D world, float radius = 0.866f)
        {
            var sphere = new BoundingSphereD(world, radius);
            var entity = MyAPIGateway.Entities.GetIntersectionWithSphere(ref sphere);

            if ( entity?.EntityId == voxelmap.EntityId )
                return true;

            return false;
        }

        private bool IsInsideVoxel(IMyVoxelBase voxelmap, Vector3D world, float radius = 0.866f)
        {
            byte material, content;
            GetVoxelContent(voxelmap, world, out content, out material);
            var voxelMat = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);

            if (content == MyVoxelConstants.VOXEL_CONTENT_EMPTY || voxelMat == null)
                return false;

            return true;
        }

        public bool IsInsideVoxel(Vector3D world)
        {
            using (m_voxelCacheStaticLock.AcquireSharedUsing())
            {
                foreach (var voxelmap in m_voxelCacheStatic)
                {
                    if (IsInsideVoxel(voxelmap, world))
                        return true;
                }
            }
            return false;
        }

        public static DepositSize CalculateDepositSize(ulong orecount)
        {
            if (orecount > (long)DepositSize.Massive)
                return DepositSize.Massive;
            if (orecount > (long)DepositSize.Large)
                return DepositSize.Large;
            if (orecount > (long)DepositSize.Medium)
                return DepositSize.Medium;
            if (orecount > (long)DepositSize.Small)
                return DepositSize.Small;
            if (orecount > (long)DepositSize.Tiny)
                return DepositSize.Tiny;
            if (orecount > (long)DepositSize.Minimal)
                return DepositSize.Minimal;
            else
                return DepositSize.None;
        }

        public static float CalculateAmount(MyVoxelMaterialDefinition material, float amount)
        {
            var oreObjBuilder = VRage.ObjectBuilders.MyObjectBuilderSerializer.CreateNewObject<MyObjectBuilder_Ore>(material.MinedOre);
            oreObjBuilder.MaterialTypeName = material.Id.SubtypeId;
            float amountCubicMeters = (float)(((float)amount / (float)MyVoxelConstants.VOXEL_CONTENT_FULL) * MyVoxelConstants.VOXEL_VOLUME_IN_METERS * Sandbox.Game.MyDrillConstants.VOXEL_HARVEST_RATIO);
            amountCubicMeters *= (float)material.MinedOreRatio;
            var physItem = MyDefinitionManager.Static.GetPhysicalItemDefinition(oreObjBuilder);
            MyFixedPoint amountInItemCount = (MyFixedPoint)(amountCubicMeters / physItem.Volume);
            return (float)amountInItemCount;
        }

        public static void RemoveVoxelContent(long voxelId, Vector3D position, out byte materialRemoved, out float amountOfMaterial)
        {
            materialRemoved = 0;
            amountOfMaterial = 0f;
            MyStorageData cache = new MyStorageData();

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(voxelId, out entity))
                return;

            IMyVoxelBase voxel = entity as IMyVoxelBase;

            byte original, material;
            var targetMin = position;
            var targetMax = position;
            Vector3I minVoxel, maxVoxel;
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref targetMin, out minVoxel);
            MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref targetMax, out maxVoxel);

            OreDetector.Instance.GetVoxelContent(voxel, position, out original, out material, cache);

            if (original == MyVoxelConstants.VOXEL_CONTENT_EMPTY)
            {
                Logger.Instance.LogMessage("Voxel Content empty");
                //Logging.Instance.WriteLine(string.Format("Content is empty"));
                return;
            }

            // Calculate Material Mined
            var voxelMat = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
            materialRemoved = material;
            amountOfMaterial = OreDetector.CalculateAmount(voxelMat, original * 3.9f);

            // Remove Content
            cache.Content(0, 0);
            voxel.Storage.WriteRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, minVoxel, maxVoxel);
        }
    }
}

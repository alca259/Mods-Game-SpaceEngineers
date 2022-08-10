using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Phoenix;
using VRage.Game.Components;
using Sandbox.Definitions;
using VRageMath;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage;
using VRage.Game.Entity;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI;

namespace Phoenix.LaserDrill
{
    public static class Constants
    {
        public static readonly string StatusNone = "None";
        public static readonly string StatusManual = "Static";
        public static readonly string DrillCustomName = "Phoenix_LaserDrill";

        public const float MaxStoppedTimeSec = 10.0f;
    }
    /// <summary>
    /// Interface for implementing common methods between static and turret based drills
    /// </summary>
    public interface ILaserDrill
    {
        IMyEntity SpawnFakeDrill(Vector3D location);
        IMyCubeBlock GetLargestOreDetector();
        void DisableDrill();
        void EnableDrill();
        void RemoveDrill();
        void FindOreDetector();
    }

    /// <summary>
    /// Static implementations of work from interface, called by actual interface implementation in class
    /// </summary>
    public static class LaserDrillExtensions
    {
        public static HashSet<IMyGps> _debugPoints = new HashSet<IMyGps>();

        public static void ClearGPS()
        {
            if (Globals.Debug)
            {
                foreach (var gps in _debugPoints)
                {
                    MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
                }
                _debugPoints.Clear();
            }
        }

        public static IMyEntity SpawnFakeDrill(IMyEntity m_drill, Vector3D location)
        {
            location = location + (m_drill.WorldMatrix.Forward * 30);

            if (Globals.Debug)
                MyAPIGateway.Session.GPS.AddLocalGps(MyAPIGateway.Session.GPS.Create("drill", null, location, true, true));

            var owner = (m_drill as IMyFunctionalBlock).OwnerId;
            var gridObjectBuilder = new MyObjectBuilder_CubeGrid()
            {
                PersistentFlags = MyPersistentEntityFlags2.CastShadows | MyPersistentEntityFlags2.InScene,
                GridSizeEnum = MyCubeSize.Large,
                IsStatic = true,
                CreatePhysics = true,
                LinearVelocity = new SerializableVector3(0, 0, 0),
                AngularVelocity = new SerializableVector3(0, 0, 0),
                PositionAndOrientation = new MyPositionAndOrientation(location, (Vector3)m_drill.WorldMatrix.Forward, (Vector3)m_drill.WorldMatrix.Up),
                DisplayName = Constants.DrillCustomName,
            };

            var cube = new MyObjectBuilder_Drill()
            {
                Min = new SerializableVector3I(0, 0, 0),
                SubtypeName = "Large_SC_LaserDrill_Hidden" + (m_drill is IMyLargeGatlingTurret ? "Turret" : "Static"),
                ColorMaskHSV = new SerializableVector3(0, -1, 0),
                EntityId = 0,
                Owner = owner,
                BlockOrientation = new SerializableBlockOrientation(VRageMath.Base6Directions.Direction.Forward, VRageMath.Base6Directions.Direction.Up),
                ShareMode = MyOwnershipShareModeEnum.All,
                CustomName = Constants.DrillCustomName,
                Enabled = true,
            } as MyObjectBuilder_FunctionalBlock;
            gridObjectBuilder.CubeBlocks.Add(cube);

            cube = new MyObjectBuilder_BatteryBlock()
            {
                Min = new SerializableVector3I(0, 0, 3),
                SubtypeName = "LargeBlockBatteryBlock",
                ColorMaskHSV = new SerializableVector3(0, -1, 0),
                EntityId = 0,
                Owner = owner,
                BlockOrientation = new SerializableBlockOrientation(VRageMath.Base6Directions.Direction.Forward, VRageMath.Base6Directions.Direction.Up),
                ShareMode = MyOwnershipShareModeEnum.All,
                CustomName = Constants.DrillCustomName,
                MaxStoredPower = float.MaxValue,
                CurrentStoredPower = float.MaxValue,
                OnlyDischargeEnabled = true,
                Enabled = true,
            };
            gridObjectBuilder.CubeBlocks.Add(cube);

            //if (Globals.Debug)
            //{
            //    cube = new MyObjectBuilder_RadioAntenna()
            //    {
            //        Min = new SerializableVector3I(0, 0, 4),
            //        SubtypeName = "LargeBlockRadioAntenna",
            //        ColorMaskHSV = new SerializableVector3(0, -1, 0),
            //        EntityId = 0,
            //        Owner = owner,
            //        BlockOrientation = new SerializableBlockOrientation(VRageMath.Base6Directions.Direction.Up, VRageMath.Base6Directions.Direction.Backward),
            //        ShareMode = MyOwnershipShareModeEnum.All,
            //        CustomName = Constants.DrillCustomName,
            //        Enabled = true,
            //    };
            //    gridObjectBuilder.CubeBlocks.Add(cube);
            //}

            var tempList = new List<MyObjectBuilder_EntityBase>();
            tempList.Add(gridObjectBuilder);
            tempList.CreateAndSyncEntities();

            // Get the entity and activate blocks
            var entity = MyAPIGateway.Entities.GetEntityById(gridObjectBuilder.EntityId) as IMyCubeGrid;
            entity.Flags &= ~EntityFlags.Save;
            //(entity as MyEntity).IsPreview = true;

            var block = entity.GetCubeBlock(Vector3I.Zero);
            block.FatBlock.GameLogic.GetAs<HiddenDrill>().CreatedByMod = true;
            Logger.Instance.LogDebug("Created drill: " + entity.DisplayName);
            return entity;
        }

        static long _rotCounter = 0;
        public static void ScaleBeam(IMyEntity reference, IMyEntity beam, Vector3D start, Vector3D end)
        {
            // Translations must be applied in this order: scale -> rotation -> translation
            if (MyAPIGateway.Session.Player != null)
            {
                var location = start; // +(reference.WorldMatrix.Forward * 1);
                var dir = (Vector3)(end - location);
                var scale = dir.Length() / 2;
                dir.Normalize();
                location = ((end + location) / 2);                                      // Create beam at half-way point

                try
                {
                    // Calculate scale

                    // Double check for sanity
                    if (double.IsNaN(scale) || double.IsInfinity(scale))
                        scale = 0.0f;

                    var currentMat = beam.WorldMatrix;
                    var scaleVec = new Vector3D(1.0, 1.0, scale);
                    //scaleVec *= scale;
                    Vector3 norm;
                    dir.CalculatePerpendicularVector(out norm);
                    var newMat = MatrixD.CreateFromTransformScale(Quaternion.CreateFromForwardUp(dir, norm), location, scaleVec);

                    // Calculate rotation for animation
                    var currentRot = Quaternion.CreateFromAxisAngle((Vector3)newMat.Forward, 0);
                    var worldRot = Quaternion.CreateFromAxisAngle((Vector3)newMat.Forward, -0.035f * 10 * _rotCounter++);
                    currentRot.Conjugate();
                    var newRot = worldRot * currentRot;
                    newRot.Normalize();                     // Normalize prevents overflow from long periods of rotation
                    newMat = MatrixD.Transform(newMat, newRot);

                    // Calculate Translation (should stay near gate
                    newMat.Translation = location + (reference.WorldMatrix.Down * 0.33);

                    //Logger.Instance.LogDebug(string.Format("1: {0}, {1}, {2}, {3}", Container.Entity.GetTopMostParent().WorldMatrix.M11, Container.Entity.GetTopMostParent().WorldMatrix.M12, Container.Entity.GetTopMostParent().WorldMatrix.M13, Container.Entity.GetTopMostParent().WorldMatrix.M14));
                    //Logger.Instance.LogDebug(string.Format("1: {0}, {1}, {2}, {3}", Container.Entity.GetTopMostParent().WorldMatrix.M21, Container.Entity.GetTopMostParent().WorldMatrix.M22, Container.Entity.GetTopMostParent().WorldMatrix.M23, Container.Entity.GetTopMostParent().WorldMatrix.M24));
                    //Logger.Instance.LogDebug(string.Format("1: {0}, {1}, {2}, {3}", Container.Entity.GetTopMostParent().WorldMatrix.M31, Container.Entity.GetTopMostParent().WorldMatrix.M32, Container.Entity.GetTopMostParent().WorldMatrix.M33, Container.Entity.GetTopMostParent().WorldMatrix.M34));
                    //Logger.Instance.LogDebug(string.Format("1: {0}, {1}, {2}, {3}\r\n", Container.Entity.GetTopMostParent().WorldMatrix.M41, Container.Entity.GetTopMostParent().WorldMatrix.M42, Container.Entity.GetTopMostParent().WorldMatrix.M43, Container.Entity.GetTopMostParent().WorldMatrix.M44));

                    #if !(VERSION_192 || VERSION_193)
                    beam.PositionComp.SetWorldMatrix(ref newMat);
                    #else
                    beam.PositionComp.WorldMatrix = newMat;
                    #endif
                    beam.PositionComp.SetPosition(newMat.Translation);

                    //Logger.Instance.LogDebug(string.Format("2: {0}, {1}, {2}, {3}", Container.Entity.GetTopMostParent().WorldMatrix.M11, Container.Entity.GetTopMostParent().WorldMatrix.M12, Container.Entity.GetTopMostParent().WorldMatrix.M13, Container.Entity.GetTopMostParent().WorldMatrix.M14));
                    //Logger.Instance.LogDebug(string.Format("2: {0}, {1}, {2}, {3}", Container.Entity.GetTopMostParent().WorldMatrix.M21, Container.Entity.GetTopMostParent().WorldMatrix.M22, Container.Entity.GetTopMostParent().WorldMatrix.M23, Container.Entity.GetTopMostParent().WorldMatrix.M24));
                    //Logger.Instance.LogDebug(string.Format("2: {0}, {1}, {2}, {3}", Container.Entity.GetTopMostParent().WorldMatrix.M31, Container.Entity.GetTopMostParent().WorldMatrix.M32, Container.Entity.GetTopMostParent().WorldMatrix.M33, Container.Entity.GetTopMostParent().WorldMatrix.M34));
                    //Logger.Instance.LogDebug(string.Format("2: {0}, {1}, {2}, {3}\r\n", Container.Entity.GetTopMostParent().WorldMatrix.M41, Container.Entity.GetTopMostParent().WorldMatrix.M42, Container.Entity.GetTopMostParent().WorldMatrix.M43, Container.Entity.GetTopMostParent().WorldMatrix.M44));
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                }
            }
        }

        public static IMyCubeBlock GetLargestOreDetector(IMyEntity drill)
        {
            IMyCubeBlock largest = null;
            MyObjectBuilder_OreDetector largestob = null;

            // If we have an ore detector, use it's sensor range
            List<IMySlimBlock> detectors = new List<IMySlimBlock>();
            (drill as IMyCubeBlock).CubeGrid.GetBlocks(detectors, x => x.FatBlock != null && x.FatBlock is IMyOreDetector);

            foreach (var detector in detectors)
            {
                var block = detector.FatBlock as IMyCubeBlock;
                var newob = block.GetObjectBuilderCubeBlock() as MyObjectBuilder_OreDetector;

                if (!((block as IMyFunctionalBlock).Enabled && block.IsFunctional))
                    continue;

                if (block.GetPlayerRelationToOwner() == MyRelationsBetweenPlayerAndBlock.Enemies)
                    continue;

                //if (!block.IsWorking)
                //    continue;
                
                Logger.Instance.LogDebug("Detector: " + block.ToString());

                if (largest == null || newob.DetectionRadius > largestob.DetectionRadius)
                {
                    largest = block;
                    largestob = newob;
                }
            }

            return largest;
        }

        public static void DisableDrill(IMyEntity drill)
        {
            ToggleDrill(drill, false);
        }

        public static void EnableDrill(IMyEntity drill)
        {
            ToggleDrill(drill, true);
        }

        private static void ToggleDrill(IMyEntity drill, bool on)
        {
            if (drill == null)
                return;

            var block = (drill as IMyCubeGrid)?.GetCubeBlock(new Vector3I(0, 0, 0))?.FatBlock as IMyFunctionalBlock;
            if (block != null && block.Enabled != on)
                (block as IMyShipDrill).GetActionWithName(on ? "OnOff_On" : "OnOff_Off").Apply((IMyCubeBlock)block);
        }

        public static void RemoveDrill(IMyEntity drill)
        {
            if (drill == null)
                return;

            if (drill.SyncObject != null)
                Sandbox.Game.Entities.MyEntities.SendCloseRequest(drill);
            else
                drill.Close();

            drill = null;

            Logger.Instance.LogMessage("Removed drill");
        }

        public static BoundingSphereD FindOreDetector(this IMyEntity drill, ref IMyCubeBlock cachedDetector, Action<IMyEntity> closeAction)
        {
            BoundingSphereD detectorRange = new BoundingSphereD();
            var detector = GetLargestOreDetector(drill);
            Logger.Instance.LogDebug("Detector: " + (detector != null ? detector.ToString() : "<null>"));

            if (detector == null && cachedDetector != null)
            {
                cachedDetector.OnClose -= closeAction;
                cachedDetector = null;
            }

            if (detector != null && (cachedDetector == null || (cachedDetector != detector)))
            {
                Logger.Instance.LogMessage("Updating ore detector");
                if (cachedDetector != null)
                    cachedDetector.OnClose -= closeAction;

                cachedDetector = detector;
                cachedDetector.OnClose += closeAction;
                //var ob = cachedDetector.GetObjectBuilderCubeBlock() as MyObjectBuilder_OreDetector;

                detectorRange = new BoundingSphereD(cachedDetector.PositionComp.GetPosition(),
                    (cachedDetector as IMyTerminalBlock).GetProperty("Range").Cast<float>().GetValue(cachedDetector));

                //if (cachedDetector.CubeGrid.Physics == null || cachedDetector.CubeGrid.Physics.LinearVelocity == Vector3D.Zero)
                //    cachedDetector.GameLogic.GetAs<OreDetectorGameLogic>().ScanOre();
            }
            return detectorRange;
        }

        public static void ConnectToTurret(this IMyEntity drill, IMyCubeBlock turret)
        {
            drill.GameLogic.GetAs<LaserDrill>().ToggleTurretConnection(turret);
        }

        public static void ClampVoxelCoord(this VRage.ModAPI.IMyStorage self, ref Vector3I voxelCoord, int distance = 1)
        {
            if (self == null) return;
            var sizeMinusOne = self.Size - distance;
            Vector3I.Clamp(ref voxelCoord, ref Vector3I.Zero, ref sizeMinusOne, out voxelCoord);
        }

        public static bool TransferItemsFrom(this Sandbox.Game.MyInventory destinationInventory, IMyInventory sourceInventory, VRage.Game.ModAPI.Ingame.IMyInventoryItem item, MyFixedPoint amount)
        {
            if (sourceInventory == null || item == null)
                return false;
            if (amount == (MyFixedPoint)0)
                return true;

            // No ModAPI equivalent for this method afaik
            //if (destinationInventory.CanTransferFromTo(sourceInventory, item))
            {
                var src = sourceInventory as Sandbox.Game.MyInventory;
                if (src != null)
                {
                    Sandbox.Game.MyInventory.Transfer(src, destinationInventory, item.ItemId, -1, new MyFixedPoint?(amount), false);
                    return true;
                }
            }
            return false;
        }
    }
}

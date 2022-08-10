#line 2
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
using Sandbox.Game.Gui;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using Sandbox.Game;
using Sandbox.Game.Entities;
using VRage.Game;
using Sandbox.Game.EntityComponents;
using VRage.Utils;
using Sandbox.Engine.Utils;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI.Interfaces.Terminal;
using SpaceEngineers.Game.ModAPI;
using VRage.Voxels;
using Sandbox.Game.Localization;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using Sandbox.Common.ObjectBuilders.Definitions;
using System.IO;

namespace Phoenix.LaserDrill
{
    class Globals
    {
        public static bool Debug = false;
    }

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    class MissionComponent : MySessionComponentBase
    {
        //private OreDetector m_oreDetector = OreDetector.Instance;
        //public OreDetector OreDetector { get { return m_oreDetector; } }



        long m_counter = 0;
        HashSet<IMyGps> m_hudPoints = new HashSet<IMyGps>();

        bool m_isBuildable = true;
        static MyDefinitionId m_turretDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_LargeGatlingTurret), "Large_SC_LaserDrillTurret");
        static MyCubeBlockDefinition m_turrentDefinition = (MyCubeBlockDefinition)MyDefinitionManager.Static.GetDefinition(m_turretDefinitionId);
        static MyCubeBlockDefinition.MountPoint[] m_turrentMountPoints = (MyCubeBlockDefinition.MountPoint[])m_turrentDefinition.MountPoints.Clone();

        public const string PLACEMENT_ERROR_MESSAGE = "Beam Drill turret must be placed on Beam Drill base";

        public override void LoadData()
        {
            Logger.Instance.Init("LaserDrill");
            Logger.Instance.Debug = Globals.Debug;
        }
        protected override void UnloadData()
        {
            Logger.Instance.Close();
        }

        public override void SaveData()
        {
            // Don't save GPS points on save
            foreach (var point in m_hudPoints)
                MyAPIGateway.Session.GPS.RemoveLocalGps(point);

            m_hudPoints.Clear();
            BeamDrillSettings.Instance.SaveAllTerminalValues();
        }

        public bool Buildable
        {
            get
            {
                return m_isBuildable;
            }
            set
            {
                if (m_isBuildable != value)
                {
                    m_isBuildable = value;
                    MyAPIGateway.Utilities.ShowNotification($"Changing state: {value}", 2000);

                    if (!MyAPIGateway.Session.IsServer)
                    {
                        // Restore mount points
                        if (value)
                            m_turrentDefinition.MountPoints = (MyCubeBlockDefinition.MountPoint[])m_turrentMountPoints.Clone();
                        // Remove mount points so the block can't be placed
                        else
                            m_turrentDefinition.MountPoints = new MyCubeBlockDefinition.MountPoint[0];
                    }
                }
            }
        }

        bool m_init = false;
        public override void UpdateBeforeSimulation()
        {
            if (!m_init)
            {
                m_init = true;


                //if (!MyAPIGateway.Multiplayer.IsServer)
                //    MessageUtils.SendMessageToServer(new MessageClientConnected());
            }

            if (m_counter++ % 120 == 0)
            {
                OreDetector.Instance.UpdateVoxelCache();
            }

            // This section should last in here
            if (MyAPIGateway.CubeBuilder.IsActivated)
            {
                var builder = MyAPIGateway.CubeBuilder as MyCubeBuilder;
                try
                {
                    if (builder.ToolbarBlockDefinition?.Id.SubtypeName == "Large_SC_LaserDrillTurret")
                    {
                        //builder.UpdateNotificationBlockNotAvailable(true);
                        var grid = builder.FindClosestGrid() as IMyCubeGrid;
                        var hit = (builder.HitInfo as IHitInfo);
                        var entity = hit?.HitEntity;

                        if (grid != null)
                        {
                            var block_pos = grid.RayCastBlocks(MyCubeBuilder.IntersectionStart, builder.FreePlacementTarget);
                            if (block_pos != null)
                            {
                                var block = grid.GetCubeBlock(block_pos.Value);
                                if (block?.FatBlock?.BlockDefinition.SubtypeId == "Large_SC_LaserDrill")
                                {
                                    // The block being aimed at is the base drill
                                    Buildable = true;
                                    return;
                                }
                            }
                        }
                        MyAPIGateway.Utilities.ShowNotification(PLACEMENT_ERROR_MESSAGE, 16, MyFontEnum.Red);
                        Buildable = false;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                    MyAPIGateway.Utilities.ShowMessage("LaserDrill", ex.ToString());
                }
            }
            Buildable = true;
        }
    }

    // The only purpose of this class is to deactive the physics on the new drill entity
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), false, new string[] { "Large_SC_LaserDrill_HiddenStatic", "Large_SC_LaserDrill_HiddenTurret" })]
    public class HiddenDrill : MyGameLogicComponent
    {
        private bool m_init = false;
        public bool CreatedByMod { get; set; }
        HashSet<IMySensorBlock> m_disabledSensors = new HashSet<IMySensorBlock>();

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Container.Entity.GetObjectBuilder(copy);
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            Entity.OnClose += Entity_OnClose;
            Logger.Instance.LogDebug("Created hidden drill");
        }

        private void Entity_OnClose(IMyEntity obj)
        {
            Entity.OnClose -= Entity_OnClose;
            NeedsUpdate = MyEntityUpdateEnum.NONE;
        }

        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                if (!CreatedByMod)
                    this.Close();

                if ((Container.Entity as IMyShipDrill).CustomName == Constants.DrillCustomName)
                {
                    if (Container.Entity.GetTopMostParent().Physics != null)
                    {
                        Container.Entity.GetTopMostParent().Physics.Deactivate();
                    }

                    if (!Globals.Debug)
                        (Container.Entity as IMyCubeBlock).CubeGrid.Visible = false;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public override void UpdateBeforeSimulation10()
        {
            if (!m_init)
            {
                m_init = true;
            }

            // Collect floating ore
            var ores = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(ores, (x) => x is IMyFloatingObject && (Container.Entity as IMyCubeBlock).CubeGrid.WorldVolume.Contains(x.GetPosition()) != ContainmentType.Disjoint);
            var drillEntity = Container.Entity as MyEntity;

            var invOwn = (drillEntity != null && drillEntity.HasInventory) ? Container.Entity as MyEntity : null;

            foreach (var ore in ores)
            {
                if (invOwn != null)
                    (invOwn.GetInventoryBase(0) as MyInventory).TakeFloatingObject(ore as MyFloatingObject);
            }

            var sphere = new BoundingSphereD(Container.Entity.PositionComp.GetPosition(), 200);
            var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
            foreach (var entity in entities)
            {
                if (entity is IMySensorBlock && (entity as IMySensorBlock).IsWorking && (entity as IMySensorBlock).DetectStations)
                {
                    if (Container.Entity.SyncObject != null)
                        MyEntities.SendCloseRequest(Container.Entity);
                    else
                        Container.Entity.Close();
                }
            }
        }
    }

    public enum DrillingMode
    {
        /// <summary>
        /// Drill is not active
        /// </summary>
        Inactive,
        /// <summary>
        /// Drill does not have an automatic or user controlled refpos. Drills straight ahead
        /// </summary>
        Static,
        /// <summary>
        /// Automatic ore mining with ore detector
        /// </summary>
        Automatic,
        /// <summary>
        /// User controlled via turret access
        /// </summary>
        User,
    }

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Drill), true, new string[] { "Large_SC_LaserDrill" })]
    public class LaserDrill : MyGameLogicComponent, ILaserDrill
    {
        public static Configuration Config = new Configuration();

        private IMyEntity m_drill;                      // This drill
        private IMyEntity m_spawnedDrill;               // The drill that will do the work
        private IMyCubeBlock m_turret;
        private Vector3D m_target = Vector3D.Zero;
        private Vector3D m_target1 = Vector3D.Zero;
        public bool Enabled { get; set; }
        private MyParticleEffect m_particleEffect;
        private PacketManager m_currentPacketManager;
        private DateTime m_queuedTime = DateTime.Now;
        private bool m_shutDown = false;
        private List<PacketManager> m_previousPacketManagers = new List<PacketManager>();

        // For voxel pre-caching
        private static readonly MyTimedItemCache m_prefetchedVoxelRaysTimedCache = new MyTimedItemCache(4000);
        private const double m_prefetchedVoxelRaysSourceMapping = 1.0f / 2.0f;
        private const double m_prefetchedVoxelRaysDirectionMapping = 50.0f;
        private static List<MyLineSegmentOverlapResult<MyEntity>> m_entityRaycastResult = null;
        private static List<IHitInfo> m_raycastResult = new List<IHitInfo>(16);

        #region Config file
        private bool m_Init = false;
        const string m_configName = "LaserDrillSetup.cfg";

        internal void LoadConfigFile()
        {
            if (MyAPIGateway.Utilities.FileExistsInLocalStorage(m_configName, typeof(LaserDrill)))
            {
                if (MyAPIGateway.Multiplayer.IsServer || !MyAPIGateway.Multiplayer.MultiplayerActive)
                {
                    Configuration config;
                    string buffer;

                    using (TextReader file = MyAPIGateway.Utilities.ReadFileInLocalStorage(m_configName, typeof(LaserDrill)))
                    {
                        buffer = file.ReadToEnd();
                    }

                    try
                    {
                        config = MyAPIGateway.Utilities.SerializeFromXML<Configuration>(buffer);

                        if (config != null)
                        {
                            LaserDrill.Config = config;

                            Logger.Instance.Debug = Config.Others.LogDebugEnabled;
                        }

                    }
                    catch (InvalidOperationException e)
                    {
                        using (TextWriter outputFile = MyAPIGateway.Utilities.WriteFileInLocalStorage(m_configName, typeof(LaserDrill)))
                        {
                            GenerateNewConfigFile();
                        }
                    }
                }
            }
            else
            {
                GenerateNewConfigFile();
            }
        }

        internal void GenerateNewConfigFile()
        {
            using (TextWriter outputFile = MyAPIGateway.Utilities.WriteFileInLocalStorage(m_configName, typeof(LaserDrill)))
            {
                outputFile.Write(MyAPIGateway.Utilities.SerializeToXML<Configuration>(new Configuration()));
            }
        }
        #endregion

        #region Operational settings
        private static float _maxRange = 800;
        public float MaxRange
        {
            get { return (m_turret != null ? Config.Others.LaserDrillRange : _maxRange); }
            set { Config.Others.LaserDrillRange = value; (m_drill as IMyShipDrill).StoreTerminalValues(); }
        }

        public float TurretRange
        {
            get
            {
                return (m_turret != null ? (m_turret as IMyTerminalBlock).GetValueFloat("Range") : Config.Others.TurretRange);
            }
        }

        float MaxRequiredPower { get { return (float)Math.Pow(MaxRange * MaxRange, Config.Compsumption.PowerFactor); } }

        private Color m_primaryColor = Color.MediumBlue;
        private Color m_secondaryColor = Color.LemonChiffon;
        public Color PrimaryBeamColor
        {
            get { return m_primaryColor; }
            set
            {
                m_primaryColor = value;
                //(m_drill as IMyShipDrill).StoreTerminalValues();
            }
        }

        public Color SecondaryBeamColor
        {
            get { return m_secondaryColor; }
            set
            {
                m_secondaryColor = value;
                //(m_drill as IMyShipDrill).StoreTerminalValues();
            }
        }
        #endregion

        #region Sound
        private MyEntity3DSoundEmitter m_soundEmitter;
        public MyEntity3DSoundEmitter SoundEmitter { get { return m_soundEmitter; } }
        #endregion

        // Accessed via messaging
        private DrillingMode m_drillingMode = DrillingMode.Inactive;
        public DrillingMode DrillingMode
        {
            get { return m_drillingMode; }
            set
            {
                if (m_drillingMode != value &&
                    MyAPIGateway.Session != null && MyAPIGateway.Multiplayer != null &&
                    MyAPIGateway.Session.Player == null && MyAPIGateway.Multiplayer.IsServer)
                {
                    MessageUtils.SendMessageToAllPlayers(new MessageMiningState() { EntityId = m_drill.EntityId, DrillingMode = value, Ore = m_targetOre });
                }

                m_drillingMode = value;
            }
        }

        private string m_targetOre = string.Empty;

        /// <summary>
        /// Cached data to speed up regular updates
        /// </summary>
        #region Cached Data
        private IMyCubeBlock m_cachedDetector = null;   // Cached detector
        private BoundingSphereD m_cachedDetectorRange;
        private MyInventory m_cachedSrcInventory = null;
        private MyInventory m_cachedDstInventory = null;
        private MyResourceSinkComponent m_sink = null;
        private MyDefinitionId m_powerDefinitionId = new VRage.Game.MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");
        #endregion

        #region Terminal Controls
        public bool CollectStone
        {
            get { return Config.Others.CollectStone; }
            set
            {
                Logger.Instance.LogDebug("CollectStone: " + value);
                Config.Others.CollectStone = value;
                (m_drill as IMyShipDrill).StoreTerminalValues();
            }
        }

        static bool m_ControlsInited = false;

        private void CreateTerminalControls()
        {
            if (m_ControlsInited)
                return;

            m_ControlsInited = true;
            Func<IMyTerminalBlock, bool> enabledCheck = delegate (IMyTerminalBlock b) { return b.BlockDefinition.SubtypeId == "Large_SC_LaserDrill"; };

            MyAPIGateway.TerminalControls.CustomControlGetter -= TerminalControls_CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter -= TerminalControls_CustomActionGetter;

            MyAPIGateway.TerminalControls.CustomControlGetter += TerminalControls_CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter += TerminalControls_CustomActionGetter;

            // Separator
            var sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, Sandbox.ModAPI.Ingame.IMyShipDrill>(string.Empty);
            sep.Visible = enabledCheck;
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyShipDrill>(sep);

            // Collect stone button
            var collectStone = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlOnOffSwitch, Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.CollectStone");
            collectStone.Getter = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrill>().CollectStone;
            collectStone.Setter = (b, v) => { if (enabledCheck(b)) MessageUtils.SendMessageToAll(new MessageToggleCollectStone() { EntityId = b.EntityId, CollectStone = v }); };
            collectStone.Visible = enabledCheck;
            collectStone.Enabled = enabledCheck;
            collectStone.Title = MyStringId.GetOrCompute("Collect Stone");
            collectStone.OnText = MyStringId.GetOrCompute("On");
            collectStone.OffText = MyStringId.GetOrCompute("Off");
            collectStone.Tooltip = MyStringId.GetOrCompute("Toggles the collection or destruction of stone while mining.");
            collectStone.SupportsMultipleBlocks = true;

            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyShipDrill>(collectStone);

            // Add actions
            // Toggle
            StringBuilder actionname = new StringBuilder();
            actionname.Append(collectStone.Title).Append(" ").Append(collectStone.OnText).Append("/").Append(collectStone.OffText);

            var collectStoneAction = MyAPIGateway.TerminalControls.CreateAction<Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.CollectStone");
            collectStoneAction.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
            collectStoneAction.Name = actionname;
            collectStoneAction.Action = (b) => collectStone.Setter(b, !collectStone.Getter(b));
            collectStoneAction.Writer = (b, t) => t.Append(b.GameLogic.GetAs<LaserDrill>().CollectStone ? collectStone.OnText : collectStone.OffText);
            collectStoneAction.Enabled = enabledCheck;
            MyAPIGateway.TerminalControls.AddAction<Sandbox.ModAPI.Ingame.IMyShipDrill>(collectStoneAction);

            // On
            actionname = new StringBuilder();
            actionname.Append(collectStone.Title).Append(" ").Append(collectStone.OnText);

            collectStoneAction = MyAPIGateway.TerminalControls.CreateAction<Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.CollectStone_On");
            collectStoneAction.Icon = @"Textures\GUI\Icons\Actions\SwitchOn.dds";
            collectStoneAction.Name = actionname;
            collectStoneAction.Action = (b) => collectStone.Setter(b, true);
            collectStoneAction.Writer = (b, t) => t.Append(b.GameLogic.GetAs<LaserDrill>().CollectStone ? collectStone.OnText : collectStone.OffText);
            collectStoneAction.Enabled = enabledCheck;
            MyAPIGateway.TerminalControls.AddAction<Sandbox.ModAPI.Ingame.IMyShipDrill>(collectStoneAction);

            // Off
            actionname = new StringBuilder();
            actionname.Append(collectStone.Title).Append(" ").Append(collectStone.OffText);

            collectStoneAction = MyAPIGateway.TerminalControls.CreateAction<Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.CollectStone_Off");
            collectStoneAction.Icon = @"Textures\GUI\Icons\Actions\SwitchOff.dds";
            collectStoneAction.Name = actionname;
            collectStoneAction.Action = (b) => collectStone.Setter(b, true);
            collectStoneAction.Writer = (b, t) => t.Append(b.GameLogic.GetAs<LaserDrill>().CollectStone ? collectStone.OnText : collectStone.OffText);
            collectStoneAction.Enabled = enabledCheck;
            MyAPIGateway.TerminalControls.AddAction<Sandbox.ModAPI.Ingame.IMyShipDrill>(collectStoneAction);

            // Range slider
            var rangeSlider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.Range");
            rangeSlider.Enabled = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrill>().m_turret == null;
            rangeSlider.Visible = enabledCheck;
            rangeSlider.Title = MyStringId.GetOrCompute("BlockPropertyTitle_OreDetectorRange");
            rangeSlider.SetLimits(1, _maxRange);
            rangeSlider.Getter = (b) => enabledCheck(b) ? b.GameLogic.GetAs<LaserDrill>().TurretRange : 0;
            rangeSlider.Setter = (b, v) =>
                {
                    if (!enabledCheck(b)) return;
                    MessageUtils.SendMessageToAll(new MessageSetStaticRange() { EntityId = b.EntityId, Range = v });
                    rangeSlider.UpdateVisual();
                };
            rangeSlider.Writer = (b, t) => t.AppendFormat("{0:D}", (int)rangeSlider.Getter(b)).Append(" m");
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyShipDrill>(rangeSlider);

            // Colors
            // Separator
            sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, Sandbox.ModAPI.Ingame.IMyShipDrill>(string.Empty);
            sep.Visible = enabledCheck;
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyShipDrill>(sep);

            var label = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlLabel, Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.ColorLabel");
            var defaultButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.ResetColors");
            var mainColor = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.PrimaryBeamColor");
            var auxColor = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlColor, Sandbox.ModAPI.Ingame.IMyShipDrill>("Phoenix.BD.SecondaryBeamColor");

            // Beam color label
            label.Label = MyStringId.GetOrCompute("Beam Colors");
            label.Visible = enabledCheck;
            label.Enabled = enabledCheck;
            label.SupportsMultipleBlocks = true;
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyShipDrill>(label);

            // Default colors
            defaultButton.Title = MyStringId.GetOrCompute("ToolbarAction_Reset");
            defaultButton.Tooltip = MyStringId.GetOrCompute("Resets the beam colors to the defaults.");
            defaultButton.Enabled = enabledCheck;
            defaultButton.Visible = enabledCheck;
            defaultButton.SupportsMultipleBlocks = true;
            defaultButton.Action = (b) =>
                {
                    MessageUtils.SendMessageToAll(new MessageSetBeamColors() { EntityId = b.EntityId, Primary = Color.MediumBlue, Secondary = Color.LemonChiffon });
                    mainColor.UpdateVisual();
                    auxColor.UpdateVisual();
                };
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyShipDrill>(defaultButton);

            // Add color sliders
            mainColor.Title = MyStringId.GetOrCompute("Primary");
            mainColor.Enabled = enabledCheck;
            mainColor.Visible = enabledCheck;
            mainColor.SupportsMultipleBlocks = true;
            mainColor.Setter = (b, v) =>
                {
                    if (!enabledCheck(b)) return;
                    MessageUtils.SendMessageToAll(new MessageSetBeamColors() { EntityId = b.EntityId, Primary = v, Secondary = b.GameLogic.GetAs<LaserDrill>().SecondaryBeamColor });
                    //mainColor.UpdateVisual();
                };
            mainColor.Getter = (b) => enabledCheck(b) ? b.GameLogic.GetAs<LaserDrill>().PrimaryBeamColor : Color.Black;
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyShipDrill>(mainColor);

            // Add color sliders
            auxColor.Title = MyStringId.GetOrCompute("Secondary");
            auxColor.Enabled = enabledCheck;
            auxColor.Visible = enabledCheck;
            auxColor.SupportsMultipleBlocks = true;
            auxColor.Setter = (b, v) =>
            {
                if (!enabledCheck(b)) return;
                MessageUtils.SendMessageToAll(new MessageSetBeamColors() { EntityId = b.EntityId, Primary = b.GameLogic.GetAs<LaserDrill>().PrimaryBeamColor, Secondary = v });
                //mainColor.UpdateVisual();
            };
            auxColor.Getter = (b) => enabledCheck(b) ? b.GameLogic.GetAs<LaserDrill>().SecondaryBeamColor : Color.Black;
            MyAPIGateway.TerminalControls.AddControl<Sandbox.ModAPI.Ingame.IMyShipDrill>(auxColor);
        }

        static void TerminalControls_CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (block is IMyShipDrill)
            {
                string subtype = (block as IMyShipDrill).BlockDefinition.SubtypeId;
                var itemsToRemove = new List<IMyTerminalAction>();

                foreach (var action in actions)
                {
                    //Logger.Instance.LogMessage("Action: " + action.Id);
                    switch (subtype)
                    {
                        case "Large_SC_LaserDrill":
                            if (
                                action.Id.StartsWith("OnOff") ||
                                action.Id.StartsWith("ShowInTerminal") ||
                                action.Id.StartsWith("ShowInToolbarConfig") ||
                                action.Id.StartsWith("ShowInInventory") ||
                                action.Id.StartsWith("ShowOnHUD") ||
                                action.Id.StartsWith("UseConveyor") ||
                                action.Id.StartsWith("Phoenix.BD")
                                )
                                break;
                            else
                                itemsToRemove.Add(action);
                            break;
                    }
                }

                foreach (var action in itemsToRemove)
                {
                    actions.Remove(action);
                }
            }
        }

        static void TerminalControls_CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
        {
            if (block is IMyShipDrill)
            {
                string subtype = (block as IMyShipDrill).BlockDefinition.SubtypeId;
                var itemsToRemove = new List<IMyTerminalControl>();
                int separatorsToKeep = 3;

                foreach (var control in controls)
                {
                    //Logger.Instance.LogMessage("Control: " + control.Id);
                    switch (subtype)
                    {
                        case "Large_SC_LaserDrill":
                            switch (control.Id)
                            {
                                case "OnOff":
                                case "ShowInTerminal":
                                case "ShowInToolbarConfig":
                                case "ShowInInventory":
                                case "Name":
                                case "ShowOnHUD":
                                case "UseConveyor":
                                    break;
                                default:
                                    if (control.Id.StartsWith("Phoenix.BD"))
                                        break;
                                    else if (control is IMyTerminalControlLabel)
                                        break;
                                    else if (control is IMyTerminalControlSeparator && separatorsToKeep-- >= 0)
                                        break;
                                    itemsToRemove.Add(control);
                                    break;
                            }
                            break;
                    }
                }

                foreach (var action in itemsToRemove)
                {
                    controls.Remove(action);
                }
            }
        }

        /// <summary>
        /// For legacy purposes only
        /// </summary>
        public void LoadTerminalValues()
        {
            // New way
            var staticsettings = (m_drill as IMyShipDrill).RetrieveTerminalValues();
            if (staticsettings != null)
            {
                Config.Others.CollectStone = staticsettings.CollectStone;
                Config.Others.TurretRange = staticsettings.Range;
                m_primaryColor = staticsettings.PrimaryColor;
                m_secondaryColor = staticsettings.SecondaryColor;
            }

            try
            {
                string strdata;
                MyAPIGateway.Utilities.GetVariable<string>(m_drill.EntityId.ToString(), out strdata);
                if (!string.IsNullOrEmpty(strdata))
                {
                    // Old way, for legacy. If found, delete it and store it in the new way
                    MyAPIGateway.Utilities.SetVariable<string>(m_drill.EntityId.ToString(), null);
                    Logger.Instance.LogDebug("Reading settings");
                    var settings = MyAPIGateway.Utilities.SerializeFromXML<DrillSettings>(strdata);
                    CollectStone = settings.CollectStone;
                }
            }
            catch { }
        }
        #endregion

        private ulong m_counter = 0;

        public override void Init(VRage.ObjectBuilders.MyObjectBuilder_EntityBase objectBuilder)
        {
            if (!m_Init)
            {
                LoadConfigFile();
            }

            m_drill = Container.Entity;
            m_shutdownTimer.Elapsed += (s, e) =>
            {
                MyAPIGateway.Utilities.InvokeOnGameThread(() =>
                {
                    // Only shut off the drill if we don't have power for one second,
                    // as things might change as new ore is located.
                    if (!m_drill.MarkedForClose && !m_drill.Closed && (m_drill as IMyFunctionalBlock).Enabled && m_sink.SuppliedRatioByType(m_powerDefinitionId) < 1.0)
                    {
                        m_shutDown = true;
                    }
                });
            };
            m_shutdownTimer.AutoReset = false;

            // Set up power requirements
            (m_drill as IMyShipDrill).Components.TryGet<MyResourceSinkComponent>(out m_sink);
            //m_sink.RemoveType(ref powerDefinitionId);
            //var newtype = new MyResourceSinkInfo() { MaxRequiredInput = m_MaxRequiredPower, RequiredInputFunc = ComputeRequiredPower, ResourceTypeId = powerDefinitionId };
            //m_sink.AddType(ref newtype);
            //m_sink.Init(MyStringHash.GetOrCompute("Defense"), m_MaxRequiredPower, ComputeRequiredPower);
            //m_sink.SetRequiredInputFuncByType(powerDefinitionId, ComputeRequiredPower);
            //m_sink.SetMaxRequiredInputByType(powerDefinitionId, m_MaxRequiredPower);
            m_sink.SetMaxRequiredInputByType(m_powerDefinitionId, MaxRequiredPower);

            //m_sink.SetRequiredInputByType(m_powerDefinitionId, ComputeRequiredPower());
            m_sink.Update();

            //base.Init(objectBuilder);

            m_targetOre = Constants.StatusNone;
            m_drill.OnClosing += m_drill_OnClosing;
            (m_drill as IMyFunctionalBlock).EnabledChanged += LaserDrill_EnabledChanged;
            (m_drill as IMyCubeBlock).IsWorkingChanged += LaserDrill_IsWorkingChanged;
            (m_drill as IMyCubeBlock).CubeGrid.OnBlockRemoved += CubeGrid_OnBlockRemoved;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            m_soundEmitter = new MyEntity3DSoundEmitter(Container.Entity as VRage.Game.Entity.MyEntity);

            LoadTerminalValues();
        }

        void CubeGrid_OnBlockRemoved(IMySlimBlock obj)
        {
            if (m_turret != null)
            {
                //var slim = m_turret.CubeGrid.GetCubeBlock(m_turret.Position);
                if (obj.FatBlock != null && obj.FatBlock == m_turret)
                {
                    //MyAPIGateway.Utilities.ShowNotification("Removing turret");
                    m_turret = null;
                    RemoveDrill();
                    //(m_drill as IMyFunctionalBlock).EnabledChanged += LaserDrill_EnabledChanged;
                }
            }
        }

        System.Timers.Timer m_shutdownTimer = new System.Timers.Timer(1000);
        void LaserDrill_IsWorkingChanged(IMyCubeBlock obj)
        {
            Logger.Instance.LogDebug("LaserDrill_IsWorkingChanged");
            m_sink.Update();
            m_shutDown = false;

            if ((m_drill as IMyFunctionalBlock).Enabled && m_sink.SuppliedRatioByType(m_powerDefinitionId) < 1.0)
                m_shutdownTimer.Start();

            SetDrillingMode();
            if (!obj.IsWorking)
            {
                PlaySound(null, true);
                RemoveDrill();
                m_target1 = Vector3D.Zero;
                StopEffects();
                m_previousPacketManagers?.Clear();
            }
            else
            {
                PlaySound("SC_SmallGatling_FireCode", true);
            }
            SetEmissives();
            (m_drill as IMyTerminalBlock).RefreshCustomInfo();
        }

        private void SetDrillingMode()
        {
            DrillingMode mode = Phoenix.LaserDrill.DrillingMode.Inactive;

            if ((m_drill as IMyTerminalBlock).IsWorking)
            {
                mode = DrillingMode.Static;
                if (m_turret != null)
                {
                    if ((m_turret as IMyLargeGatlingTurret).IsUnderControl)
                        mode = DrillingMode.User;
                    else if (m_cachedDetector != null)
                        mode = DrillingMode.Automatic;
                    else if (!m_turret.IsFunctional)
                        mode = Phoenix.LaserDrill.DrillingMode.Inactive;
                }
            }
            else
            {
                m_targetOre = Constants.StatusNone;
            }
            DrillingMode = mode;
        }

        public void SetEmissives()
        {
            if (MyAPIGateway.Session?.Player == null)
                return;

            var offColor = Color.Red;
            var onColor = Color.Green;
            var activeColor = Color.Blue;
            var emissiveName = "Emissive";
            var emissivity = 1.0f;

            if ((m_drill as IMyTerminalBlock).IsWorking)
            {
                m_drill.SetEmissiveParts(emissiveName, onColor, emissivity);
                m_turret?.SetEmissiveParts(emissiveName, onColor, emissivity);
                m_turret?.SetEmissivePartsForSubparts(emissiveName, onColor, emissivity);
            }
            else
            {
                m_drill.SetEmissiveParts(emissiveName, offColor, emissivity);
                m_turret?.SetEmissiveParts(emissiveName, offColor, emissivity);
                m_turret?.SetEmissivePartsForSubparts(emissiveName, offColor, emissivity);
            }

            // Set the emissives of the turret emitters to the beam colors selected in the terminal
            if (m_turret != null && m_turret.IsFunctional && (m_turret as MyEntity).Subparts?.ContainsKey("GatlingTurretBase1") == true)
            {
                var turretsub = (m_turret as MyEntity).Subparts["GatlingTurretBase1"]?.Subparts["GatlingTurretBase2"];
                var barrel = turretsub?.Subparts["GatlingBarrel"];
                turretsub?.SetEmissiveParts(emissiveName, PrimaryBeamColor, emissivity);
                barrel?.SetEmissiveParts(emissiveName, SecondaryBeamColor, emissivity);
            }
        }

        public static void AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            try
            {
                StringBuilder text = new StringBuilder(100);
                var drill = (arg1 as IMyTerminalBlock).GameLogic.GetAs<LaserDrill>();

                drill.SetDrillingMode();

                text.Append(MyTexts.GetString(MySpaceTexts.BlockPropertiesText_RequiredInput));
                MyValueFormatter.AppendWorkInBestUnit(drill.ComputeRequiredPower(), text);
                text.AppendLine();
                text.AppendFormat("Drilling Mode: {0}\r\n", drill.DrillingMode);
                if (drill.DrillingMode != DrillingMode.Inactive)
                {
                    text.AppendFormat("Mining Target: {0}\r\n", drill.m_targetOre);
                    text.AppendFormat("Currently Mining: {0}\r\n",
                        drill.DrillingMode != DrillingMode.Inactive ?
                        OreDetector.Instance.GetSingleMaterialAtPoint(drill.m_target)?.MinedOre ?? Constants.StatusNone :
                        Constants.StatusNone);
                    if (drill.m_target != Vector3D.Zero)
                        text.AppendFormat("Distance to Target: {0:F0} m\r\n", (arg1.PositionComp.GetPosition() - drill.m_target).Length());
                }
                //if ((arg1 as IMyTerminalBlock).CubeGrid.IsStatic && !Globals.AllowOnStation)
                //    text.Append("Drill cannot be used on stations." + Environment.NewLine +
                //                "Keen disables planet physics when there are no dynamic grids around.");
                //if (drill.GetNearestMineableOre(false) == Vector3D.Zero)
                //    text.Append("No ore found, automatic mining disabled.\r\n");

                arg2.Append(text);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        // When the turret is attached, this method will not be called
        void LaserDrill_EnabledChanged(IMyTerminalBlock obj)
        {
            if (obj == null || obj.Closed)
                return;

            Logger.Instance.LogDebug("LaserDrill_EnabledChanged");
            m_isStopped = false;
            m_targetOre = Constants.StatusNone;
            (Container.Entity as IMyTerminalBlock).RefreshCustomInfo();

            Enabled = obj.IsWorking;
            m_target = Vector3D.Zero;
            m_lastOreInPath = Vector3D.Zero;

            if (MyAPIGateway.Multiplayer == null)
                return;

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                try
                {
                    //m_sink.MaxRequiredInput = ComputeRequiredPower();
                    TransferInventory();

                    //Enabled = (m_drill as IMyShipDrill).Enabled;

                    //if (obj.IsWorking)
                    //    EnableDrill();
                    //else
                    //    DisableDrill();
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                }
            }
        }

        void m_drill_OnClosing(IMyEntity obj)
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
            m_drill.OnClosing -= m_drill_OnClosing;
            (m_drill as IMyFunctionalBlock).EnabledChanged -= LaserDrill_EnabledChanged;
            (m_drill as IMyTerminalBlock).AppendingCustomInfo -= AppendingCustomInfo;
            (m_drill as IMyCubeBlock).IsWorkingChanged -= LaserDrill_IsWorkingChanged;
            (m_drill as IMyCubeBlock).CubeGrid.OnBlockRemoved -= CubeGrid_OnBlockRemoved;
            (obj as IMyShipDrill).DeleteTerminalValues();
            m_shutdownTimer?.Close();
            m_shutdownTimer = null;

            RemoveDrill();
        }

        public override VRage.ObjectBuilders.MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Container.Entity.GetObjectBuilder(copy);
        }

        public void PlaySound(string soundname, bool stopPrevious = false)
        {
            MyEntity3DSoundEmitter emitter = null;

            emitter = m_drill.GameLogic.GetAs<LaserDrill>().SoundEmitter;

            if (emitter != null)
            {
                if (string.IsNullOrEmpty(soundname))
                {
                    emitter.StopSound(false);
                }
                else
                {
                    MySoundPair sound = new MySoundPair(soundname);
                    //emitter.CustomMaxDistance = (float)m_drill.GetTopMostParent().PositionComp.WorldVolume.Radius * 2.0f;
                    //Logger.Instance.LogDebug("Distance: " + emitter.CustomMaxDistance);
                    emitter.CustomVolume = 1.0f;
                    emitter.PlaySound(sound, stopPrevious);
                }
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Session == null || m_turret == null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                return;
            }
            LoadTerminalValues();
            CreateTerminalControls();
            SetEmissives();
            // Set up custom into
            (m_drill as IMyTerminalBlock).AppendingCustomInfo += AppendingCustomInfo;
            (m_drill as IMyTerminalBlock).RefreshCustomInfo();
            FindOreDetector();
            m_nearestOre = GetNearestMineableOre(TurretRange);                             // Get ore to mine
        }

        public override void UpdateBeforeSimulation()
        {
            if (!m_Init)
            {
                LoadConfigFile();
            }

            Profiler.Begin("LaserDrill.UpdateBeforeSimulation");
            m_counter++;

            if (!(m_drill as IMyShipDrill).Enabled)
                m_target = Vector3D.Zero;

            //if ((m_drill as IMyCubeBlock).CubeGrid.IsStatic && !Globals.AllowOnStation)
            //{
            //    MessageUtils.ShowMessageToUsersInRange(m_drill as IMyFunctionalBlock, "Beam Drill cannot be used on a station.", 5000, true);
            //    (m_drill as IMyFunctionalBlock).Enabled = false;
            //}

            if (!Globals.Debug)
                MyAPIGateway.Utilities.ShowNotification("Target: " + m_target.ToString(), 50);
            var currentPower = m_sink.RequiredInputByType(m_powerDefinitionId);
            var newPower = ComputeRequiredPower();
            if (Math.Abs(currentPower - newPower) > 0.001)
            {
                m_sink.SetMaxRequiredInputByType(m_powerDefinitionId, newPower);
                m_sink.SetRequiredInputByType(m_powerDefinitionId, newPower);
                //m_sink.Update();
            }

            if (m_shutDown)
            {
                MessageUtils.ShowMessageToUsersInRange(m_drill, "Insufficient power, shutting off drill.", 5000, true);
                try
                {
                    (m_drill as IMyFunctionalBlock).Enabled = false;
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                }
            }

            if ((m_drill as IMyShipDrill).IsWorking && m_target != Vector3D.Zero && !MyAPIGateway.Utilities.IsDedicated)
            {
                // Don't draw beams for local user if they are controlling it, for performance
                if (m_turret == null || MyAPIGateway.Session.CameraController.GetType().Name == "MySpectatorCameraController" ||
                    !((m_turret as IMyLargeGatlingTurret).IsUnderControl && MyAPIGateway.Players.GetPlayerControllingEntity(m_turret) == MyAPIGateway.Session.Player))
                    DrawBeams();
                //matrix.Translation = matrix.Translation + (matrix.Backward * 0.5);
                //VRage.Game.MySimpleObjectDraw.DrawTransparentSphere(ref matrix, 3f, ref color1, VRage.Game.MySimpleObjectRasterizer.Solid, 20, "WeaponLaser", "WeaponLaser", 1);
            }
            Profiler.End("LaserDrill.UpdateBeforeSimulation");
        }

        Vector3D m_nearestOre;

        // For a drill, this always executes, even when not drilling
        public override void UpdateBeforeSimulation10()
        {
            Profiler.Begin("LaserDrill.UpdateBeforeSimulation10");
            //Logger.Instance.LogMessage("update10");
            LaserDrillExtensions.ClearGPS();
            if (m_counter % 6 == 0)
            {
                (m_drill as IMyTerminalBlock).RefreshCustomInfo();
            }

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                try
                {
                    var rangeSq = TurretRange * TurretRange;
                    var range = TurretRange;

                    if (m_counter % 6 == 0)
                        FindOreDetector();

                    if (m_turret != null)
                    {
                        LaserDrillExtensions.ClearGPS();
                        if (!m_turret.IsFunctional)
                        {
                            MessageUtils.ShowMessageToUsersInRange(m_drill as IMyFunctionalBlock, "Turret not functional", 5000, true);
                            (m_drill as IMyShipDrill).Enabled = false;
                            return;
                        }

                        Logger.Instance.LogDebug("Using turret");
                        if ((m_turret as IMyLargeGatlingTurret).IsUnderControl)
                        {
                            m_nearestOre = m_turret.PositionComp.GetPosition() + (m_turret as MyEntity).Subparts["GatlingTurretBase1"].Subparts["GatlingTurretBase2"].Subparts["GatlingBarrel"].WorldMatrix.Forward * range;
                            m_target = RaycastToVoxel_New(m_nearestOre, range);
                            Logger.Instance.LogDebug("Under user control");
                        }
                        else
                        {
                            if (m_cachedDetector == null)
                                FindOreDetector();

                            //if (m_nearestOre == Vector3D.Zero || m_target == Vector3D.Zero)
                            //{
                            //    try
                            //    {
                            //        m_nearestOre = GetNearestMineableOre(range);
                            //    }
                            //    catch (Exception ex)
                            //    {
                            //        // AccessViolationException is the one we need to ignore
                            //        // Ignore error, but log it anyway
                            //        // Log error, but supress in-game message spam, since we can't avoid this
                            //        Logger.Instance.LogExceptionOnGameThread(ex, false);
                            //    }
                            //}
                            //MyAPIGateway.Parallel.Start(() =>
                            //{
                            try
                            {
                                m_nearestOre = GetNearestMineableOre(range);                             // Get ore to mine
                                m_target = RaycastToVoxel_New(m_nearestOre, range);
                            }
                            catch (Exception ex)
                            {
                                // AccessViolationException is the one we need to ignore
                                // Ignore error, but log it anyway
                                // Log error, but supress in-game message spam, since we can't avoid this
                                Logger.Instance.LogExceptionOnGameThread(ex, false);
                            }
                            //});
                            //m_nearestOre = GetNearestMineableOre();                             // Get ore to mine

                            if (m_nearestOre == Vector3D.Zero)
                            {
                                m_lastOreInPath = Vector3D.Zero;
                                if (m_cachedDetector == null)
                                    MessageUtils.ShowMessageToUsersInRange(m_drill as IMyFunctionalBlock, "Add ore detector for automatic mining.", 5000, true);
                                else
                                    MessageUtils.ShowMessageToUsersInRange(m_drill as IMyFunctionalBlock, "No ore detected nearby. Increase range, get closer, or wait for scanning (30s).", 5000, true);
                                (m_drill as IMyShipDrill).Enabled = false;

                                Profiler.End("LaserDrill.UpdateBeforeSimulation10");
                                return;
                            }
                            Logger.Instance.LogDebug("Get ore: " + m_nearestOre.ToString());
                        }
                    }
                    else
                    {
                        m_nearestOre = m_drill.PositionComp.GetPosition() + (m_drill as MyEntity).WorldMatrix.Forward * MaxRange;
                    }

                    Logger.Instance.LogDebugOnGameThread($"Nearest: {m_nearestOre}");
                    //MyAPIGateway.Parallel.Start(() =>
                    //{
                    try
                    {
                        m_target = RaycastToVoxel_New(m_nearestOre, range);
                    }
                    catch (Exception ex)
                    {
                        // AccessViolationException is the one we need to ignore
                        // Ingore errors, since they'll just spam the log, but the mod will still work
                        //Logger.Instance.LogExceptionOnGameThread(ex, false);
                    }
                    //});

                    // Do this in a thread, so the game doesn't lag
                    //if (!m_raycastTask.valid || m_raycastTask.IsComplete)
                    //{
                    //    m_raycastTask = MyAPIGateway.Parallel.Start(() =>
                    //    {

                    //// Try again with just shooting forward
                    //if (m_target == Vector3D.Zero)
                    //        m_target = RaycastToVoxel_New((m_drill.PositionComp.GetPosition());
                    //Logger.Instance.LogDebugOnGameThread("task end");
                    //    });
                    //}
                    //else
                    //{
                    //    Profiler.End("LaserDrill.UpdateBeforeSimulation10");
                    //    return;
                    //}
                    Logger.Instance.LogDebugOnGameThread("UpdateBeforeSimulation10: " + m_target.ToString());
                    if (m_target != Vector3D.Zero)
                    {
                        if (MyAPIGateway.Multiplayer.IsServer)
                        {
                            if (m_spawnedDrill == null)
                            {
                                m_spawnedDrill = SpawnFakeDrill(m_target);
                                if (m_spawnedDrill == null)
                                {
                                    // Something happened, abort
                                    (m_drill as IMyTerminalBlock).GetActionWithName("OnOff_Off").Apply(m_drill as IMyTerminalBlock);
                                    Profiler.End("LaserDrill.UpdateBeforeSimulation10");
                                    return;
                                }
                            }

                            if (m_spawnedDrill != null && !m_spawnedDrill.Closed)
                            {
                                EnableDrill();
                                // To keep the drill aimed where the original is, just use its WorldMatrix
                                var matrix = m_drill.WorldMatrix;
                                if (m_drill is IMyLargeGatlingTurret)
                                    matrix = (m_drill as MyEntity).Subparts["GatlingTurretBase1"].Subparts["GatlingTurretBase2"].Subparts["GatlingBarrel"].WorldMatrix;

                                //var quat = VRageMath.Quaternion.CreateFromForwardUp(matrix.Forward, matrix.Up);
                                //quat.Conjugate();
                                var newmat = m_spawnedDrill.PositionComp.WorldMatrixRef;
                                //newmat = VRageMath.MatrixD.Transform(newmat, quat);
                                //var forw = (m_drill.PositionComp.WorldMatrix.Translation - m_target);
                                //forw.Normalize();
                                //var rot = MatrixD.CreateFromDir(forw, m_drill.WorldMatrix.Up);
                                //var quat = Quaternion.CreateFromRotationMatrix(rot);
                                //var newmat = MatrixD.Transform(oldmat, quat);
                                newmat.Translation = m_target;
#if !(VERSION_192 || VERSION_193)
                                m_spawnedDrill.PositionComp.SetWorldMatrix(ref newmat);
#else
                                m_spawnedDrill.PositionComp.WorldMatrix = newmat;
#endif

                            }

                            if (m_turret != null)
                            {
                                //(m_turret as MyLargeTurretBase).LookAt(m_target);
                                (m_turret as IMyLargeGatlingTurret).SetTarget(m_target);  // TODO: Put this back after the game bug is fixed
                                //(m_turret as IMyLargeGatlingTurret).SetTarget(m_spawnedDrill);
                                //(m_turret as MyLargeTurretBase).TargetLocking = false;
                                //(m_turret as IMyLargeGatlingTurret).SetLockedTarget(m_spawnedDrill as IMyCubeGrid);  // TODO: Put this back after the game bug is fixed
                                //(m_turret as IMyLargeGatlingTurret).TrackTarget(m_spawnedDrill);
                            }

                            //if (m_counter % 6 == 0)
                            //{
                            //    Logger.Instance.LogMessage("Mining voxel at: " + m_target.ToString());
                            //    var intersect = OreDetector.Instance.GetVoxelContainingPoint(m_target);
                            //    byte materialRemoved;
                            //    float amount;
                            //    RemoveVoxelContent(intersect.EntityId, m_target, out materialRemoved, out amount);
                            //    SpawnInventory(materialRemoved, amount);
                            //}

                            //MessageUtils.SendMessageToAllPlayers(new MessageTarget() { EntityId = m_drill.EntityId, Target = m_target });
                            StartEffects();
                        }
                        //LaserDrillExtensions.ScaleBeam(m_drill, m_drillBeam, m_drill.PositionComp.GetPosition(), m_target);
                    }
                    else
                    {
                        m_lastOreInPath = Vector3D.Zero;
                        StopEffects();
                        if (MyAPIGateway.Multiplayer.IsServer)
                        {
                            //if (m_cachedDetector != null)
                            //    OreDetector.Instance.UpdateOreList(m_cachedDetector.PositionComp.GetPosition());
                            DisableDrill();
                        }
                    }
                    if (MyAPIGateway.Multiplayer.IsServer)
                        MessageUtils.SendMessageToAllPlayers(new MessageTarget() { EntityId = m_drill.EntityId, Target = m_target });

                    (m_drill as IMyTerminalBlock).RefreshCustomInfo();
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                }
            }
            Profiler.End("LaserDrill.UpdateBeforeSimulation10");
        }

        public override void UpdateBeforeSimulation100()
        {
            Profiler.Begin("LaserDrill.UpdateBeforeSimulation100");
            base.UpdateBeforeSimulation100();

            if (MyAPIGateway.Multiplayer.IsServer)
                TransferInventory();                                                    // Continuously transfer inventory out

            // If the drill has a problem and keeps spinning, just turn it off
            if (m_isStopped && (DateTime.Now - m_stopTime).TotalSeconds > Constants.MaxStoppedTimeSec && DrillingMode == DrillingMode.Automatic)
            {
                //MessageUtils.ShowMessageToUsersInRange(m_drill as IMyFunctionalBlock, "Ore detection is confused, stopping.", 5000, true);
                //MessageUtils.ShowMessageToUsersInRange(m_drill as IMyFunctionalBlock, "Ore detection is confused, stopping.", 5000, true);
                (m_drill as IMyFunctionalBlock).Enabled = false;
            }
            Profiler.End("LaserDrill.UpdateBeforeSimulation100");
        }

        void SpawnedDrill_OnClose(IMyEntity obj)
        {
            if (m_spawnedDrill != null && m_spawnedDrill == obj)
            {
                StopEffects();
                (obj as IMyCubeGrid).OnBlockRemoved -= SpawnedDrill_OnBlockRemoved;
                m_spawnedDrill = null;
            }
        }

        public float ComputeRequiredPower()
        {
            return ComputeRequiredPower(m_target);
        }

        public float ComputeRequiredPower(Vector3D target)
        {
            float power = Config.Compsumption.MinPowerCompsumption;
            if ((m_drill as IMyShipDrill).Enabled && target != Vector3D.Zero)
            {
                // Base on distance to target being drilled
                var dir = (m_drill.PositionComp.GetPosition() - target);
                power = (float)Math.Pow(dir.LengthSquared(), Config.Compsumption.PowerFactor);
            }

            if (MyAPIGateway.Session.CreativeMode)
                return 0.01f;

            //if (power > 0)
            //    Logger.Instance.LogDebug("ComputeRequiredPower: " + power);
            return power;
        }

        private void StartEffects()
        {
            if (MyAPIGateway.Session.Player == null)
                return;

            if (!(m_drill as IMyFunctionalBlock).IsWorking)
                return;

            var matrix = MatrixD.CreateTranslation(m_target);
            //matrix.Translation += m_spawnedDrill.WorldMatrix.Backward * 2;

            if (m_particleEffect != null)
            {
                m_particleEffect.WorldMatrix = matrix;
                return;
            }

            //StopEffects();
            MyParticlesManager.TryCreateParticleEffect((int)506, out m_particleEffect);
            Logger.Instance.LogAssert(m_particleEffect != null, "m_particleEffect != null");
            if (m_particleEffect != null)
            {
                m_particleEffect.UserScale = m_turret == null ? 5.0f : 2.0f;
                m_particleEffect.WorldMatrix = matrix;
                m_particleEffect.Play();
            }
            //UpdateParticleMatrix();

            //m_effectLight = CreateEffectLight();
        }

        protected void StopEffects()
        {
            if (MyAPIGateway.Session.Player == null)
                return;

            if (m_particleEffect != null)
            {
                m_particleEffect.Stop();
                m_particleEffect = null;
            }

            //if (m_effectLight != null)
            //{
            //    MyLights.RemoveLight(m_effectLight);
            //    m_effectLight = null;
            //}
        }

        private void DrawBeams()
        {
            float timeDelta = ((int)MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds) / 1000f;
            float rotationDeltaAngle = (timeDelta * 1f * MathHelper.TwoPi * 2.0f) % MathHelper.TwoPi;
            var matrix = (m_drill as MyEntity).PositionComp.WorldMatrixRef;

            var rot = Quaternion.CreateFromAxisAngle((Vector3)matrix.Forward, -rotationDeltaAngle);
            matrix = MatrixD.Transform(matrix, rot);
            matrix.Translation = (m_drill as MyEntity).PositionComp.WorldMatrixRef.Translation;

            Vector3D start = matrix.Translation;
            var sideOffset = matrix.Left * 2.9f;
            var forwardDir = matrix.Forward;
            var frontOffset = matrix.Forward * 0;

            // If there is a turret, place the beams relative to where it's pointing.
            if (m_turret != null && !m_turret.MarkedForClose && m_turret.IsFunctional)
            {
                var barrel = (m_turret as MyEntity).Subparts["GatlingTurretBase1"].Subparts["GatlingTurretBase2"].Subparts["GatlingBarrel"];
                matrix = barrel.WorldMatrix;
                start = matrix.Translation;
                sideOffset = matrix.Left * 1.8f;
                frontOffset = matrix.Forward * 2.1f;
                forwardDir = matrix.Forward;
            }

            var target = (m_target == Vector3D.Zero ? (start + forwardDir * 20) : m_target);
            var dir = target - start;
            dir.Normalize();
            target = target + (dir * 3);

            var maincolor = PrimaryBeamColor.ToVector4();
            var auxcolor = SecondaryBeamColor.ToVector4();
            var packetcolor = Color.DarkOrange.ToVector4();

            if (m_currentPacketManager == null)
                m_currentPacketManager = new PacketManager(target, start, packetcolor);

            if (m_currentPacketManager.Origin != start)
            {
                m_currentPacketManager.Origin = start;

                foreach (var packet in m_previousPacketManagers)
                    packet.Origin = start;

            }
            if (m_counter % 60 == 0)
            {
                if (m_currentPacketManager.Target != m_target && target != Vector3D.Zero)
                {
                    m_previousPacketManagers.Add(m_currentPacketManager);
                    m_currentPacketManager = new PacketManager(target, start, packetcolor);
                }
            }

            for (int idx = 0; idx < m_previousPacketManagers.Count; idx++)
            {
                if (!m_previousPacketManagers[idx].DrawPackets(false))
                {
                    m_previousPacketManagers.RemoveAt(idx);
                    idx--;
                }
            }

            var beam = MyStringId.GetOrCompute("BeamLaser");

            VRage.Game.MySimpleObjectDraw.DrawLine(start + frontOffset - sideOffset, target - sideOffset, beam, ref auxcolor, 0.33f, BlendTypeEnum.LDR);
            VRage.Game.MySimpleObjectDraw.DrawLine(start, target, beam, ref maincolor, 1f, BlendTypeEnum.LDR);
            VRage.Game.MySimpleObjectDraw.DrawLine(start + frontOffset + sideOffset, target + sideOffset, beam, ref auxcolor, 0.33f, BlendTypeEnum.LDR);

            // Draw 'pulsing' beam
            if (m_counter % 20 == 0)
                VRage.Game.MySimpleObjectDraw.DrawLine(start, target, beam, ref maincolor, 1.1f, BlendTypeEnum.LDR);

            m_currentPacketManager.DrawPackets();
        }

        List<IMySlimBlock> m_terminalCache = new List<IMySlimBlock>();

        #region Interface Implementation
        public IMyEntity SpawnFakeDrill(Vector3D position)
        {
            bool success = true;

            if (!MyAPIGateway.Multiplayer.IsServer)
                return null;

            var sphere = new BoundingSphereD(position, 200);
            // All sensors must have station detection turned off
            ((m_drill as IMyTerminalBlock).CubeGrid as IMyCubeGrid).GetBlocks(m_terminalCache, x => x.FatBlock is IMySensorBlock);

            foreach (var block in m_terminalCache)
            {
                if ((block.FatBlock as IMySensorBlock).IsWorking && (block.FatBlock as IMySensorBlock).DetectStations)
                    success = false;
            }
            m_terminalCache.Clear();

            if (success)
            {
                var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
                foreach (var entity in entities)
                {
                    if (entity is IMySensorBlock && (entity as IMySensorBlock).IsWorking && (entity as IMySensorBlock).DetectStations)
                    {
                        success = false;
                        break;
                    }
                }
            }
            if (!success)
            {
                MessageUtils.ShowMessageToUsersInRange(Container.Entity as IMyFunctionalBlock, "Disable 'Detect Stations' in all sensor blocks to prevent game crash.", 5000, true);
                return null;
            }
            return LaserDrillExtensions.SpawnFakeDrill(m_turret ?? m_drill, position);
        }

        public IMyCubeBlock GetLargestOreDetector()
        {
            return LaserDrillExtensions.GetLargestOreDetector(m_drill);
        }

        public Vector3D GetNearestMineableOre(float range, bool updateTerminal = true)
        {
            Profiler.Begin("LaserDrill.GetNearestMineableOre");
            //if (m_cachedDetector == null)
            //    FindOreDetector();
            var pos = GetNearestMineableOre(m_cachedDetector, m_turret ?? m_drill, ref m_targetOre, range, updateTerminal);
            Profiler.End("LaserDrill.GetNearestMineableOre");
            return pos;
        }

        DateTime m_timeSinceLastDetection = DateTime.Now;
        Vector3D m_lastOreInPath = Vector3D.Zero;

        public Vector3D GetNearestMineableOre(IMyCubeBlock detector, IMyEntity reference, ref string targetOreName, float range, bool updateTerminal = true)
        {
            Profiler.Begin("LaserDrill.GetNearestMineableOre1");
            var refpos = Vector3D.Zero;      // Defaulting to the reference position will just drill forward

            if (reference == null)
                return refpos;

            var blockPosition = reference.PositionComp.GetPosition();

            Profiler.Begin("LaserDrill.GetNearestMineableOre1_IsI");
            if (m_target1 != Vector3D.Zero && OreDetector.Instance.IsInsideVoxel(m_target1))
            {
                //Logger.Instance.LogMessage("target: " + m_target1);
                Profiler.End("LaserDrill.GetNearestMineableOre1_IsI");
                Profiler.End("LaserDrill.GetNearestMineableOre1");
                return m_target1;
            }
            Profiler.End("LaserDrill.GetNearestMineableOre1_IsI");

            var turretRange = _maxRange;
            turretRange = range * range;

            if (detector != null && !detector.Closed && !detector.MarkedForClose &&
                reference != null && !reference.Closed && !reference.MarkedForClose)
            {
                // Frustum didn't work, so use a simple plane as the drilling angle limits
                var plane = new PlaneD(blockPosition + (reference.WorldMatrix.Down * 5), reference.WorldMatrix.Up);

                Profiler.Begin("LaserDrill.GetNearestMineableOre1_OL");
                // Allow world loading to continue mining where it left off
                if ((DateTime.Now - m_timeSinceLastDetection).TotalMilliseconds < 1000)
                    refpos = blockPosition;

                // Use the detector to find the closest ore deposit
                // TODO: Lock this instead of allocating
                var orelist = detector.GameLogic.GetAs<OreDetectorGameLogic>().MiningInformation.ToList();

                //var orelist = OreDetector.Instance.OreLocations;
                //OreDetector.Instance.GetOreLocations(ref orelist);
                var closest = new MiningInformation();
                var closestIron = new MiningInformation();
                var closestIce = new MiningInformation();

                Logger.Instance.LogDebugOnGameThread("Ore count: " + orelist.Count);

                string privList = null;
                // Priority mode
                if (reference.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled)
                {
                    privList = reference.GameLogic.GetAs<LaserDrillTurret>().PriorityList.FirstOrDefault((x) =>
                    {
                        Logger.Instance.LogDebug("Priority testing: " + x);
                        var ore = orelist.FirstOrDefault((i) =>
                        {
                            Logger.Instance.LogDebugOnGameThread($"Ore range: {(blockPosition - i.Location).LengthSquared()}, Turret Range: {turretRange }");
                            if (i.Material.MinedOre == x && (blockPosition - i.Location).LengthSquared() < turretRange &&
                            plane.Intersects(new BoundingBoxD() { Min = blockPosition, Max = i.Location }) != PlaneIntersectionType.Back)
                            {
                                if (i.Positions?.Count > 0)
                                    return true;
                            }
                            return false;
                        });

                        if (ore != null && ore.Material != null)
                        {
                            Logger.Instance.LogDebug("Priority ore found: " + ore.Material.MinedOre);
                            closest = ore;
                            return true;
                        }

                        return false;
                    });
                }
                else
                {
                    foreach (var deposit in orelist)
                    {
                        Logger.Instance.LogDebugOnGameThread(string.Format("Testing {0}; {1}", deposit.Material.MinedOre, deposit.Location));
                        //if (drillExclusionFrustum.Contains(ore.Value) == ContainmentType.Contains)
                        //    continue;

                        if (plane.Intersects(new BoundingBoxD() { Min = blockPosition, Max = deposit.Location }) == PlaneIntersectionType.Intersecting)
                            continue;

                        Profiler.Begin("LaserDrill.GetNearestMineableOre1_RC");
                        var location = RaycastToVoxel_New(deposit.Location, range);
                        if (location == Vector3D.Zero || (((location - blockPosition).LengthSquared() > (deposit.Location - blockPosition).LengthSquared())))
                        {
                            Logger.Instance.LogDebugOnGameThread("Invalid locaton");
                            //m_cachedDetector?.GameLogic?.GetAs<OreDetectorGameLogic>().RemoveSingleMaterialAtPoint(ore.Value);
                            continue;
                        }
                        Profiler.End("LaserDrill.GetNearestMineableOre1_RC");

                        if (string.IsNullOrEmpty(closest?.Material?.MinedOre) && closest.Location == Vector3D.Zero)
                            closest = deposit;

                        Logger.Instance.LogDebugOnGameThread("Ore to target distance: " + (detector.PositionComp.GetPosition() - deposit.Location).LengthSquared());
                        //Logger.Instance.LogDebug("Last to target distance: " + (detector.PositionComp.GetPosition() - closest.Value).LengthSquared());
                        //Logger.Instance.LogDebug("Turret to target distance: " + (blockPosition - ore.Value).LengthSquared());
                        //Logger.Instance.LogDebug("Turret distance: " + turretRange);

                        if ((blockPosition - deposit.Location).LengthSquared() < (closest.Location - deposit.Location).LengthSquared() &&
                            deposit.Positions?.Count > 0)
                        {
                            Logger.Instance.LogDebugOnGameThread("Found ore: " + deposit.Material.MinedOre);
                            if (deposit.Material.MinedOre == "Iron")
                                closestIron = deposit;
                            else if (deposit.Material.MinedOre == "Ice")
                                closestIce = deposit;
                            else
                                closest = deposit;
                        }
                        else
                        {
                            Logger.Instance.LogDebugOnGameThread($"IF: {((blockPosition - deposit.Location).LengthSquared() < turretRange)}");
                            Logger.Instance.LogDebugOnGameThread($"Voxel count at deposit: {deposit.Positions?.Count}");
                        }
                    }

                    if (string.IsNullOrEmpty(closest?.Material?.MinedOre) && !string.IsNullOrEmpty(closestIron?.Material?.MinedOre))
                        closest = closestIron;

                    if (string.IsNullOrEmpty(closest?.Material?.MinedOre) && !string.IsNullOrEmpty(closestIce?.Material?.MinedOre))
                        closest = closestIce;
                }

                Logger.Instance.LogDebugOnGameThread($"Closest: {closest?.Material?.MinedOre}, at {closest?.Location}");
                if (!string.IsNullOrEmpty(closest?.Material?.MinedOre))
                {
                    byte content, material;
                    var cache = new MyStorageData();

                    //foreach (var vector in closest.Positions)
                    for (int posidx = 0; posidx < closest.Positions.Count; posidx++)
                    {
                        refpos = closest.Positions[posidx];
                        var dir = refpos - blockPosition;
                        dir.Normalize();
                        Logger.Instance.LogDebugOnGameThread("Before: " + refpos);

                        //// To optimize scanning, once we found the last spot, save it so we mine all the way to it
                        ////var newrefpos = OreDetector.Instance.GetLastMatchingVoxelInDirection(refpos, dir);
                        //refpos = refpos + dir;
                        //// Don't exceed drill range
                        //if ((blockPosition - refpos).LengthSquared() > turretRange)
                        //    refpos = refpos + (dir * range);
                        m_lastOreInPath = refpos;

                        OreDetector.Instance.GetVoxelContent(closest.Voxel, m_lastOreInPath, out content, out material, cache);
                        Logger.Instance.LogDebugOnGameThread($"Testing coord: {m_lastOreInPath}");
                        if (content < MyVoxelConstants.VOXEL_ISO_LEVEL)
                        {
                            closest.Positions.Remove(closest.Positions[posidx]);
                            posidx--;
                            Logger.Instance.LogDebugOnGameThread("Voxel is empty");
                            continue;
                        }
                        //Logger.Instance.LogDebug(string.Format("Caching location: {0}", m_lastOreInPath));
                        //}

                        Logger.Instance.LogDebugOnGameThread("After: " + refpos);

                        m_timeSinceLastDetection = DateTime.Now;
                        if (targetOreName != closest.Material.MinedOre)
                        {
                            targetOreName = closest.Material.MinedOre;
                            if (updateTerminal)
                                MyAPIGateway.Utilities.InvokeOnGameThread(() => (reference as IMyTerminalBlock).RefreshCustomInfo());
                        }
                        break;
                    }
                }
                else
                {
                    if (targetOreName != Constants.StatusNone)
                    {
                        targetOreName = Constants.StatusNone;
                        if (updateTerminal)
                            MyAPIGateway.Utilities.InvokeOnGameThread(() => (reference as IMyTerminalBlock).RefreshCustomInfo());
                    }
                }

                Profiler.End("LaserDrill.GetNearestMineableOre1_CL");

            }
            else
            {
                targetOreName = Constants.StatusNone;
                if (updateTerminal)
                    MyAPIGateway.Utilities.InvokeOnGameThread(() => (reference as IMyTerminalBlock)?.RefreshCustomInfo());

                Logger.Instance.LogDebug("detector or turret null");
            }
            Profiler.End("LaserDrill.GetNearestMineableOre1");
            return refpos;
        }

        public void FindOreDetector()
        {
            m_cachedDetectorRange = m_drill.FindOreDetector(ref m_cachedDetector, m_cachedDetector_OnClose);
        }

        public void EnableDrill()
        {
            m_isStopped = false;
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            LaserDrillExtensions.EnableDrill(m_spawnedDrill);
            //if (m_turret != null && !(m_turret as Ingame.IMyLargeGatlingTurret).IsUnderControl)
            //    (m_turret as IMyTerminalBlock).GetActionWithName("Shoot_On").Apply(m_turret);
        }

        DateTime m_stopTime;
        bool m_isStopped = false;
        public void DisableDrill()
        {
            if (!m_isStopped)
                m_stopTime = DateTime.Now;

            m_isStopped = true;
            StopEffects();

            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            LaserDrillExtensions.DisableDrill(m_spawnedDrill);
            //if (m_turret != null && !(m_turret as Ingame.IMyLargeGatlingTurret).IsUnderControl)
            //    (m_turret as IMyTerminalBlock).GetActionWithName("Shoot_Off").Apply(m_turret);
        }

        public void RemoveDrill()
        {
            if (MyAPIGateway.Multiplayer == null || !MyAPIGateway.Multiplayer.IsServer)
                return;
            LaserDrillExtensions.RemoveDrill(m_spawnedDrill);
            m_cachedSrcInventory = null;
            m_spawnedDrill = null;

            //if (m_drillBeam != null)
            //{
            //    if (m_drillBeam.SyncObject != null)
            //        m_drillBeam.SyncObject.SendCloseRequest();
            //    else
            //        m_drillBeam.Close();
            //}
            //m_drillBeam = null;
        }

        /// <summary>
        /// Prefetch voxel physics if it is currently needed.
        /// </summary>
        private void PrefetchVoxelPhysicsIfNeeded(Vector3D origin, Vector3D destination)
        {
            var line = new LineD(origin, destination);
            var direction = destination - origin;
            direction.Normalize();

            LineD approximatedLine = new LineD(
                new Vector3D(Math.Floor(line.From.X) * m_prefetchedVoxelRaysSourceMapping,
                    Math.Floor(line.From.Y) * m_prefetchedVoxelRaysSourceMapping,
                    Math.Floor(line.From.Z) * m_prefetchedVoxelRaysSourceMapping),
                new Vector3D(Math.Floor(direction.X * m_prefetchedVoxelRaysDirectionMapping),
                    Math.Floor(direction.Y * m_prefetchedVoxelRaysDirectionMapping),
                    Math.Floor(direction.Z * m_prefetchedVoxelRaysDirectionMapping)));

            if (m_prefetchedVoxelRaysTimedCache.IsItemPresent(approximatedLine.GetHash(), (int)MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds))
            {
                return; // no need to compute it again now
            }

            using (MyUtils.ReuseCollection(ref m_entityRaycastResult))
            {
                MyGamePruningStructure.GetAllEntitiesInRay(ref line, m_entityRaycastResult, MyEntityQueryType.Static);
                foreach (var entity in m_entityRaycastResult)
                {
                    var planetPhysics = entity.Element as MyVoxelBase;
                    if (planetPhysics != null)
                    {
                        Logger.Instance.LogDebugOnGameThread($"Prefetching: {planetPhysics?.StorageName ?? planetPhysics?.DisplayName}");
                        (planetPhysics.RootVoxel as MyPlanet).PrefetchShapeOnRay(ref line);
                    }
                }
            }
        }

        MyStorageData m_cache = new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
        public Vector3D RaycastToVoxel_New(Vector3D preferredTarget, float range)
        {
            Profiler.Begin("LaserDrill.RaycastToVoxel_New");
            var hitinfos = new List<IHitInfo>();
            var direction = (preferredTarget - (m_turret ?? m_drill).PositionComp.GetPosition());
            direction.Normalize();
            preferredTarget += direction * range;
            var start = (m_turret ?? m_drill).PositionComp.GetPosition();

            //PrefetchVoxelPhysicsIfNeeded(start, preferredTarget);

            MyAPIGateway.Physics.CastRay(start, preferredTarget, hitinfos, 11);
            if (hitinfos.Count == 0 && m_raycastResult.Count > 0)
            {
                if (Globals.Debug)
                    Logger.Instance.LogDebugOnGameThread($"Using cached result: {m_raycastResult[0].Position}");
                return m_raycastResult[0].Position;
            }

            if (Globals.Debug)
                Logger.Instance.LogDebugOnGameThread(string.Format("raycast: from: {0}; to: {1}; count: {2}", start, preferredTarget.ToString(), hitinfos.Count));

            foreach (var hitinfo in hitinfos)
            {
                if (Globals.Debug)
                    Logger.Instance.LogDebugOnGameThread("CastRay entity: " + hitinfo.HitEntity?.DisplayName ?? string.Empty);
                if (hitinfo.HitEntity is IMyVoxelBase)
                {
                    if (Globals.Debug)
                    {
                        Logger.Instance.LogDebugOnGameThread("CastRay hit voxel: " + hitinfo.Position);
                        Logger.Instance.LogDebugOnGameThread("CastRay hit distance2: " + ((m_turret ?? m_drill).PositionComp.GetPosition() - hitinfo.Position).LengthSquared());
                    }
                    Profiler.End("LaserDrill.RaycastToVoxel_New");
                    return hitinfo.Position;
                }
            }

            byte content, material;

            // Nothing was found, quite possibly the center of the voxel is gone, but there's still contents
            if (OreDetector.Instance.GetVoxelContent(preferredTarget, out content, out material))
            {
                if (Globals.Debug)
                    Logger.Instance.LogDebugOnGameThread("Voxel content: " + content);
                if (content >= MyVoxelConstants.VOXEL_ISO_LEVEL)
                {
                    Profiler.End("LaserDrill.RaycastToVoxel_New");
                    return preferredTarget;
                }
            }
            Profiler.End("LaserDrill.RaycastToVoxel_New");

            return Vector3D.Zero;
        }

        public Vector3D RaycastToVoxel_Dot(Vector3D preferredTarget)
        {
            Profiler.Begin("LaserDrill.RaycastToVoxel_Dot");
            byte content, material;
            var startPoint = (m_turret ?? m_drill).PositionComp.GetPosition();
            var direction = (preferredTarget - startPoint);
            direction.Normalize();
            preferredTarget += direction * TurretRange;

            Logger.Instance.LogDebug(string.Format("raycast: from: {0}; to: {1}", (m_turret ?? m_drill).PositionComp.GetPosition(), preferredTarget.ToString()));

            var voxel = OreDetector.Instance.GetVoxelContainingPoint(preferredTarget);
            var cache = new MyStorageData();
            if (OreDetector.Instance.GetVoxelContent(voxel, startPoint, out content, out material, cache, preferredTarget, MyVoxelRequestFlags.SurfaceMaterial))
            {
                Vector3I Min, Max;
                MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref startPoint, out Min);
                MyVoxelCoordSystems.WorldPositionToVoxelCoord(voxel.PositionLeftBottomCorner, ref preferredTarget, out Max);

                // Enumerate voxel points
                Vector3I c;
                for (c.Z = Min.Z; c.Z <= Max.Z; ++c.Z)
                {
                    for (c.Y = Min.Y; c.Y <= Max.Y; ++c.Y)
                    {
                        for (c.X = Min.X; c.X <= Max.X; ++c.X)
                        {
                            // If the dot product is less than the voxel half size, we are in the voxel along that line
                            Vector3D worldpos;
                            MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionLeftBottomCorner, ref c, out worldpos);
                            if (direction.Dot(worldpos) < MyVoxelConstants.VOXEL_SIZE_IN_METRES_HALF)
                            {
                                Logger.Instance.LogDebug($"Dot product passed");
                                // If we are close, then grab the content to see if it's the surface level
                                content = cache.Content(ref c);
                                if (content > 0 && content < MyVoxelConstants.VOXEL_ISO_LEVEL)
                                {
                                    Logger.Instance.LogDebug($"Found surface voxel: {worldpos}");
                                    return worldpos;
                                }
                            }
                            break;
                        }
                        break;
                    }
                    break;
                }
            }

            //// Nothing was found, quite possibly the center of the voxel is gone, but there's still contents
            //if (OreDetector.Instance.GetVoxelContent(preferredTarget, out content, out material))
            //{
            //    Logger.Instance.LogDebugOnGameThread("Voxel content: " + content);
            //    if (content >= MyVoxelConstants.VOXEL_ISO_LEVEL)
            //    {
            //        Profiler.End("LaserDrill.RaycastToVoxel_New");
            //        return preferredTarget;
            //    }
            //}
            Profiler.End("LaserDrill.RaycastToVoxel_New");

            return Vector3D.Zero;
        }

        public Vector3D RaycastToVoxel(Vector3D preferredTarget)
        {
            float checkRange = 10;        // How far to increment each collision check 
            float preciseCheckRange = 0.5f;        // How far to increment each collision check 
            Vector3D location = Vector3D.Zero;
            bool precise = false;
            var drill = m_turret ?? m_drill;
            var source = drill.PositionComp.GetPosition();
            var target = preferredTarget;
            Logger.Instance.LogDebugOnGameThread("type: " + drill.GetType() + " start: " + drill.PositionComp.GetPosition().ToString());
            Logger.Instance.LogDebugOnGameThread("preferred: " + preferredTarget.ToString());

            // If our target is set to the drill position itself, just move forward along the drill vector
            if (preferredTarget == source)
            {
                var forward = drill.PositionComp.WorldMatrixRef.Forward;

                if (drill is IMyLargeGatlingTurret)
                    forward = (drill as MyEntity).Subparts["GatlingTurretBase1"].Subparts["GatlingTurretBase2"].Subparts["GatlingBarrel"].WorldMatrix.Forward;

                //MyAPIGateway.Utilities.ShowNotification("self", 90);
                if (drill is IMyCameraController)
                {
                    //MyAPIGateway.Utilities.ShowNotification("Camera", 90);
                    //var matrix = (drill as IMyCameraController).GetViewMatrix();
                    //matrix = MatrixD.Invert(matrix);
                    var matrix = (drill as MyEntity).Subparts["GatlingTurretBase1"].Subparts["GatlingTurretBase2"].Subparts["GatlingBarrel"].WorldMatrix;
                    target = target + (matrix.Forward * TurretRange);
                }
                else
                {
                    target = target + (forward * TurretRange);
                }
                source = source + forward * 5;
            }

            Logger.Instance.LogDebugOnGameThread("Target: " + target.ToString());
            var dir = target - source;
            dir.Normalize();

            target = target + (dir * 2);                                  // To avoid coming up short, make the target 2m further
            //Logger.Instance.LogMessage(string.Format("Dir: ", dir.X, dir.Y, dir.Z));
            //Logger.Instance.LogMessage(string.Format("refpos: ", refpos.X, refpos.Y, refpos.Z));
            List<MyEntity> entities = null;
            var raycast = Sandbox.Game.Entities.MyEntities.IsRaycastBlocked(source, target);

            if (!raycast)
            {
                var aabb = new BoundingBoxD(source - VoxelConstants.VOXEL_RADIUS / 2, target + VoxelConstants.VOXEL_RADIUS / 2);
                entities = Sandbox.Game.Entities.MyEntities.GetEntitiesInAABB(ref aabb);
            }

            if (raycast || (entities != null && entities.Count > 0))
            //if (entities != null && entities.Count > 0)
            {
                Logger.Instance.LogDebugOnGameThread("Testing collision");
                //foreach (var entity in entities)
                //{
                //    Logger.Instance.LogDebugOnGameThread("collision with entity: " + entity.ToString());
                //}
                // We know we are blocked, now we need to find by what, and where
                // This will be horribly inefficient
                var checkpoint = source;
                do
                {
                    checkpoint = checkpoint + (dir * (precise ? preciseCheckRange : checkRange));    // Increment 10m
                    var sphere = new BoundingSphereD(checkpoint, (precise ? preciseCheckRange : checkRange * 0.9f));
                    //var sphere = new BoundingSphereD(checkpoint, (precise ? MyVoxelConstants.VOXEL_RADIUS : checkRange * 0.9f));
                    if (Globals.Debug)
                    {
                        //_debugPoints.Add(MyAPIGateway.Session.GPS.Create(null, null, checkpoint, true, true));
                        //MyAPIGateway.Session.GPS.AddLocalGps(_debugPoints.Last<IMyGps>());
                    }

                    // TODO: Getting all entities causes jumping drills, need to fix
                    IMyEntity intersect = Sandbox.Game.Entities.MyEntities.GetIntersectionWithSphere(ref sphere, (drill as IMyCubeBlock).CubeGrid as VRage.Game.Entity.MyEntity, drill as VRage.Game.Entity.MyEntity, false, false, true);
                    if (intersect == null)
                    {
                        try
                        {
                            intersect = OreDetector.Instance.GetVoxelContainingPoint(checkpoint);
                        }
                        catch (InvalidOperationException ex)
                        {
                            // Lets just assume it's bad
                            //OreDetector.Instance.RemoveSingleMaterial(sphere.Center);
                            // Collection modified error
                            Logger.Instance.LogException(ex);
                        }
                    }

                    // TODO implement non-voxel collision detection
                    if (intersect != null)
                    {
                        byte content, material;
                        OreDetector.Instance.GetVoxelContent(intersect as IMyVoxelBase, checkpoint, out content, out material);
                        var voxelMat = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
                        if (content == MyVoxelConstants.VOXEL_CONTENT_EMPTY || voxelMat == null)
                        {
                            Logger.Instance.LogDebugOnGameThread("Empty: " + checkpoint);
                            continue;
                        }
                        //if (voxelMat.MinedOre == "Stone")
                        //Logger.Instance.LogMessage(string.Format("Voxel content: {0} at {1}", content, checkpoint));
                        //else
                        //{
                        //    Logger.Instance.LogMessage("Found Voxel: " + checkpoint.ToString());
                        //}

                        //    if (intersect is VRage.Game.Entity.MyEntitySubpart)
                        //        continue;

                        //    if (intersect.GetTopMostParent() == m_spawnedDrill)
                        //        continue;

                        //    if (intersect is IMyFloatingObject)
                        //        continue;

                        //    if (intersect is IMyShipDrill && (intersect as IMyShipDrill).CustomName == Constants.DrillCustomName)
                        //        continue;

                        //    if (intersect is IMyCubeGrid && (intersect as IMyCubeGrid).DisplayName == Constants.DrillCustomName)
                        //        continue;

                        if (!precise)
                        {
                            sphere.Center = sphere.Center - (drill.PositionComp.WorldMatrixRef.Forward * checkRange);    // Increment 10m
                            precise = true;
                            continue;
                        }

                        Logger.Instance.LogDebugOnGameThread(string.Format("Intersection of {0} at {1}", intersect.GetType(), checkpoint));
                        //Logger.Instance.LogDebugOnGameThread(string.Format("Intersection: {0}, {1}, {2}", sphere.Center.X, sphere.Center.Y, sphere.Center.Z));
                        return checkpoint;
                    }
                    else
                    {
                        //OreDetector.Instance.RemoveSingleMaterial(sphere.Center);
                        Logger.Instance.LogDebugOnGameThread(string.Format("No intersection at {0}", checkpoint));
                    }
                    //Logger.Instance.LogDebugOnGameThread(string.Format("step: {0}", checkpoint.ToString()));
                } while (
                            (checkpoint - target).LengthSquared() > Vector3D.One.LengthSquared() &&
                            (checkpoint - m_drill.PositionComp.GetPosition()).LengthSquared() < MaxRange * MaxRange);

                if (location == Vector3D.Zero && preferredTarget != Vector3D.Zero)
                {
                    // If the voxel is only a single one remaining, try to target it directly
                    byte material, content;
                    var voxel = OreDetector.Instance.GetVoxelContainingPoint(preferredTarget);
                    OreDetector.Instance.GetVoxelContent(voxel, preferredTarget, out content, out material);
                    Logger.Instance.LogDebugOnGameThread(string.Format("Could not find ore at {0}; Material {1}, Content: {2}", preferredTarget, material, content));

                    if (OreDetector.Instance.GetVoxelContent(OreDetector.Instance.GetVoxelContainingPoint(preferredTarget),
                        preferredTarget, out content, out material) && content > MyVoxelConstants.VOXEL_CONTENT_EMPTY)
                    {
                        Logger.Instance.LogDebugOnGameThread("Using original ore location");
                        return preferredTarget;
                    }

                }
            }
            return location;
        }
        #endregion

        public void ToggleTurretConnection(IMyCubeBlock turret)
        {
            m_turret = turret;
            SetEmissives();
            RemoveDrill();
            if ((m_drill as IMyFunctionalBlock)?.Enabled == true)
                (m_turret as IMyFunctionalBlock)?.ApplyAction("Shoot_On");
            else
                (m_turret as IMyFunctionalBlock)?.ApplyAction("Shoot_Off");
            //(m_drill as IMyFunctionalBlock).EnabledChanged -= LaserDrill_EnabledChanged;
        }

        /// <summary>
        /// This removes the reference to the detector we are monitoring.
        /// </summary>
        /// <param name="obj"></param>
        void m_cachedDetector_OnClose(IMyEntity obj)
        {
            m_cachedDetector = null;
        }

        void SpawnedDrill_OnBlockRemoved(IMySlimBlock obj)
        {
            // We don't care what happened, just regenerate the drill platform
            RemoveDrill();
        }


        /// <summary>
        /// Transfer all inventory from source to destination
        /// </summary>
        private void TransferInventory()
        {
            if (m_spawnedDrill == null)
                return;

            if (m_cachedSrcInventory == null || m_cachedDstInventory == null)
            {
                try
                {
                    // Get Source inventory (from spawned drill)
                    var srcdrill = (m_spawnedDrill as IMyCubeGrid).GetCubeBlock(new Vector3I(0, 0, 0)).FatBlock as MyEntity;
                    Logger.Instance.LogAssert(srcdrill.InventoryCount == 1, "srcdrill.InventoryCount == 0");
                    m_cachedSrcInventory = srcdrill.GetInventory(0);

                    // Get destination inventory (from grid drill)
                    var dstdrill = m_drill as MyEntity;
                    Logger.Instance.LogAssert(dstdrill.InventoryCount == 1, "dstdrill.InventoryCount == 0");
                    m_cachedDstInventory = dstdrill.GetInventory(0);
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                }
            }

            try
            {
                // Transfer items
                var items = m_cachedSrcInventory.GetItems();

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (!CollectStone && item.Content.SubtypeName == "Stone")
                    {
                        Logger.Instance.LogDebug(string.Format("Destroying {0:F2} of {1}", (float)item.Amount, item.Content.SubtypeName));
                        m_cachedSrcInventory.Remove(item, item.Amount);
                    }
                    else
                    {
                        if (m_cachedDstInventory.ItemsCanBeAdded(item.Amount, item))
                        {
                            Logger.Instance.LogDebug(string.Format("Transferring {0:F2} of {1}", (float)item.Amount, item.Content.SubtypeName));
                            m_cachedDstInventory.TransferItemsFrom(m_cachedSrcInventory, item, item.Amount);
                        }
                        else
                        {
                            // If inventory is full, transfer what is possible, then shut the drill off and notify the user
                            Logger.Instance.LogDebug(string.Format("Can not transfer {0:F2} of {1}, inventory full?", (float)item.Amount, item.Content.SubtypeName));
                            m_cachedDstInventory.TransferItemsFrom(m_cachedSrcInventory, item, item.Amount);
                            MessageUtils.ShowMessageToUsersInRange(m_drill as IMyFunctionalBlock, "Inventory full, mining stopped", 5000, true);
                            (m_drill as IMyFunctionalBlock).Enabled = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        /// <summary>
        /// Create ore in drill inventory
        /// </summary>
        private void SpawnInventory(byte material, float amount)
        {
            if (m_cachedDstInventory == null)
            {
                try
                {
                    // Get destination inventory (from grid drill)
                    var dstdrill = m_drill as MyEntity;
                    Logger.Instance.LogAssert(dstdrill.InventoryCount == 1, "dstdrill.InventoryCount == 0");
                    m_cachedDstInventory = dstdrill.GetInventoryBase(0) as MyInventory;
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogException(ex);
                }
            }

            var voxelMat = MyDefinitionManager.Static.GetVoxelMaterialDefinition(material);
            if (!string.IsNullOrEmpty(voxelMat.MinedOre))
            {
                var definitionId = new MyDefinitionId(typeof(MyObjectBuilder_Ore), voxelMat.MinedOre);
                var definition = MyDefinitionManager.Static.GetDefinition(definitionId);

                // Fill ammo inventory constantly
                var item = new MyObjectBuilder_InventoryItem()
                {
                    Amount = (MyFixedPoint)amount,
                    Content = new MyObjectBuilder_Ore() { SubtypeName = voxelMat.MinedOre },
                    ItemId = 0
                };

                if ((m_cachedDstInventory as MyInventory).CanItemsBeAdded(item.Amount, item.Content.GetId()) && m_cachedDstInventory.GetItemsCount() == 0)
                {
                    Logger.Instance.LogMessage(string.Format("Adding {0} kg of {1}", item.Amount, voxelMat.MinedOre));
                    (m_cachedDstInventory as MyInventory).AddItems(item.Amount, item.Content);
                }
            }
        }

        public void SetMiningStatus(DrillingMode mode, string ore)
        {
            DrillingMode = mode;
            m_targetOre = ore;
            (m_drill as IMyTerminalBlock).RefreshCustomInfo();
        }

        public void SetTarget(Vector3D target)
        {
            m_target = target;

            if (target == Vector3D.Zero)
                StopEffects();
            else
                StartEffects();
            (m_drill as IMyTerminalBlock).RefreshCustomInfo();
            //Logger.Instance.LogDebug("Setting target: " + m_target.ToString());
        }

    }

    public static class Extensions
    {
        /// <summary>
        /// Creates the objectbuilders in game, and syncs it to the server and all clients.
        /// </summary>
        /// <param name="entities"></param>
        public static void CreateAndSyncEntities(this List<VRage.ObjectBuilders.MyObjectBuilder_EntityBase> entities)
        {
            MyAPIGateway.Entities.RemapObjectBuilderCollection(entities);
            entities.ForEach(item => MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(item));
            MyAPIGateway.Multiplayer.SendEntitiesCreated(entities);
        }
    }
}

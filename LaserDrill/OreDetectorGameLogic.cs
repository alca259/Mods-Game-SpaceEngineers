using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Phoenix.LaserDrill
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OreDetector), false)]
    public class OreDetectorGameLogic : MyGameLogicComponent
    {
        const float MaximumOreScanningRange = 500;
        static bool _ControlsInited = false;
        float m_detectionRangeSquared = 150 * 150;
        //OreDetectorThread m_oreTask = new OreDetectorThread();
        private long PlayerId = 0;
        MyOreDetectorComponent m_oreComponent = new MyOreDetectorComponent();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_10TH_FRAME;
            (Container.Entity as IMyTerminalBlock).AppendingCustomInfo += OreDetectorGameLogic_AppendingCustomInfo;
            m_oreComponent.DetectionRadius = Math.Min(((Container.Entity as IMyOreDetector).GetObjectBuilderCubeBlock() as MyObjectBuilder_OreDetector).DetectionRadius, MaximumOreScanningRange);
            m_detectionRangeSquared = m_oreComponent.DetectionRadius * m_oreComponent.DetectionRadius;
            m_oreComponent.OnCheckControl += () => true;
            m_oreComponent.ReferenceEntity = Container.Entity;
            Logger.Instance.LogDebug($"Initialized Ore Detector '{Container.Entity.DisplayName}' with radius: {m_oreComponent.DetectionRadius}");
        }

        void OreDetectorGameLogic_AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            var logic = arg1.GameLogic.GetAs<OreDetectorGameLogic>();
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                var deposits = logic.AggregatedMiningInformation;
                if (arg1.IsWorking)
                {
                    if ((logic.Container.Entity as IMyTerminalBlock).GetValueFloat("Range") > MaximumOreScanningRange)
                    {
                        arg2.Append("Scanning range capped to ");
                        MyValueFormatter.AppendDistanceInBestUnit(MaximumOreScanningRange, arg2);
                        arg2.AppendLine();
                    }

                    if (logic.m_oreComponent.Scanning)
                    {
                        arg2.AppendFormat("Scanning: {0:P2}" + Environment.NewLine, logic.m_oreComponent.Complete);
                    }
                    else
                    {
                        if (deposits.Count() > 0)
                        {
                            arg2.AppendLine("Ore deposits:");

                            foreach (var deposit in deposits)
                            {
                                if ((arg1.PositionComp.GetPosition() - deposit.Location).LengthSquared() < logic.m_detectionRangeSquared)
                                {
                                    arg2.AppendFormat("{0}: {1}" + System.Environment.NewLine, deposit.Material.MinedOre, OreDetector.CalculateDepositSize(deposit.Count));
                                }
                            }
                        }
                        else
                        {
                            arg2.AppendLine("No ore detected");
                        }
                    }
                }
                else
                    arg2.Append("Ore detector offline");

                MessageUtils.SendMessageToAllPlayers(new MessageCustomInfo() { EntityId = arg1.EntityId, Text = arg2.ToString() });
            }
            else
            {
                arg2.Append(logic.CustomInfo);
            }
        }

        void OreDetectorGameLogic_OnClose(IMyEntity obj)
        {
            //NeedsUpdate = MyEntityUpdateEnum.NONE;
            //if (m_oreTask != null)
            //{
            //    m_oreTask.FreeMemory();
            //    m_oreTask = null;
            //}
            try
            {
                if (Entity != null)
                {
                    (Container.Entity as IMyCubeBlock).IsWorkingChanged -= OreDetectorGameLogic_IsWorkingChanged;
                    (Container.Entity as IMyCubeBlock).OnClose -= OreDetectorGameLogic_OnClose;
                    (Container.Entity as IMyTerminalBlock).AppendingCustomInfo -= OreDetectorGameLogic_AppendingCustomInfo;
                }
            }
            catch(Exception ex)
            {
                // Logger.Instance.LogException(ex);
            }
        }

        void OreDetectorGameLogic_IsWorkingChanged(IMyCubeBlock obj)
        {
            try
            {
                (Container.Entity as IMyTerminalBlock).RefreshCustomInfo();

                if (!obj.IsWorking)
                    m_oreComponent.Clear();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public string CustomInfo
        {
            get;
            set;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Session == null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                return;
            }

            Init();
            CreateTerminalControls();
            try
            {
                (Container.Entity as IMyCubeBlock).IsWorkingChanged += OreDetectorGameLogic_IsWorkingChanged;
                (Container.Entity as IMyCubeBlock).OnClose += OreDetectorGameLogic_OnClose;

                m_detectionRangeSquared = ((Container.Entity as IMyCubeBlock).GetObjectBuilderCubeBlock() as MyObjectBuilder_OreDetector).DetectionRadius;
                m_detectionRangeSquared *= m_detectionRangeSquared;
                if ((Container.Entity as IMyFunctionalBlock).Enabled)
                {
                    //(Container.Entity as IMyFunctionalBlock).GetActionWithName("OnOff_Off").Apply((Container.Entity as IMyFunctionalBlock));
                    //(Container.Entity as IMyFunctionalBlock).GetActionWithName("OnOff_On").Apply((Container.Entity as IMyFunctionalBlock));
                    //(Container.Entity as IMyFunctionalBlock).RequestEnable(false);
                    //(Container.Entity as IMyFunctionalBlock).RequestEnable(true);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Container.Entity.GetObjectBuilder(copy);
        }

        public void Init()
        {
            if (MyAPIGateway.Session?.Player != null)
            {
                if (MyAPIGateway.Session.Player != null)
                    PlayerId = MyAPIGateway.Session.Player.IdentityId;
            }
            else
            {
                // Try again next update
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
        }

        public override void UpdateAfterSimulation100()
        {
            try
            {
                var range = (Container.Entity as IMyTerminalBlock).GetValueFloat("Range");
                if(range != m_oreComponent.DetectionRadius)
                {
                    Logger.Instance.LogDebug($"Updating Ore Detector '{(Container.Entity as IMyTerminalBlock).DisplayNameText}' radius to: {range}");
                    m_oreComponent.DetectionRadius = Math.Min(range, MaximumOreScanningRange);      // Cap out to avoid processing delays that are too long
                    m_detectionRangeSquared = m_oreComponent.DetectionRadius * m_oreComponent.DetectionRadius;
                }
                (Container.Entity as IMyTerminalBlock).RefreshCustomInfo();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public override void UpdateBeforeSimulation100()
        {
            try
            {
                if ((Container.Entity as IMyOreDetector).IsWorking)
                    m_oreComponent.Update(Container.Entity.PositionComp.GetPosition(), false);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        private void CreateTerminalControls()
        {
            if (_ControlsInited)
                return;

            _ControlsInited = true;

            if (MyAPIGateway.Multiplayer.IsServer)
            {
                // Ore list for PB
                var orelist = MyAPIGateway.TerminalControls.CreateProperty<List<string>, IMyOreDetector>("Phoenix.Ore.DetectedOre");
                if (orelist != null)
                {
                    //orelist.Visible = (b) => true;
                    orelist.Enabled = (b) => true;
                    orelist.Getter = (b) => b.GameLogic.GetAs<OreDetectorGameLogic>()?.GetOreList();
                    orelist.Setter = (b, v) => { };
                    MyAPIGateway.TerminalControls.AddControl<IMyOreDetector>(orelist);
                }
            }
        }

        List<string> m_cachedOreList = new List<string>(20);
        private List<string> GetOreList()
        {
            var deposits = m_oreComponent.AggregatedMiningInformation;
            m_cachedOreList.Clear();

            foreach (var deposit in deposits)
            {
                //if ((Container.Entity.PositionComp.GetPosition() - deposit.Location).LengthSquared() < m_detectionRangeSquared)
                    m_cachedOreList.Add(MyAPIGateway.Session.GPS.Create(deposit.Material?.MinedOre, deposit.Count.ToString(), deposit.Location, false, true).ToString() + OreDetector.CalculateDepositSize(deposit.Count));
            }
            return m_cachedOreList;
        }

        Dictionary<Vector3D, MyVoxelMaterialDefinition> m_localOreList = new Dictionary<Vector3D, MyVoxelMaterialDefinition>();
        public Dictionary<Vector3D, MyVoxelMaterialDefinition> GetNearbyOreList()
        {
            return m_localOreList;
        }

        public ConcurrentCachingHashSet<MiningInformation> MiningInformation
        {
            get
            {
                return m_oreComponent.MiningInformation;
            }
        }

        public ConcurrentCachingList<MiningInformationPB> AggregatedMiningInformation
        {
            get
            {
                return m_oreComponent.AggregatedMiningInformation;
            }
        }
    }

}

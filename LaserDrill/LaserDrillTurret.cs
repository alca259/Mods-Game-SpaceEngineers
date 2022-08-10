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
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using VRage.Game.ModAPI;
using Sandbox.Game;
using VRage.Game;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Collections;
using VRage.Utils;
using SpaceEngineers.Game.ModAPI;

namespace Phoenix.LaserDrill
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeGatlingTurret), false, new string[] { "Large_SC_LaserDrillTurret" })]
    public class LaserDrillTurret : MyGameLogicComponent
    {
        IMyGunObject<MyGunBase> m_turret = null;
        IMyShipDrill m_baseDrill = null;
        bool m_bInit = false;

        #region Terminal Controls
        static bool _ControlsInited = false;

        static IMyTerminalControlCheckbox m_enablePriority;
        static IMyTerminalControlListbox m_selectedOre;
        static IMyTerminalControlButton m_addButton;
        static IMyTerminalControlButton m_remButton;
        static IMyTerminalControlButton m_moveUpButton;
        static IMyTerminalControlButton m_moveDownButton;
        static IMyTerminalControlListbox m_oreList;

        #region Control Values
        public bool PriorityEnabled
        {
            get { return m_priorityEnabled; }
            set { m_priorityEnabled = value; UpdateControlVisuals(); }
        }
        bool m_priorityEnabled = false;
        #endregion

        private ulong m_counter = 0;

        private ITerminalProperty<bool> m_blockShootProperty;

        private void CreateTerminalControls()
        {
            if (_ControlsInited)
                return;

            _ControlsInited = true;
            Func<IMyTerminalBlock, bool> enabledCheck = delegate (IMyTerminalBlock b) { return b.BlockDefinition.SubtypeId == "Large_SC_LaserDrillTurret"; };

            MyAPIGateway.TerminalControls.CustomControlGetter -= TerminalControls_CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter -= TerminalControls_CustomActionGetter;

            MyAPIGateway.TerminalControls.CustomControlGetter += TerminalControls_CustomControlGetter;
            MyAPIGateway.TerminalControls.CustomActionGetter += TerminalControls_CustomActionGetter;

            m_enablePriority = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyLargeGatlingTurret>("Phoenix.BD.EnableOrePriority");
            m_selectedOre = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyLargeGatlingTurret>("Phoenix.BD.OrePriorityList");
            m_addButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyLargeGatlingTurret>("Phoenix.BD.AddSelectedOre");
            m_remButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyLargeGatlingTurret>("Phoenix.BD.RemoveSelectedOre");
            m_moveUpButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyLargeGatlingTurret>("Phoenix.BD.MoveUpSelectedOre");
            m_moveDownButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyLargeGatlingTurret>("Phoenix.BD.MoveDownSelectedOre");
            m_oreList = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlListbox, IMyLargeGatlingTurret>("Phoenix.BD.OreList");

            // Separator
            var sep = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSeparator, IMyLargeGatlingTurret>(string.Empty);
            if (sep != null)
            {
                sep.Visible = enabledCheck;
                sep.Enabled = enabledCheck;
                MyAPIGateway.TerminalControls.AddControl<IMyLargeGatlingTurret>(sep);
            }

            // Ore priority checkbox
            if (m_enablePriority != null)
            {
                m_enablePriority.Title = MyStringId.GetOrCompute("Priority Mining");
                m_enablePriority.Tooltip = MyStringId.GetOrCompute("Enables setting of AI ore priorities when mining. The default is: rare ore, ice, iron.");
                m_enablePriority.Getter = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled;
                m_enablePriority.Setter = (b, v) => { if (enabledCheck(b)) MessageUtils.SendMessageToAll(new MessageToggleOrePriority() { EntityId = b.EntityId, OrePriority = v }); };
                m_enablePriority.Visible = enabledCheck;
                m_enablePriority.Enabled = enabledCheck;
                m_enablePriority.OnText = MyStringId.GetOrCompute("On");
                m_enablePriority.OffText = MyStringId.GetOrCompute("Off");
                MyAPIGateway.TerminalControls.AddControl<IMyLargeGatlingTurret>(m_enablePriority);

                var action = MyAPIGateway.TerminalControls.CreateAction<IMyLargeGatlingTurret>("Phoenix.BD.EnableOrePriority");
                if (action != null)
                {
                    StringBuilder actionname = new StringBuilder();
                    actionname.Append(m_enablePriority.Title).Append(" ").Append(m_enablePriority.OnText).Append("/").Append(m_enablePriority.OffText);

                    action.Name = actionname;
                    action.Icon = @"Textures\GUI\Icons\Actions\Toggle.dds";
                    action.ValidForGroups = true;
                    action.Action = (b) => m_enablePriority.Setter(b, !m_enablePriority.Getter(b));
                    action.Writer = (b, t) => t.Append(b.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled ? m_enablePriority.OnText : m_enablePriority.OffText);

                    MyAPIGateway.TerminalControls.AddAction<IMyLargeGatlingTurret>(action);
                }
            }

            // Ore priority list
            if (m_selectedOre != null)
            {
                m_selectedOre.ListContent = OrePriority_PopulateList;
                m_selectedOre.ItemSelected = (b, y) =>
                {
                    b.GameLogic.GetAs<LaserDrillTurret>().SelectFromOrePriority(y);
                };
                m_selectedOre.Title = MyStringId.GetOrCompute("Ore Priority");
                m_selectedOre.Tooltip = MyStringId.GetOrCompute("Determines the order in which ore will be mined (from top to bottom). The default is all non-stone ore.");
                m_selectedOre.Visible = enabledCheck;
                m_selectedOre.Enabled = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled;
                m_selectedOre.VisibleRowsCount = 6;
                MyAPIGateway.TerminalControls.AddControl<IMyLargeGatlingTurret>(m_selectedOre);
            }

            // Remove button
            if (m_remButton != null)
            {
                m_remButton.Title = MyStringId.GetOrCompute("BlockPropertyTitle_LCDScreenRemoveSelectedTextures");
                m_remButton.Action = (b) => b.GameLogic.GetAs<LaserDrillTurret>().RemoveSelectedOre();
                m_remButton.Visible = enabledCheck;
                m_remButton.Enabled = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled && b.GameLogic.GetAs<LaserDrillTurret>().CanRemoveSelectedOre();
                m_remButton.Tooltip = MyStringId.GetOrCompute("Remove selected ore from the priority list.");
                MyAPIGateway.TerminalControls.AddControl<IMyLargeGatlingTurret>(m_remButton);
            }

            // Move Up button
            if (m_moveUpButton != null)
            {
                m_moveUpButton.Title = MyStringId.GetOrCompute("BlockActionTitle_MoveWaypointUp");
                m_moveUpButton.Action = (b) => b.GameLogic.GetAs<LaserDrillTurret>().MoveSelectedOreUp();
                m_moveUpButton.Visible = enabledCheck;
                m_moveUpButton.Enabled = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled && b.GameLogic.GetAs<LaserDrillTurret>().CanMoveSelectedOreUp();
                m_moveUpButton.Tooltip = MyStringId.GetOrCompute("Move selected ore up the priority list.");
                MyAPIGateway.TerminalControls.AddControl<IMyLargeGatlingTurret>(m_moveUpButton);
            }

            // Move Down button
            if (m_moveDownButton != null)
            {
                m_moveDownButton.Title = MyStringId.GetOrCompute("BlockActionTitle_MoveWaypointDown");
                m_moveDownButton.Action = (b) => b.GameLogic.GetAs<LaserDrillTurret>().MoveSelectedOreDown();
                m_moveDownButton.Visible = enabledCheck;
                m_moveDownButton.Enabled = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled && b.GameLogic.GetAs<LaserDrillTurret>().CanMoveSelectedOreDown();
                m_moveDownButton.Tooltip = MyStringId.GetOrCompute("Move selected ore down the priority list.");
                MyAPIGateway.TerminalControls.AddControl<IMyLargeGatlingTurret>(m_moveDownButton);
            }

            // Add button
            if (m_addButton != null)
            {
                m_addButton.Title = MyStringId.GetOrCompute("BlockPropertyTitle_LCDScreenSelectTextures");
                m_addButton.Action = (b) => b.GameLogic.GetAs<LaserDrillTurret>().AddSelectedOre();
                m_addButton.Visible = enabledCheck;
                m_addButton.Enabled = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled && b.GameLogic.GetAs<LaserDrillTurret>().CanAddSelectedOre();
                m_addButton.Tooltip = MyStringId.GetOrCompute("Add selected ore to the priority list.");
                MyAPIGateway.TerminalControls.AddControl<IMyLargeGatlingTurret>(m_addButton);
            }

            // Ore list
            if (m_oreList != null)
            {
                m_oreList.ListContent = OreList_PopulateList;
                m_oreList.Title = MyStringId.GetOrCompute("Available Ore");
                m_oreList.Visible = enabledCheck;
                m_oreList.Enabled = (b) => enabledCheck(b) && b.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled;
                m_oreList.ItemSelected = (b, y) =>
                {
                    b.GameLogic.GetAs<LaserDrillTurret>().SelectFromOreList(y);
                    m_addButton.UpdateVisual();
                };
                m_oreList.VisibleRowsCount = 6;

                MyAPIGateway.TerminalControls.AddControl<IMyLargeGatlingTurret>(m_oreList);
            }
        }

        private void SendOreList()
        {
            MessageUtils.SendMessageToAll(new MessageOrePriorityList() { EntityId = Container.Entity.EntityId, OreList = m_orePriorityList });
        }

        #region Add ore
        private bool CanAddSelectedOre()
        {
            return m_selectedOreList.Count > 0;
        }

        private void AddSelectedOre()
        {
            m_orePriorityList.AddRange(m_selectedOreList);
            SendOreList();
            UpdateControlVisuals();
        }
        #endregion
        #region Remove Ore
        private bool CanRemoveSelectedOre()
        {
            return m_selectedOrePriority.Count > 0;
        }

        private void RemoveSelectedOre()
        {
            //Logger.Instance.LogMessage("Removing ore from terminal: " + m_selectedOreList[0]);
            m_selectedOrePriority.ForEach((x) => m_orePriorityList.Remove(x));
            m_selectedOrePriority.Clear();
            SendOreList();
            m_selectedOre.UpdateVisual();
            m_oreList.UpdateVisual();
            UpdateButtonVisuals();
        }
        #endregion
        #region Move Ore
        private bool CanMoveSelectedOreUp()
        {
            if (m_selectedOrePriority.Count == 0)
            {
                return false;
            }

            if (m_orePriorityList.Count == 0)
            {
                return false;
            }

            foreach (var item in m_selectedOrePriority)
            {
                int index = m_orePriorityList.IndexOf(item);
                {
                    if (CanMoveItemUp(index))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool CanMoveItemUp(int index)
        {
            if (index == -1)
            {
                return false;
            }

            for (int i = index - 1; i >= 0; i--)
            {
                if (!m_selectedOrePriority.Contains(m_orePriorityList[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void MoveSelectedOreUp()
        {
            if (m_selectedOrePriority.Count > 0)
            {
                var indexes = new List<int>(m_selectedOrePriority.Count);
                foreach (var item in m_selectedOrePriority)
                {
                    int index = m_orePriorityList.IndexOf(item);
                    if (CanMoveItemUp(index))
                    {
                        indexes.Add(index);
                    }
                }

                if (indexes.Count > 0)
                {
                    for (int i = 0; i < indexes.Count; i++)
                    {
                        SwapPositions(indexes[i] - 1, indexes[i]);
                    }
                }
            }
            SendOreList();
            m_selectedOre.UpdateVisual();
            UpdateButtonVisuals();
        }

        private bool CanMoveSelectedOreDown()
        {
            if (m_selectedOrePriority.Count == 0)
            {
                return false;
            }

            if (m_orePriorityList.Count == 0)
            {
                return false;
            }

            foreach (var item in m_selectedOrePriority)
            {
                int index = m_orePriorityList.IndexOf(item);
                {
                    if (CanMoveItemDown(index))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool CanMoveItemDown(int index)
        {
            if (index == -1)
            {
                return false;
            }

            for (int i = index + 1; i < m_orePriorityList.Count; i++)
            {
                if (!m_selectedOrePriority.Contains(m_orePriorityList[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void MoveSelectedOreDown()
        {
            if (m_selectedOrePriority.Count > 0)
            {
                var indexes = new List<int>(m_selectedOrePriority.Count);
                foreach (var item in m_selectedOrePriority)
                {
                    int index = m_orePriorityList.IndexOf(item);
                    if (CanMoveItemDown(index))
                    {
                        indexes.Add(index);
                    }
                }

                if (indexes.Count > 0)
                {
                    for (int i = indexes.Count - 1; i >= 0; i--)
                    {
                        SwapPositions(indexes[i], indexes[i] + 1);
                    }
                }
            }
            SendOreList();
            m_selectedOre.UpdateVisual();
            UpdateButtonVisuals();
        }

        public void UpdateControlVisuals()
        {
            if (_ControlsInited)
            {
                m_oreList.UpdateVisual();
                m_selectedOre.UpdateVisual();
                UpdateButtonVisuals();
            }
        }

        private void UpdateButtonVisuals()
        {
            m_addButton.UpdateVisual();
            m_remButton.UpdateVisual();
            m_moveDownButton.UpdateVisual();
            m_moveUpButton.UpdateVisual();
        }

        private void SwapPositions(int index1, int index2)
        {
            var w1 = m_orePriorityList[index1];
            var w2 = m_orePriorityList[index2];

            m_orePriorityList[index1] = w2;
            m_orePriorityList[index2] = w1;
        }

        #endregion

        List<string> m_selectedOreList = new List<string>();            // Selection in ore list box (bottom)
        List<string> m_selectedOrePriority = new List<string>();        // Selection of user chosen ores (top)

        List<string> m_orePriorityList = new List<string>();
        public List<string> PriorityList { get { return m_orePriorityList; } }

        static void OrePriority_PopulateList(Sandbox.ModAPI.IMyTerminalBlock block, List<VRage.ModAPI.MyTerminalControlListBoxItem> list, List<VRage.ModAPI.MyTerminalControlListBoxItem> selected)
        {
            block.GameLogic.GetAs<LaserDrillTurret>().m_orePriorityList.ForEach((x) => list.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(x), MyStringId.NullOrEmpty, null)));
        }

        void SelectFromOreList(List<VRage.ModAPI.MyTerminalControlListBoxItem> selection)
        {
            m_selectedOreList.Clear();
            if( selection.Count > 0)
            {
                selection.ForEach((x) => m_selectedOreList.Add(x.Text.ToString()));
            }
            UpdateButtonVisuals();
        }

        void SelectFromOrePriority(List<VRage.ModAPI.MyTerminalControlListBoxItem> selection)
        {
            m_selectedOrePriority.Clear();
            if (selection.Count > 0)
            {
                Logger.Instance.LogDebug("Selecting ore: " + selection[0].Text);
                selection.ForEach((x) => m_selectedOrePriority.Add(x.Text.ToString()));
            }
            UpdateButtonVisuals();
        }

        HashSet<string> m_oreNames = new HashSet<string>();
        static DictionaryValuesReader<string, MyVoxelMaterialDefinition> _cachedOres = MyDefinitionManager.Static.GetVoxelMaterialDefinitions();
        static void OreList_PopulateList(Sandbox.ModAPI.IMyTerminalBlock block, List<VRage.ModAPI.MyTerminalControlListBoxItem> list, List<VRage.ModAPI.MyTerminalControlListBoxItem> selected)
        {
            try
            {
                if (block.GameLogic.GetAs<LaserDrillTurret>().m_oreNames.Count == 0)
                {
                    foreach (var material in _cachedOres)
                    {
                        if (material.MinedOre != "Stone")
                            block.GameLogic.GetAs<LaserDrillTurret>().m_oreNames.Add(material.MinedOre);
                    }
                }
                block.GameLogic.GetAs<LaserDrillTurret>().m_oreNames.ToList().ForEach((x) => 
                {
                    if (!block.GameLogic.GetAs<LaserDrillTurret>().m_orePriorityList.Contains(x))
                        list.Add(new MyTerminalControlListBoxItem(MyStringId.GetOrCompute(x), MyStringId.NullOrEmpty, null));
                });
            }
            catch( Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        static void TerminalControls_CustomActionGetter(IMyTerminalBlock block, List<IMyTerminalAction> actions)
        {
            if (block is IMyLargeTurretBase)
            {
                string subtype = (block as Sandbox.ModAPI.Ingame.IMyLargeTurretBase).BlockDefinition.SubtypeId;
                var itemsToRemove = new List<IMyTerminalAction>();

                foreach (var action in actions)
                {
                    //Logger.Instance.LogDebug("Action: " + action.Id);
                    switch (subtype)
                    {
                        case "Large_SC_LaserDrillTurret":
                            if (
                                //action.Id.StartsWith("OnOff") ||
                                action.Id.StartsWith("Control") ||
                                action.Id.StartsWith("IncreaseRange") ||
                                action.Id.StartsWith("DecreaseRange") ||
                                action.Id.StartsWith("Phoenix.BD")
                                )
                                break;
                            else
                                itemsToRemove.Add(action);
                            break;
                        default:
                            if (action.Id.StartsWith("Phoenix.BD"))
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
            if (block is IMyLargeTurretBase)
            {
                string subtype = (block as Sandbox.ModAPI.Ingame.IMyLargeTurretBase).BlockDefinition.SubtypeId;

                var itemsToRemove = new List<IMyTerminalControl>();
                int separatorsToKeep = 3;

                foreach (var control in controls)
                {
                    //Logger.Instance.LogDebug("Control: " + control.Id);
                    switch (subtype)
                    {
                        case "Large_SC_LaserDrillTurret":
                            switch (control.Id)
                            {
                                //case "OnOff":
                                case "ShowInTerminal":
                                case "ShowInToolbarConfig":
                                case "Name":
                                case "ShowOnHUD":
                                case "Control":
                                case "Range":
                                    break;
                                default:
                                    if (control.Id.StartsWith("Phoenix.BD"))
                                        break;
                                    if (control is IMyTerminalControlSeparator && separatorsToKeep-- > 0)
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

        public void LoadTerminalValues()
        {
            // New way
            var settings = (m_turret as IMyLargeGatlingTurret).RetrieveTerminalValues();
            if (settings != null)
            {
                m_priorityEnabled = settings.PriorityEnabled;
                PriorityList.Clear();
                m_orePriorityList.AddRange(settings.OrePriority);
            }
        }
        #endregion

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            m_turret = Container.Entity as IMyGunObject<MyGunBase>;
            if( m_turret != null )
            {
                var block = Container.Entity as IMyFunctionalBlock;
                Container.Entity.OnClose += Entity_OnClose;
                Container.Entity.OnMarkForClose += Entity_OnMarkForClose;
                block.CubeGrid.OnBlockRemoved += CubeGrid_OnBlockRemoved;
                block.IsWorkingChanged += LaserDrillTurret_IsWorkingChanged;
                block.OnPhysicsChanged += LaserDrillTurret_OnPhysicsChanged;
                block.AppendingCustomInfo += Block_AppendingCustomInfo;
                Logger.Instance.LogDebug(m_turret.GetType().ToString());

                // Get the block directly below, and see if it's the drill
                if(!HookBaseDrill())
                {
                    if (_ControlsInited && (MyAPIGateway.CubeBuilder == null || MyAPIGateway.CubeBuilder.BlockCreationIsActivated))
                    {
                        MyAPIGateway.Utilities.ShowNotification(MissionComponent.PLACEMENT_ERROR_MESSAGE, 5000, MyFontEnum.Red);
                        MyAPIGateway.Utilities.InvokeOnGameThread(() => block.CubeGrid.RemoveBlock(block.SlimBlock));
                        return;
                    }
                }
            }
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        private void Block_AppendingCustomInfo(IMyTerminalBlock arg1, StringBuilder arg2)
        {
            arg2.AppendLine("To turn On/Off, use the '" + (m_baseDrill?.CustomName ?? m_baseDrill?.DisplayName ?? m_baseDrill?.DefinitionDisplayNameText ?? "Beam Drill") + "' block, not this turret.");

            if (m_baseDrill == null)
                arg2.AppendLine("Not attached to a base drill. This turret is not functional.");
        }

        private void LaserDrillTurret_OnPhysicsChanged(IMyEntity obj)
        {
            try
            {
                var turretsub = (m_turret as MyEntity).Subparts["GatlingTurretBase1"]?.Subparts["GatlingTurretBase2"];
                if (turretsub.Physics?.Enabled != true)
                {
                    Sandbox.Engine.Physics.MyPhysicsHelper.InitModelPhysics((m_turret as MyEntity).Subparts["GatlingTurretBase1"]);
                    Sandbox.Engine.Physics.MyPhysicsHelper.InitModelPhysics(turretsub);
                }
            }
            catch { /* ignore */ }
        }

        void LaserDrillTurret_IsWorkingChanged(IMyCubeBlock obj)
        {
            try
            {
                FixBrokenTargeting();
            }
            catch
            { /* ignore */ }
        }

        void Entity_OnMarkForClose(IMyEntity obj)
        {
            UnhookBaseDrill();
            Container.Entity.OnMarkForClose -= Entity_OnMarkForClose;
            (m_turret as IMyTerminalBlock).AppendingCustomInfo -= Block_AppendingCustomInfo;
            (m_turret as IMyTerminalBlock).CubeGrid.OnBlockRemoved -= CubeGrid_OnBlockRemoved;


            // Clear inventory
            try
            {
                var srcdrill = Container.Entity as MyEntity;
                Logger.Instance.LogAssert(srcdrill.InventoryCount == 1, "srcdrill.InventoryCount == 0");
                var inv = srcdrill.GetInventoryBase(0);

                var items = inv.GetItems();
                for (int x = 0; x < inv.GetItemsCount(); x++)
                {
                    inv.Remove(items[x], items[x].Amount);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        void Entity_OnClose(IMyEntity obj)
        {
            NeedsUpdate = MyEntityUpdateEnum.NONE;
            UnhookBaseDrill();
        }

        void m_baseDrill_EnabledChanged(IMyTerminalBlock obj)
        {
            Logger.Instance.LogDebug("Activating turret");
            FixBrokenTargeting();
            if(!(m_turret as IMyFunctionalBlock).Enabled && obj.IsWorking)
                (m_turret as IMyFunctionalBlock).ApplyAction("OnOff_On");

            if(!(m_turret as IMyLargeGatlingTurret).IsUnderControl)
                (m_turret as IMyFunctionalBlock).GetActionWithName((obj.IsWorking ? "Shoot_On" : "Shoot_Off")).Apply(m_turret as IMyFunctionalBlock);
        }

        void UnhookBaseDrill()
        {
            if (m_baseDrill != null)
                m_baseDrill.EnabledChanged -= m_baseDrill_EnabledChanged;
        }

        bool HookBaseDrill()
        {
            var block = Container.Entity as IMyCubeBlock;

            // Get the block directly below, and see if it's the drill
            var down = Base6Directions.GetFlippedDirection(block.Orientation.Up);
            var downvec = Base6Directions.GetIntVector(down);
            Vector3I drillpos = block.Position + (downvec * 1);
            var drill = (Container.Entity as IMyCubeBlock).CubeGrid.GetCubeBlock(drillpos);

            if (drill != null && drill.FatBlock != null && drill.FatBlock is IMyShipDrill)
            {
                m_baseDrill = drill.FatBlock as IMyShipDrill;
                m_baseDrill.ConnectToTurret(m_turret as IMyCubeBlock);
                Logger.Instance.LogMessage("Found base drill: " + m_baseDrill.CustomName);

                m_baseDrill.EnabledChanged += m_baseDrill_EnabledChanged;
                return true;
            }
            Logger.Instance.LogMessage("No base drill found for turret: " + (m_turret as IMyTerminalBlock).CustomName);
            return false;
        }

        void FixBrokenTargeting()
        {
            try
            {
                (m_turret as IMyLargeGatlingTurret).ApplyAction("EnableIdleMovement_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetMeteors_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetMissiles_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetSmallShips_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetLargeShips_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetCharacters_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetStations_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetNeutrals_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetFriends_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("TargetEnemies_Off");
                (m_turret as IMyLargeGatlingTurret).ApplyAction("EnableTargetLocking_Off");
            }
            catch (NullReferenceException)
            {
                // Game bug in 1.200.
            }
        }

        void CubeGrid_OnBlockRemoved(IMySlimBlock obj)
        {
            try
            {
                if (obj.FatBlock != null && obj.FatBlock == m_baseDrill)
                {
                    m_baseDrill.EnabledChanged -= m_baseDrill_EnabledChanged;
                    m_baseDrill = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogException(ex);
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (MyAPIGateway.Multiplayer == null || MyAPIGateway.Session == null)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                return;
            }

            if (m_baseDrill == null)
                HookBaseDrill();

            if (!m_bInit && m_turret != null)
            {
                m_bInit = true;
                LoadTerminalValues();
                CreateTerminalControls();
                FixBrokenTargeting();
            }
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

            // Should always be enabled
            if (!(m_turret as IMyFunctionalBlock).Enabled)
                (m_turret as IMyFunctionalBlock).Enabled = true;

            m_blockShootProperty = (m_turret as IMyTerminalBlock)?.GetProperty("Shoot").Cast<bool>();
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Container.Entity.GetObjectBuilder(copy);
        }

        private bool CalcUserTryingToShoot()
        {
            var gun = m_turret as IMyUserControllableGun;
            if (gun != null && gun.IsShooting)
                return true;
            var gunBase = m_turret as IMyGunObject<MyGunBase>;
            if (gunBase != null && gunBase.IsShooting)
                return true;
            if (m_blockShootProperty != null && m_blockShootProperty.GetValue((IMyCubeBlock)m_turret))
                return true;
            return false;
        }

        MyInventory m_cachedInventory;
        MyObjectBuilder_AmmoMagazine ammo = new MyObjectBuilder_AmmoMagazine() { SubtypeName = "SC_50mm_MAG" };
        public override void UpdateBeforeSimulation()
        {
            //MyAPIGateway.Utilities.ShowNotification("Shooting: " + ((MyObjectBuilder_UserControllableGun)(((IMyCubeBlock)m_turret).GetObjectBuilderCubeBlock())).IsShooting, 20);
            if (m_baseDrill != null && !m_baseDrill.Closed && !m_baseDrill.MarkedForClose &&
                !((IMyCubeBlock)m_turret).Closed && !((IMyCubeBlock)m_turret).MarkedForClose)
            {
                //var obj = ((MyObjectBuilder_UserControllableGun)(((IMyCubeBlock)m_turret).GetObjectBuilderCubeBlock()));
                //var shooting = obj.IsShooting;
                //var terminal = obj.IsShootingFromTerminal;
                var shooting = CalcUserTryingToShoot();
                var enabled = m_baseDrill.GameLogic.GetAs<LaserDrill>().Enabled;

                if (m_counter++ % 100 == 0)
                    (m_turret as IMyTerminalBlock).RefreshCustomInfo();

                //Logger.Instance.LogDebug($"Shooting: {shooting}");
                // We need to monitor the shooting state, and change the drill (or this) as appropriate
                //if (shooting != enabled)
                //{
                //    //MyAPIGateway.Utilities.ShowNotification("shooting: " + shooting, 50);
                //    (m_turret as IMyTerminalBlock).GetActionWithName("Shoot_" + (enabled ? "On" : "Off")).Apply((m_turret as IMyTerminalBlock));
                //}
                if ((m_turret as IMyLargeGatlingTurret).IsUnderControl && enabled != shooting)
                {
                    Logger.Instance.LogDebug("Fixing mismatched base drill, set to " + shooting);
                    //m_baseDrill.GameLogic.GetAs<LaserDrill>().Enabled = shooting;
                    (m_baseDrill as IMyTerminalBlock).GetActionWithName("OnOff_" + (shooting ? "On" : "Off")).Apply((m_baseDrill as IMyTerminalBlock));
                }

                if (m_cachedInventory == null || m_cachedInventory.GetItemAmount(ammo.GetId()) < 10)
                {
                    try
                    {
                        // Fill ammo inventory constantly
                        var item = new MyObjectBuilder_InventoryItem()
                        {
                            Amount = (MyFixedPoint)10.0,
                            Content = new MyObjectBuilder_AmmoMagazine() { SubtypeName = "SC_50mm_MAG" },
                            ItemId = 0
                        };

                        var count = (m_turret as VRage.Game.Entity.MyEntity).InventoryCount;
                        for (int i = 0; i < count; i++)
                        {
                            var inventory = (m_turret as VRage.Game.Entity.MyEntity).GetInventoryBase();

                            if ((inventory as MyInventory).CanItemsBeAdded(item.Amount, item.Content.GetId()) && inventory.GetItemsCount() == 0)
                            {
                                m_cachedInventory = inventory as MyInventory;
                                Logger.Instance.LogMessage("Adding inventory");
                                (inventory as MyInventory).AddItems(item.Amount, item.Content);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Instance.LogException(ex);
                    }
                }
            }
        }
    }
}

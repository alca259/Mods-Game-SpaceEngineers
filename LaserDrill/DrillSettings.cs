using ProtoBuf;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace Phoenix.LaserDrill
{
    /// <summary>
    /// Singleton for saving and loading all block terminal settings.
    /// </summary>
    public class BeamDrillSettings
    {
        static bool _initialized = false;
        static BeamDrillSettings _instance = new BeamDrillSettings();
        public static BeamDrillSettings Instance
        {
            get
            {
                if (!_initialized)
                    Init();
                return _instance;
            }
        }

        private BeamDrillSettings()
        {
        }

        private static void Init()
        {
            try
            {
                _instance.LoadAllTerminalValues();
                _initialized = true;
            }
            catch
            {
                // ignore
            }
        }
        List<StaticDrillSetting> m_staticSettings = new List<StaticDrillSetting>();
        List<TurretDrillSetting> m_turretSettings = new List<TurretDrillSetting>();

        // This is to save data to world file
        public void SaveAllTerminalValues()
        {
            Logger.Instance.LogDebug("SaveAllTerminalValues");
            try
            {
                var strdata = MyAPIGateway.Utilities.SerializeToXML<List<StaticDrillSetting>>(m_staticSettings);
                MyAPIGateway.Utilities.SetVariable<string>("Phoenix.BD.Static", strdata);

                strdata = MyAPIGateway.Utilities.SerializeToXML<List<TurretDrillSetting>>(m_turretSettings);
                MyAPIGateway.Utilities.SetVariable<string>("Phoenix.BD.Turret", strdata);
            }
            catch (Exception ex)
            {
                // If an old save game is loaded, it seems it might try to resave to upgrade.
                // If this happens, the ModAPI may not be initialized
                // NEVER prevent someone from saving their game.
                // It's better to lose terminal information than a player to lose hours of work.
                Logger.Instance.LogMessage("WARNING: There was an error saving terminal settings. Values may be lost.");
                Logger.Instance.LogMessage(ex.Message);
                Logger.Instance.LogMessage(ex.StackTrace);
            }
        }

        public void LoadAllTerminalValues()
        {
            Logger.Instance.LogDebug("LoadAllTerminalValues");

            string strdata;
            MyAPIGateway.Utilities.GetVariable<string>("Phoenix.BD.Static", out strdata);
            if (!string.IsNullOrEmpty(strdata))
            {
                Logger.Instance.LogDebug("Success!");
                m_staticSettings = MyAPIGateway.Utilities.SerializeFromXML<List<StaticDrillSetting>>(strdata);
            }

            MyAPIGateway.Utilities.GetVariable<string>("Phoenix.BD.Turret", out strdata);
            if (!string.IsNullOrEmpty(strdata))
            {
                Logger.Instance.LogDebug("Success!");
                m_turretSettings = MyAPIGateway.Utilities.SerializeFromXML<List<TurretDrillSetting>>(strdata);
            }
        }

        #region Static drill
        public StaticDrillSetting RetrieveTerminalValues(IMyShipDrill drill)
        {
            Logger.Instance.LogDebug("RetrieveTerminalValues");
            var settings = m_staticSettings.FirstOrDefault((x) => x.EntityId == drill.EntityId);
            if (settings != null)
            {
                Logger.Instance.LogDebug("Found existing settings for block: " + drill.CustomName);
                //drill.GameLogic.GetAs<LaserDrill>().CollectStone = settings.CollectStone;
                //drill.GameLogic.GetAs<LaserDrill>().MaxRange = settings.Range;
                //drill.GameLogic.GetAs<LaserDrill>().PrimaryBeamColor = settings.PrimaryColor;
                //drill.GameLogic.GetAs<LaserDrill>().SecondaryBeamColor = settings.SecondaryColor;
            }
            return settings;
        }

        public void StoreTerminalValues(IMyShipDrill drill)
        {
            Logger.Instance.LogDebug("StoreTerminalValues");
            var settings = m_staticSettings.FirstOrDefault((x) => x.EntityId == drill.EntityId);

            if (settings == null)
            {
                settings = new StaticDrillSetting();
                settings.EntityId = drill.EntityId;
                m_staticSettings.Add(settings);
            }

            settings.CollectStone = drill.GameLogic.GetAs<LaserDrill>().CollectStone;
            settings.Range = drill.GameLogic.GetAs<LaserDrill>().MaxRange;
            settings.PrimaryColor = drill.GameLogic.GetAs<LaserDrill>().PrimaryBeamColor;
            settings.SecondaryColor = drill.GameLogic.GetAs<LaserDrill>().SecondaryBeamColor;
        }

        public void DeleteTerminalValues(IMyShipDrill drill)
        {
            Logger.Instance.LogDebug("DeleteTerminalValues");
            var settings = m_staticSettings.FirstOrDefault((x) => x.EntityId == drill.EntityId);

            if (settings != null)
            {
                BeamDrillSettings.Instance.m_staticSettings.Remove(settings);
            }
        }
        #endregion
        #region Turret drill
        public TurretDrillSetting RetrieveTerminalValues(IMyLargeGatlingTurret drill)
        {
            Logger.Instance.LogDebug("RetrieveTerminalValues");
            var settings = m_turretSettings.FirstOrDefault((x) => x.EntityId == drill.EntityId);
            if (settings != null)
            {
                Logger.Instance.LogDebug("Found settings for block: " + drill.CustomName);
                //drill.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled = settings.PriorityEnabled;
                //drill.GameLogic.GetAs<LaserDrillTurret>().PriorityList.Clear();
                //drill.GameLogic.GetAs<LaserDrillTurret>().PriorityList.AddRange(settings.OrePriority);
            }
            return settings;
        }

        public void StoreTerminalValues(IMyLargeGatlingTurret drill)
        {
            var settings = m_turretSettings.FirstOrDefault((x) => x.EntityId == drill.EntityId);

            if (settings == null)
            {
                settings = new TurretDrillSetting();
                settings.EntityId = drill.EntityId;
                m_turretSettings.Add(settings);
            }
            settings.PriorityEnabled = drill.GameLogic.GetAs<LaserDrillTurret>().PriorityEnabled;
            settings.OrePriority = drill.GameLogic.GetAs<LaserDrillTurret>().PriorityList;
        }

        public void DeleteTerminalValues(IMyLargeGatlingTurret drill)
        {
            Logger.Instance.LogDebug("DeleteTerminalValues");
            var settings = m_turretSettings.FirstOrDefault((x) => x.EntityId == drill.EntityId);

            if (settings != null)
            {
                BeamDrillSettings.Instance.m_turretSettings.Remove(settings);
            }
        }
        #endregion
    }

    public static class DrillSettingExtensions
    {
        #region Static drill
        public static StaticDrillSetting RetrieveTerminalValues(this IMyShipDrill drill)
        {
            return BeamDrillSettings.Instance.RetrieveTerminalValues(drill);
        }

        public static void StoreTerminalValues(this IMyShipDrill drill)
        {
            BeamDrillSettings.Instance.StoreTerminalValues(drill);
        }

        public static void DeleteTerminalValues(this IMyShipDrill drill)
        {
            BeamDrillSettings.Instance.DeleteTerminalValues(drill);
        }
        #endregion
        #region Turret
        public static TurretDrillSetting RetrieveTerminalValues(this IMyLargeGatlingTurret drill)
        {
            return BeamDrillSettings.Instance.RetrieveTerminalValues(drill);
        }

        public static void StoreTerminalValues(this IMyLargeGatlingTurret drill)
        {
            BeamDrillSettings.Instance.StoreTerminalValues(drill);
        }

        public static void DeleteTerminalValues(this IMyLargeGatlingTurret drill)
        {
            BeamDrillSettings.Instance.DeleteTerminalValues(drill);
        }
        #endregion
    }

    [ProtoContract]
    public class StaticDrillSetting
    {
        [ProtoMember(1)]
        public long EntityId = 0;

        [ProtoMember(2)]
        public bool CollectStone = true;

        [ProtoMember(3)]
        public float Range = 800;

        [ProtoMember(4)]
        public Color PrimaryColor = Color.MediumBlue;

        [ProtoMember(5)]
        public Color SecondaryColor = Color.LemonChiffon;
    }

    [ProtoContract]
    public class TurretDrillSetting
    {
        [ProtoMember(1)]
        public long EntityId = 0;

        [ProtoMember(2)]
        public bool PriorityEnabled = false;

        [ProtoMember(3)]
        public List<string> OrePriority;
    }

    /// <summary>
    /// Left for legacy purposes
    /// </summary>
    [ProtoContract]
    public class DrillSettings
    {
        [ProtoMember(1)]
        public bool CollectStone = true;
    }
}

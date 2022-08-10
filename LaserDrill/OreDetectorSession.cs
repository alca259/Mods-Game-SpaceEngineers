using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Phoenix.LaserDrill
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    class OreDetectorSession : MySessionComponentBase
    {
        private OreDetector m_oreDetector = OreDetector.Instance;
        public OreDetector OreDetector { get { return m_oreDetector; } }
        protected override void UnloadData()
        {
            base.UnloadData();
            m_oreDetector = null;
            OreDetector.UnloadStatic();
        }

        public override void UpdateAfterSimulation()
        {
            base.UpdateAfterSimulation();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;

namespace Phoenix.LaserDrill
{
    public class MiningInformation
    {
        public MyVoxelMaterialDefinition Material;
        public Vector3D Location;
        public IMyVoxelBase Voxel;
        public List<Vector3D> Positions;
    }

    public class MiningInformationPB
    {
        public MyVoxelMaterialDefinition Material;
        public Vector3D Location;
        public IMyVoxelBase Voxel;
        public ulong Count;

        public MiningInformationPB(MiningInformation mi)
        {
            Material = mi.Material;
            Location = mi.Location;
            Voxel = mi.Voxel;
            Count = (ulong)mi.Positions.Count;
        }
    }
}

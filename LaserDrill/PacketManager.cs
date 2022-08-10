using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Phoenix.LaserDrill
{
    // This code was provided by rexxar
    public class PacketManager
    {
        public Vector3D Target;
        public Vector3D Origin;
        public Vector4 Color;
        private double _multiplier;
        private List<PacketItem> _packets = new List<PacketItem>();
        private double _travelDist;
        private bool _init;

        private class PacketItem
        {
            public PacketItem(Vector3D position)
            {
                Position = position;
                Ticks = 0;
            }
            public Vector3D Position;
            public int Ticks;
        }

        public PacketManager(Vector3D origin, Vector3D target, Vector4 color)
        {
            this.Target = origin;
            this.Origin = target;
            this.Color = color;
        }

        private void Init()
        {
            //sqrt is terrible, so calculate the distance once during init
            _travelDist = Vector3D.Distance(Target, Origin);
            _packets.Add(new PacketItem(Target));
            //packets move at 20 - 40m/s
            double speed = Math.Max(10, Math.Min(20, _travelDist / 3));
            _multiplier = 1 / ((_travelDist / speed) * 60);
        }

        /// <summary>
        /// Draws packets
        /// </summary>
        /// <param name="add"></param>
        /// <returns>false if no more packets</returns>
        public bool DrawPackets(bool add = true)
        {
            UpdatePackets(add);

            foreach (var packet in _packets)
            {
                MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("OreParticle"), Color, packet.Position, 1.6f, packet.Ticks);
            }
            return _packets.Count > 0;
        }

        private void UpdatePackets(bool addPackets = true)
        {
            if (!_init)
            {
                _init = true;
                Init();
            }

            List<PacketItem> toRemove = new List<PacketItem>();
            foreach (var packet in _packets)
            {
                packet.Ticks++;
                packet.Position = Vector3D.Lerp(Target, Origin, (_multiplier * packet.Ticks));

                //delete the packet once it gets to the destination
                if ((_multiplier * packet.Ticks) > 1)
                    toRemove.Add(packet);
            }

            foreach (var removePacket in toRemove)
                _packets.Remove(removePacket);

            if (addPackets)
            {
                //if the last packet to go out is more than 10m from origin, add a new one
                var lastPacket = _packets.LastOrDefault();
                if (lastPacket != null)
                {
                    if (Vector3D.DistanceSquared(lastPacket.Position, Target) > 100) //10^2
                        _packets.Add(new PacketItem(Target));
                }
            }
        }
    }
}

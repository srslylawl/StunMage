using System;

namespace STUN
{
    [Flags]
    public enum MappingProtocol : byte
    {
        Tcp = 1,
        Udp = 2,
        Both = Tcp | Udp
    }
}

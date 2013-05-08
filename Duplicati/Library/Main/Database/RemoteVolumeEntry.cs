using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Database
{
    public struct RemoteVolumeEntry : IRemoteVolume
    {
    	private readonly string m_name;
    	private readonly string m_hash;
    	private readonly long m_size;
    	private readonly RemoteVolumeType m_type;
    	private readonly RemoteVolumeState m_state;
    	
        public string Name { get { return m_name; } }
        public string Hash { get { return m_hash; } }
        public long Size { get { return m_size; } }
        public RemoteVolumeType Type { get { return m_type; } }
        public RemoteVolumeState State { get { return m_state; } }

        public RemoteVolumeEntry(string name, string hash, long size, RemoteVolumeType type, RemoteVolumeState state)
        {
            m_name = name;
            m_size = size;
            m_type = type;
            m_state = state;
            m_hash = hash;
        }
    }
}

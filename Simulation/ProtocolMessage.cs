using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IXISim.Simulation
{
    enum MessageType
    {
        Hello,
        HelloReply,
        Bye,
    }
    struct HelloData
    {
        public ulong CurrentBlock;
    }

    class ProtocolMessage
    {
        public ulong ID { get; private set; }
        public MessageType Type { get; private set; }
        public Object Data { get; private set; }

        public ProtocolMessage(MessageType type, Object data)
        {
            ID = SimulationController.Instance.GetNextID();
            Type = type;
            switch(type)
            {
                case MessageType.Hello:
                    {
                        if(!(data is HelloData))
                        {
                            throw new Exception("Message type 'Hello' expects data type 'HelloData'.");
                        }
                        Data = data;
                        break;
                    }
                case MessageType.HelloReply:
                case MessageType.Bye:
                    {
                        if(data != null)
                        {
                            throw new Exception(String.Format("Message type '{0}' should have no attached data.", type.ToString()));
                        }
                        break;
                    }
            }
        }
    }
}

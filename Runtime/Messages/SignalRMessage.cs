namespace SignalRLite.Messages
{
    public enum MessageType : int
    {
        Handshake        = 0,
        Invocation       = 1,
        StreamItem       = 2,
        Completion       = 3,
        StreamInvocation = 4,
        CancelInvocation = 5,
        Ping             = 6,
        Close            = 7,
        Ack              = 8,
        Sequence         = 9,
    }

    public class SignalRMessage
    {
        public MessageType Type;
        public string      InvocationId;
        public string      Target;
        public object[]    Arguments;
        public object      Item;
        public object      Result;
        public string      Error;
        public bool        AllowReconnect;
        public bool        NonBlocking;
        public string[]    StreamIds;
        public long        SequenceId;

        public override string ToString() =>
            $"[SignalRMessage type={Type} target={Target} invocationId={InvocationId} error={Error}]";
    }
}

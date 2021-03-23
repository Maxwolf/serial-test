namespace Serial.Server
{
    public class SerialPacket
    {
        public int Port { get; set; }

        public int BaudRate { get; set; }

        public string PortName { get; set; }

        public bool HasExecuted { get; set; }

        public string CommandText { get; set; }

        public string ResultText { get; set; }
    }
}
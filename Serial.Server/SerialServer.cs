using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace Serial.Server
{
    /// <summary>
    /// Facilitates communication over serial cable to transmit commands to sub-devices of a given client device.
    /// </summary>
    public sealed class SerialServer
    {
        private SerialPort _serialPort;
        
        private int _baudRate;
        private int _portNumber;
        private readonly Queue<string> _sentCommands = new();

        public event Action<SerialPacket> SerialError;

        public event Action<SerialPacket> SerialCommandSent;

        public event Action<SerialPacket> SerialMessage;

        public string FoundPort { get; private set; }
        public bool Connected { get => _serialPort != null && _serialPort.IsOpen; }

        public SerialPacket Connect(int serialPort, int baudRate)
        {
            string[] ports = GetPortNames();
            FoundPort = string.Empty;
            _baudRate = baudRate;
            _portNumber = serialPort;
            foreach (var item in ports)
            {
                if (item.Contains(serialPort.ToString()))
                {
                    FoundPort = item;
                    break;
                }
            }

            if (string.IsNullOrEmpty(FoundPort))
            {
                // Serial packet for when there is an error.
                var serialError = new SerialPacket()
                {
                    ResultText = "Unable to find specified port! Exiting!",
                    PortName = FoundPort,
                    Port = _portNumber,
                    BaudRate = _baudRate
                };

                SerialError?.Invoke(serialError);
                return null;
            }

            _serialPort = new SerialPort(FoundPort)
            {
                BaudRate = _baudRate,
                DiscardNull = true,
                Parity = Parity.None,
                StopBits = StopBits.One,
                DataBits = 8,
                Handshake = Handshake.None,
                RtsEnable = true,
                DtrEnable = true,
                Encoding = Encoding.UTF8,
                NewLine = "\r\n",
                ReadTimeout = 1500,
                WriteTimeout = 1500
            };

            try
            {
                _serialPort.Open();
            }
            catch (Exception err)
            {
                // Serial packet for when there is an error.
                var serialError = new SerialPacket()
                {
                    ResultText = err.Message,
                    PortName = FoundPort,
                    Port = _portNumber,
                    BaudRate = _baudRate
                };

                SerialError?.Invoke(serialError);
                return null;
            }

            // Creates packet object to return with connection info.
            var serialPacket = new SerialPacket()
            {
                BaudRate = _baudRate,
                Port = serialPort,
                PortName = FoundPort,
            };

            return serialPacket;
        }

        public void SendCommand(string command)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _sentCommands.Enqueue(command);
                _serialPort.WriteLine(command);

                // Serial packet for when a message is sent but not executed yet.
                var serialSentPacket = new SerialPacket()
                {
                    CommandText = command,
                    PortName = FoundPort,
                    Port = _portNumber,
                    BaudRate = _baudRate
                };

                SerialCommandSent?.Invoke(serialSentPacket);
            }
        }

        public void Destroy()
        {
            _serialPort.Close();
            _serialPort.Dispose();
            _serialPort = null;
        }

        public void DoEvents()
        {
            string resultRaw = string.Empty;
            if (_serialPort == null)
            {
                return;
            }

            try
            {
                resultRaw = _serialPort.ReadExisting().Trim();
            }
            catch (TimeoutException err)
            {
                // Serial packet for when there is an error.
                var serialError = new SerialPacket()
                {
                    ResultText = err.Message,
                    PortName = FoundPort,
                    Port = _portNumber,
                    BaudRate = _baudRate
                };

                SerialError?.Invoke(serialError);
                return;
            }

            // Skip if empty string.
            if (string.IsNullOrEmpty(resultRaw))
            {
                return;
            }

            // Skip if just a new line empty prompt.
            if (resultRaw.Trim() == ">>>")
            {
                return;
            }

            // Check if incoming command matches last one sent.
            if (_sentCommands.TryDequeue(out string cmd))
            {
                if (!resultRaw.Contains(cmd))
                {
                    // Serial packet for when there is an error.
                    var serialError = new SerialPacket()
                    {
                        ResultText = "Incoming command was not last one sent! Desync!",
                        PortName = FoundPort,
                        Port = _portNumber,
                        BaudRate = _baudRate
                    };

                    SerialError?.Invoke(serialError);
                    return;
                }
            }

            // Removes expected last command name and Python code prompt to leave just result.
            var resultParsed = resultRaw.Replace(cmd, string.Empty).Trim();
            resultParsed = resultParsed.Replace(">>>", string.Empty).Trim();

            // Serial packet for when result has come in for a matching source command.
            var serialResultPacket = new SerialPacket()
            {
                CommandText = cmd,
                HasExecuted = true,
                ResultText = resultParsed,
                PortName = FoundPort,
                Port = _portNumber,
                BaudRate = _baudRate
            };

            // Event for this single serial server amongst many fires.
            SerialMessage?.Invoke(serialResultPacket);
        }

        /// <summary>
        /// From https://stackoverflow.com/questions/434494/serial-port-rs232-in-mono-for-multiple-platforms
        /// </summary>
        /// <returns></returns>
        private static string[] GetPortNames()
        {
            int p = (int)Environment.OSVersion.Platform;
            List<string> serial_ports = new();

            // Are we on Unix?
            if (p == 4 || p == 128 || p == 6)
            {
                string[] ttys = System.IO.Directory.GetFiles("/dev/", "tty*");
                foreach (string dev in ttys)
                {
                    //Arduino MEGAs show up as ttyACM due to their different USB<->RS232 chips
                    if (dev.StartsWith("/dev/ttyS") || dev.StartsWith("/dev/ttyUSB") || dev.StartsWith("/dev/ttyACM"))
                    {
                        serial_ports.Add(dev);
                    }
                }
            }
            else
            {
                serial_ports.AddRange(SerialPort.GetPortNames());
            }

            return serial_ports.ToArray();
        }
    }
}
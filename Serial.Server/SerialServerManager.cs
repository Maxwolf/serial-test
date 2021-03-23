using NLog;
using System;
using System.Collections.Generic;

namespace Serial.Server
{
    public sealed class SerialServerManager
    {
        private readonly Dictionary<int, SerialServer> _serialServers = new();

        /// <summary>
        /// NLog instance should be created per class.
        /// </summary>
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public event Action<SerialPacket> SerialServerError;

        public event Action<SerialPacket> SerialServerCommandSent;

        public event Action<SerialPacket> SerialServerMessage;

        private static readonly object _serialLock = new();

        public void Restart()
        {
            Logger.Info("Serial server manager starting up...");

            lock (_serialLock)
            {
                _serialServers.Clear();
            }
        }

        /// <summary>
        /// Creates serial connection on specified port and stores the connection within internal mapping based on port number.
        /// </summary>
        /// <param name="port">Serial port to use such as /dev/ttyS0 on Linux, or COM3 on Windows.</param>
        /// <param name="baudRate">Serial communication rate, typical good range is 9600.</param>
        /// <returns></returns>
        public SerialPacket CreateSerialConnection(int port, int baudRate)
        {
            lock (_serialLock)
            {
                // Skip if the internal map already contains this port.
                if (_serialServers.ContainsKey(port))
                {
                    // Serial packet for when there is an error.
                    var serialError = new SerialPacket()
                    {
                        ResultText = $"Already created serial server on port {port}",
                        Port = port,
                        BaudRate = baudRate
                    };

                    SerialServerError?.Invoke(serialError);
                    return null;
                }

                // Connect will locate the serial device regardless of platform.
                var _serialServer = new SerialServer();
                _serialServer.SerialError += OnSerialError;
                _serialServer.SerialMessage += OnSerialMessage;
                _serialServer.SerialCommandSent += OnSerialCommandSent;

                // Actually attempts a connection to the serial port here.
                var serialPacket = _serialServer.Connect(port, baudRate);

                // Complain if something went wrong with the connection.
                if (serialPacket == null)
                {
                    // Serial packet for when there is an error.
                    var serialError = new SerialPacket()
                    {
                        ResultText = $"Error creating serial connection on port {port}",
                        Port = port,
                        BaudRate = baudRate
                    };

                    SerialServerError?.Invoke(serialError);
                    return null;
                }

                Logger.Info("Serial communication startup...");
                Logger.Info($"Using serial port: {_serialServer.FoundPort}");

                // Add serial server to internal map of them for future use.
                _serialServers.Add(port, _serialServer);

                return serialPacket;
            }
        }

        private void OnSerialError(SerialPacket serialError)
        {
            SerialServerError?.Invoke(serialError);
        }

        private void OnSerialCommandSent(SerialPacket serialSent)
        {
            SerialServerCommandSent?.Invoke(serialSent);
        }

        private void OnSerialMessage(SerialPacket serialResult)
        {
            SerialServerMessage?.Invoke(serialResult);
        }

        /// <summary>
        /// Run commands and pump events.
        /// </summary>
        public void DoEvents()
        {
            // Pump events for each serial server currently in our mapping.
            lock (_serialLock)
            {
                foreach (var serialServ in _serialServers.Values)
                {
                    if (serialServ == null)
                    {
                        return;
                    }

                    if (!serialServ.Connected)
                    {
                        return;
                    }

                    serialServ.DoEvents();
                }
            }
        }

        /// <summary>
        /// Send command to connected serial port, it must already be in internal mapping.
        /// </summary>
        public void SendCommand(SerialPacket serialCommand)
        {
            if (serialCommand == null)
            {
                return;
            }

            lock (_serialLock)
            {
                // Complain if trying to send message to unconnected serial port.
                if (!_serialServers.ContainsKey(serialCommand.Port))
                {
                    Logger.Warn($"Attempted to send message to {serialCommand.Port}, but not in the internal mapping!");
                    return;
                }

                // Ensure the serial server is not null.
                if (_serialServers[serialCommand.Port] == null)
                {
                    Logger.Warn($"Found {serialCommand.Port} in the mapping, but it's instance is null!");
                    return;
                }

                // Complain if serial port exists but is not yet connected.
                if (!_serialServers[serialCommand.Port].Connected)
                {
                    Logger.Warn($"Cannot send message! Found mapping to {serialCommand.Port}, but it's not connected yet!");
                    return;
                }

                _serialServers[serialCommand.Port].SendCommand(serialCommand.CommandText);
            }
        }

        public void Destroy()
        {
            lock (_serialLock)
            {
                foreach (var serialServ in _serialServers.Values)
                {
                    if (serialServ == null)
                    {
                        return;
                    }

                    serialServ.Destroy();
                }

                _serialServers.Clear();
            }
        }
    }
}
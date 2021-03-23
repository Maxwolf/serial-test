using CommandLine;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Serial.Server
{
    public class Program
    {
        /// <summary>
        /// NLog instance should be created per class.
        /// </summary>
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Ordered list of commands we are going to run.
        /// </summary>
        private static readonly Queue<SerialPacket> _commands = new();

        private class Options
        {
            [Option('p', "ports", Required = true, HelpText = "Serial port numbers to use")]
            public IEnumerable<int> Ports { get; set; }

            [Option('b', "baud", Default = 9600, HelpText = "Serial port baud rate.")]
            public int BaudRate { get; set; }
        }

        public static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
            .WithParsed(RunOptions)
            .WithNotParsed(HandleParseError);
        }

        private static void RunOptions(Options opts)
        {
            // Controller of multiple serial connections
            var serialManager = new SerialServerManager();
            serialManager.SerialServerError += OnSerialServerError;
            serialManager.SerialServerMessage += OnSerialServerMessage;
            serialManager.SerialServerCommandSent += OnSerialServerCommandSent;

            // Prepares for main loop for connecting devices, and queuing up commands to execute on each.
            foreach (var port in opts.Ports)
            {
                // Add new serial connection to work with.
                var serialPacket = serialManager.CreateSerialConnection(port, opts.BaudRate);

                // Complain if something went wrong with the connection.
                if (serialPacket == null)
                {
                    Logger.Error($"Unable to connect to serial port {port}. Exiting!");
                    continue;
                }

                // Resets microcontroller without making it power cycle.
                var resetPacket = new SerialPacket()
                {
                    Port = port,
                    CommandText = "machine.soft_reset()"
                };
                _commands.Enqueue(resetPacket);

                bool shouldBlink = false;
                for (int i = 0; i < 5; i++)
                {
                    shouldBlink = !shouldBlink;
                    if (shouldBlink)
                    {
                        // LED pin on
                        var ledHighPacket = new SerialPacket()
                        {
                            Port = port,
                            CommandText = "machine.Pin(25, machine.Pin.OUT).high()"
                        };
                        _commands.Enqueue(ledHighPacket);
                    }
                    else
                    {
                        // LED pin off
                        var ledLowPacket = new SerialPacket()
                        {
                            Port = port,
                            CommandText = "machine.Pin(25, machine.Pin.OUT).low()"
                        };
                        _commands.Enqueue(ledLowPacket);
                    }
                }

                // LED pin off
                var ledLowPacketAgain = new SerialPacket()
                {
                    Port = port,
                    CommandText = "machine.Pin(25, machine.Pin.OUT).low()"
                };
                _commands.Enqueue(ledLowPacketAgain);

                // Unique ID
                var uniqueIdPacket = new SerialPacket()
                {
                    Port = port,
                    CommandText = "machine.unique_id()"
                };
                _commands.Enqueue(uniqueIdPacket);

                // LED pin value
                var pinValuePacket = new SerialPacket()
                {
                    Port = port,
                    CommandText = "machine.Pin(25).value()"
                };
                _commands.Enqueue(pinValuePacket);
            }

            // Main loop of program. Pump events and send commands from queue.
            while (serialManager != null)
            {
                serialManager.DoEvents();

                if (_commands.TryDequeue(out SerialPacket cmd))
                {
                    serialManager.SendCommand(cmd);
                }

                Thread.Sleep(1000);
            }

            // Destroys every serial server instance inside it.
            serialManager?.Destroy();

            // Goodbye!
            Environment.Exit(0);
        }

        private static void OnSerialServerCommandSent(SerialPacket serialSent)
        {
            Logger.Info($"SENT[{serialSent.Port}]: {serialSent.CommandText}");
        }

        private static void OnSerialServerMessage(SerialPacket serialResult)
        {
            if (string.IsNullOrEmpty(serialResult.ResultText))
            {
                Logger.Info($"GET[{serialResult.Port}]: None");
                return;
            }

            Logger.Info($"GET[{serialResult.Port}]: {serialResult.ResultText}");
        }

        private static void OnSerialServerError(SerialPacket serialError)
        {
            Logger.Error($"ERROR[{serialError.Port}]: {serialError.ResultText}");
            Environment.Exit(1);
        }

        private static void HandleParseError(IEnumerable<Error> errs)
        {
            Environment.Exit(1);
        }
    }
}
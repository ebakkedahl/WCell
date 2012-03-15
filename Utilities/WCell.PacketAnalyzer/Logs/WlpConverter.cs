﻿using System;
using System.IO;
using System.Linq;
using NLog;
using WCell.Constants;
using WCell.Core.Network;
using WCell.Util;

namespace WCell.PacketAnalysis.Logs
{
    /// <summary>
    /// A converter for Log-files, using the format that is generated by the Wlp packet sniffer.
    /// </summary>
    public class WlpConverter
    {
        protected static Logger Log = LogManager.GetCurrentClassLogger();

        public static void Extract(string logFile, OpCodeValidator validator, Action<ParsablePacketInfo> parser)
        {
            Extract(logFile, new LogHandler(validator, parser));
        }

        /// <summary>
        /// Extracts all Packets out of the given logged and default-formatted lines
        /// </summary>
        public static void Extract(string logFile, params LogHandler[] handlers)
        {
            var file = File.Open(logFile, FileMode.Open, FileAccess.Read);
            using (var reader = new BinaryReader(file))
            {
                reader.ReadBytes(3);
                reader.ReadBytes(2);
                reader.ReadByte();
                reader.ReadInt16();
                reader.ReadBytes(4);
                reader.ReadBytes(20);
                reader.ReadBytes(64);

                while (reader.BaseStream.Position != reader.BaseStream.Length)
                {
                    var direction = reader.ReadByte() != 0xFF ? PacketSender.Client : PacketSender.Server;
                    var time = Utility.GetUTCTimeSeconds(reader.ReadUInt32());
                    var length = reader.ReadInt32();
                    var opcode = (RealmServerOpCode)(direction == PacketSender.Client
                                                        ? reader.ReadInt32()
                                                        : reader.ReadInt16());

                    var data = reader.ReadBytes(length - (direction == PacketSender.Client ? 4 : 2));

                    var opcodeHandlers = handlers.Where(handler => handler.Validator(opcode)).ToList();
                    if (opcodeHandlers.Count() <= 0)
                        continue;

                    if (!Enum.IsDefined(typeof(RealmServerOpCode), opcode))
                    {
                        Log.Warn("Packet had undefined Opcode: " + opcode);
                        continue;
                    }

                    var rawPacket = DisposableRealmPacketIn.Create(opcode, data);
                    if (rawPacket != null)
                        foreach (var handler in opcodeHandlers)
                            handler.PacketParser(new ParsablePacketInfo(rawPacket, direction, time));
                }
            }
        }

        /// <summary>
        /// Renders the given log file to the given output.
        /// </summary>
        /// <param name="logFile">The log file.</param>
        /// <param name="output">A StreamWriter or Console.Out etc</param>
        public static void ConvertLog(string logFile, TextWriter output)
        {
            var writer = new IndentTextWriter(output);
            Extract(logFile, LogConverter.DefaultValidator, info => LogConverter.ParsePacket(info, writer));
        }

        public static void ConvertLog(string logFile, string outputFile)
        {
            using (var stream = new StreamWriter(outputFile, false))
                ConvertLog(logFile, stream);
        }
    }
}
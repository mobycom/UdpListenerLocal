using System;
using System.Net;
using System.Net.Sockets;
using MobyCom.Udp;

namespace UdpListenerLocal
{
    internal class Program
    {
        private const int ListenPort = 11000;

        static void Main()
        {
            Console.Title = "MobyCom - UDP Listener Local";

            Console.WriteLine("🟢 MobyCom UDP Listener Local");
            Console.WriteLine($"📡 Listening on UDP port {ListenPort}");
            Console.WriteLine("⏳ Waiting for packets...\n");

            using var udpClient = new UdpClient(ListenPort);
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                byte[] payload = udpClient.Receive(ref remote);

                if (MobyComParser.TryParse(payload, out var packet, out var error))
                {
                    if (packet.PacketType == MobyComPacketType.Event)
                        LogEvent(remote, payload, packet);
                    else
                        LogHeartbeat(remote, payload, packet);
                }
                else
                {
                    WriteSeparator();
                    Console.WriteLine($"Packet received from {remote.Address}:{remote.Port}");
                    Console.WriteLine($"RAW: {BitConverter.ToString(payload).Replace("-", " ")}");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Invalid packet: {error}");
                    Console.ResetColor();
                }

                // ACK padrão
                udpClient.Send(new byte[] { 0x02, 0x06, 0x03 }, 3, remote);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("ACK sent");
                Console.ResetColor();
            }
        }

        // ================================
        // LOGS FORMATADOS
        // ================================
        static void LogEvent(IPEndPoint remote, byte[] payload, MobyComPacket packet)
        {
            WriteSeparator();
            Console.WriteLine($"Packet received from {remote.Address}:{remote.Port}");
            Console.WriteLine($"RAW: {BitConverter.ToString(payload).Replace("-", " ")}");

            WriteHeader("*VALID UDP EVENT*", ConsoleColor.Blue);

            WriteField("Device:", packet.DeviceId);
            WriteField("Account:", packet.Account);
            WriteField("Event:", packet.EventCode);
            WriteField("Partition:", packet.Partition);
            WriteField("Zone/User:", packet.ZoneOrUser);
            WriteField(
                "Channel:",
                packet.Channel == 0x01 ? "Ethernet" : "Cellular/LTE"
            );
        }

        static void LogHeartbeat(IPEndPoint remote, byte[] payload, MobyComPacket packet)
        {
            WriteSeparator();
            Console.WriteLine($"Packet received from {remote.Address}:{remote.Port}");
            Console.WriteLine($"RAW: {BitConverter.ToString(payload).Replace("-", " ")}");

            WriteHeader("*VALID UDP HEARTBEAT*", ConsoleColor.Green);
        }

        // ================================
        // HELPERS DE CONSOLE
        // ================================
        static void WriteSeparator()
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('#', 90));
            Console.ResetColor();
        }

        static void WriteHeader(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        static void WriteField(string label, string value)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"{label,-12}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(value);
            Console.ResetColor();
        }
    }
}

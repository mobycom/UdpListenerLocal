using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using MobyCom.Udp;

namespace UdpListenerLocal
{
    internal class Program
    {
        private const int ListenPort = 11000;

        // FIFO somente para EVENTs
        private static readonly ConcurrentQueue<MobyComPacket> _eventQueue = new();
        private const int MaxQueueSize = 500;

        // Status em memória
        private static readonly ConcurrentDictionary<string, DateTime> _lastSeen = new();
        private static readonly ConcurrentDictionary<string, bool> _deviceOnline = new();

        static void Main()
        {
            Console.Title = "MobyCom - UDP Listener Local (ACK REAL)";

            Console.WriteLine("🟢 MobyCom UDP Listener Local");
            Console.WriteLine("✅ ACK 100% fiel ao servidor real");
            Console.WriteLine($"📡 Listening on UDP port {ListenPort}");
            Console.WriteLine("⏳ Waiting for packets...\n");

            // Worker de EVENTs
            StartEventWorker();

            using var udpClient = new UdpClient(ListenPort);
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                byte[] payload = udpClient.Receive(ref remote);

                if (MobyComParser.TryParse(payload, out var packet, out var error))
                {
                    if (packet.PacketType == MobyComPacketType.Event)
                    {
                        EnqueueEvent(packet);
                        LogEvent(remote, payload, packet);
                    }
                    else if (packet.PacketType == MobyComPacketType.Heartbeat)
                    {
                        HandleHeartbeat(packet);
                        LogHeartbeat(remote, payload);
                    }
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

                // =====================================================
                // ✅ ACK MOBYCOM REAL (copiado do servidor real)
                // =====================================================
                var ack = MobyComAckBuilder.BuildAck(payload);
                udpClient.Send(ack, ack.Length, remote);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("ACK sent (MobyCom real)");
                Console.ResetColor();
            }
        }

        // =========================================================
        // FIFO EVENT
        // =========================================================
        static void EnqueueEvent(MobyComPacket packet)
        {
            _eventQueue.Enqueue(packet);

            while (_eventQueue.Count > MaxQueueSize)
            {
                _eventQueue.TryDequeue(out _);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠ FIFO overflow — oldest EVENT discarded");
                Console.ResetColor();
            }
        }

        static void StartEventWorker()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    if (_eventQueue.TryDequeue(out var packet))
                    {
                        try
                        {
                            await PersistEventAsync(packet);

                            // 🔧 FUTURO
                            // await FirebaseEventSent(packet);
                            // await WebhookEventSent(packet);
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"❌ EVENT worker error: {ex.Message}");
                            Console.ResetColor();
                        }
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
            });
        }

        // =========================================================
        // HEARTBEAT
        // =========================================================
        static void HandleHeartbeat(MobyComPacket packet)
        {
            _lastSeen[packet.DeviceId] = DateTime.UtcNow;
            _deviceOnline[packet.DeviceId] = true;
        }

        // =========================================================
        // PERSISTÊNCIA (stub — sem console)
        // =========================================================
        static Task PersistEventAsync(MobyComPacket packet)
        {
            // Aqui entra DB / Firebase / Webhook no futuro
            return Task.CompletedTask;
        }

        // =========================================================
        // LOG FORMATADO
        // =========================================================
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

        static void LogHeartbeat(IPEndPoint remote, byte[] payload)
        {
            WriteSeparator();
            Console.WriteLine($"Packet received from {remote.Address}:{remote.Port}");
            Console.WriteLine($"RAW: {BitConverter.ToString(payload).Replace("-", " ")}");

            WriteHeader("*VALID UDP HEARTBEAT*", ConsoleColor.Green);
        }

        // =========================================================
        // CONSOLE HELPERS
        // =========================================================
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

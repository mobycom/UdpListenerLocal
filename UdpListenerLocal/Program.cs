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

        // Timestamp de inicialização para filtrar eventos antigos
        private static DateTime _serverStartTime;

        static void Main()
        {
            _serverStartTime = DateTime.UtcNow;

            Console.Title = "MobyCom - UDP Listener Local (ACK REAL)";

            Console.WriteLine("MobyCom UDP Listener Local");
            Console.WriteLine("ACK 100% fiel ao servidor real");
            Console.WriteLine($"Listening on UDP port {ListenPort}");
            Console.WriteLine($"Server started at: {_serverStartTime:yyyy-MM-dd HH:mm:ss.fff} UTC");
            Console.WriteLine("Waiting for packets...\n");

            // Worker de EVENTs
            StartEventWorker();

            using var udpClient = new UdpClient(ListenPort);

            // Verificar tamanho do buffer
            int bufferSize = (int)udpClient.Client.GetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReceiveBuffer
            );
            Console.WriteLine($"Socket receive buffer size: {bufferSize:N0} bytes\n");

            // Limpar buffer do socket (descartar pacotes antigos)
            FlushSocketBuffer(udpClient);

            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                byte[] payload = udpClient.Receive(ref remote);
                DateTime receivedAt = DateTime.UtcNow;

                if (MobyComParser.TryParse(payload, out var packet, out var error))
                {
                    if (packet.PacketType == MobyComPacketType.Event)
                    {
                        // Filtro: ignorar eventos recebidos nos primeiros 3 segundos
                        TimeSpan timeSinceStartup = receivedAt - _serverStartTime;

                        if (timeSinceStartup.TotalSeconds < 3)
                        {
                            WriteSeparator();
                            Console.WriteLine($"Packet received from {remote.Address}:{remote.Port}");
                            Console.WriteLine($"RAW: {BitConverter.ToString(payload).Replace("-", " ")}");

                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"⚠️  EVENT IGNORED (startup buffer) - received {timeSinceStartup.TotalSeconds:F2}s after startup");
                            Console.ResetColor();

                            // Enviar ACK mesmo assim (device precisa confirmar recebimento)
                            var ack = MobyComAckBuilder.BuildAck(payload);
                            udpClient.Send(ack, ack.Length, remote);

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("ACK sent");
                            Console.ResetColor();
                            continue;
                        }

                        EnqueueEvent(packet);
                        LogEvent(remote, payload, packet, receivedAt);
                    }
                    else if (packet.PacketType == MobyComPacketType.Heartbeat)
                    {
                        HandleHeartbeat(packet);
                        LogHeartbeat(remote, payload, receivedAt);
                    }

                    // =====================================================
                    // ✅ ACK MOBYCOM REAL (copiado do servidor real)
                    // =====================================================
                    var ackPacket = MobyComAckBuilder.BuildAck(payload);
                    udpClient.Send(ackPacket, ackPacket.Length, remote);

                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine("ACK sent");
                    Console.ResetColor();
                }
                else
                {
                    WriteSeparator();
                    Console.WriteLine($"Packet received from {remote.Address}:{remote.Port}");
                    Console.WriteLine($"RAW: {BitConverter.ToString(payload).Replace("-", " ")}");

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Invalid packet: {error}");
                    Console.ResetColor();

                    // Tentar enviar ACK mesmo para pacotes inválidos
                    try
                    {
                        var ackInvalid = MobyComAckBuilder.BuildAck(payload);
                        udpClient.Send(ackInvalid, ackInvalid.Length, remote);

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("ACK sent ✓");
                        Console.ResetColor();
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("❌ Could not send ACK for invalid packet");
                        Console.ResetColor();
                    }
                }
            }
        }

        // =========================================================
        // FLUSH SOCKET BUFFER (Limpar pacotes antigos do SO)
        // =========================================================
        static void FlushSocketBuffer(UdpClient udpClient)
        {
            int flushedCount = 0;
            udpClient.Client.ReceiveTimeout = 100; // 100ms timeout

            try
            {
                Console.WriteLine("Flushing old packets from socket buffer...");

                while (true)
                {
                    try
                    {
                        byte[] buffer = new byte[1024];
                        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                        int bytesRead = udpClient.Client.ReceiveFrom(buffer, ref remoteEP);

                        if (bytesRead > 0)
                        {
                            flushedCount++;
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine($"   Discarded old packet #{flushedCount} ({bytesRead} bytes) from {remoteEP}");
                            Console.ResetColor();
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        // Timeout = buffer vazio
                        break;
                    }
                }
            }
            finally
            {
                udpClient.Client.ReceiveTimeout = 0; // Remove timeout (blocking mode)

                if (flushedCount > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✅ Flushed {flushedCount} old packet(s) from buffer\n");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine("Socket buffer was clean (no old packets)\n");
                }
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
        static void LogEvent(IPEndPoint remote, byte[] payload, MobyComPacket packet, DateTime receivedAt)
        {
            TimeSpan timeSinceStartup = receivedAt - _serverStartTime;

            WriteSeparator();
            Console.WriteLine($"Packet received from {remote.Address}:{remote.Port}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{receivedAt:yyyy-MM-dd HH:mm:ss.fff} UTC (+ {timeSinceStartup.TotalSeconds:F2}s since startup)");
            Console.ResetColor();
            Console.WriteLine($"RAW: {BitConverter.ToString(payload).Replace("-", " ")}");

            WriteHeader("*VALID UDP EVENT*", ConsoleColor.Green);

            WriteField("Device:", packet.DeviceId);
            WriteField("Account:", packet.Account);
            WriteField("Event:", packet.EventCode);
            WriteField("Partition:", packet.Partition);
            WriteField("Zone/User:", packet.ZoneOrUser);
            WriteField("Channel:", packet.Channel.ToString("X2"));
        }

        static void LogHeartbeat(IPEndPoint remote, byte[] payload, DateTime receivedAt)
        {
            TimeSpan timeSinceStartup = receivedAt - _serverStartTime;

            WriteSeparator();
            Console.WriteLine($"Packet received from {remote.Address}:{remote.Port}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{receivedAt:yyyy-MM-dd HH:mm:ss.fff} UTC (+ {timeSinceStartup.TotalSeconds:F2}s since startup)");
            Console.ResetColor();
            Console.WriteLine($"RAW: {BitConverter.ToString(payload).Replace("-", " ")}");

            WriteHeader("*VALID UDP HEARTBEAT*", ConsoleColor.Green);
        }

        // =========================================================
        // CONSOLE HELPERS
        // =========================================================
        static void WriteSeparator()
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('#', 100));
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
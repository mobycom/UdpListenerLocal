using System;
using System.Text;

namespace MobyCom.Udp
{
    public enum MobyComPacketType
    {
        Unknown = 0,
        Heartbeat = 1,
        Event = 2
    }

    public sealed class MobyComPacket
    {
        public MobyComPacketType PacketType { get; init; }

        public string DeviceId { get; init; } = string.Empty;
        public string Account { get; init; } = string.Empty;

        // EventCode HEX (ex: 1401, 3401)
        public string EventCode { get; init; } = string.Empty;

        public string Partition { get; init; } = string.Empty;
        public string ZoneOrUser { get; init; } = string.Empty;

        public byte Channel { get; init; }
        public byte Technology { get; init; }

        // CRC16 recebido (passivo)
        public ushort Crc16 { get; init; }

        public byte[] Raw { get; init; } = Array.Empty<byte>();
    }

    public static class MobyComParser
    {
        private const int EventLength = 30;
        private const int HeartbeatLength = 18;

        public static bool TryParse(byte[] payload, out MobyComPacket packet, out string error)
        {
            packet = null!;
            error = string.Empty;

            if (payload == null)
            {
                error = "Payload null";
                return false;
            }

            bool isHeartbeat = payload.Length == HeartbeatLength;
            bool isEvent = payload.Length == EventLength;

            if (!isHeartbeat && !isEvent)
            {
                error = $"Invalid length: {payload.Length}";
                return false;
            }

            // CRC16 = últimos 2 bytes (big-endian no fio)
            ushort crc16 = (ushort)((payload[^2] << 8) | payload[^1]);

            // ================================
            // CAMPOS COMUNS
            // ================================
            // DeviceId: bytes 4..7
            string deviceId = ReadHex(payload, 4, 4);

            // Channel / Technology
            byte channel = payload[10];
            byte technology = payload[11];

            // ================================
            // HEARTBEAT
            // ================================
            if (isHeartbeat)
            {
                packet = new MobyComPacket
                {
                    PacketType = MobyComPacketType.Heartbeat,
                    DeviceId = deviceId,
                    Channel = channel,
                    Technology = technology,
                    Crc16 = crc16,
                    Raw = payload
                };
                return true;
            }

            // ================================
            // EVENT
            // ================================
            // Account: bytes 8..9
            string account = ReadHex(payload, 8, 2);

            // EventCode: HEX, little-endian no payload
            // Ex: 01 14 => 1401
            string eventCode =
                payload[23].ToString("X2") +
                payload[22].ToString("X2");

            if (!IsValidEventCode(eventCode))
            {
                error = $"Invalid EventCode: {eventCode}";
                return false;
            }

            // Partition: byte 25
            string partition = payload[25].ToString("X2");

            // Zone/User: bytes 26..27
            string zoneOrUser =
                payload[26] == 0x00
                    ? payload[27].ToString("X2")
                    : payload[26].ToString("X2") + payload[27].ToString("X2");

            packet = new MobyComPacket
            {
                PacketType = MobyComPacketType.Event,
                DeviceId = deviceId,
                Account = account,
                EventCode = eventCode,
                Partition = partition,
                ZoneOrUser = zoneOrUser,
                Channel = channel,
                Technology = technology,
                Crc16 = crc16,
                Raw = payload
            };

            return true;
        }

        // ================================
        // HELPERS
        // ================================
        private static string ReadHex(byte[] data, int index, int length)
        {
            var sb = new StringBuilder(length * 2);
            for (int i = 0; i < length; i++)
                sb.Append(data[index + i].ToString("X2"));
            return sb.ToString();
        }

        // EVENT válido (Contact-ID)
        private static bool IsValidEventCode(string eventCode)
        {
            if (string.IsNullOrWhiteSpace(eventCode) || eventCode.Length != 4)
                return false;

            return eventCode[0] == '1'
                || eventCode[0] == '3'
                || eventCode[0] == '6';
        }
    }
}

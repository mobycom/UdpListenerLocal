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

            // Device ID: [04][05][06][07] LITTLE-ENDIAN
            // Ex no fio: 38 86 1A DA  => ID = DA1A8638
            string deviceId =
                payload[7].ToString("X2") +
                payload[6].ToString("X2") +
                payload[5].ToString("X2") +
                payload[4].ToString("X2");

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
            // ⚠️ CORRIGIDO: Account bytes [19][18] - byte [19] é MSB, [18] é LSB
            // Protocolo SIA DC-05: byte [19] contém o nibble mais significativo
            // No fio: [19]=0x23, [18]=0x45 → deve resultar em "2345"
            string account =
                payload[19].ToString("X2") +
                payload[18].ToString("X2");

            // ⚠️ CORRIGIDO: EventCode bytes [23][22] - byte [23] é MSB, [22] é LSB
            // Protocolo SIA DC-05: byte [23] contém o nibble mais significativo
            // No fio: [23]=0x11, [22]=0x30 → deve resultar em "1130" (Burglary)
            string eventCode =
                payload[23].ToString("X2") +
                payload[22].ToString("X2");

            if (!IsValidEventCode(eventCode))
            {
                error = $"Invalid EventCode: {eventCode}";
                return false;
            }

            // Partition: [25] (HEX)
            string partition = payload[25].ToString("X2");

            // Zone/User: [26][27] (BCD) — low digits em [26], high digits em [27]
            // Ex: [26]=0x23, [27]=0x00 => 023
            int zoneLow = BcdToInt(payload[26]);     // 00..99
            int zoneHigh = BcdToInt(payload[27]);    // 00..99
            int zoneUserValue = (zoneHigh * 100) + zoneLow;

            string zoneOrUser = zoneUserValue.ToString("D3");

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

        private static int BcdToInt(byte b)
        {
            int hi = (b >> 4) & 0xF;
            int lo = b & 0xF;

            if (hi > 9 || lo > 9)
                throw new ArgumentException($"Invalid BCD byte: 0x{b:X2}");

            return (hi * 10) + lo;
        }

        private static bool IsValidEventCode(string eventCode)
        {
            return !string.IsNullOrWhiteSpace(eventCode)
                   && eventCode.Length == 4
                   && (eventCode[0] == '1' || eventCode[0] == '3' || eventCode[0] == '6');
        }
    }
}
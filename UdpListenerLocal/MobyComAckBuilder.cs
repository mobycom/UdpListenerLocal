using System;

namespace MobyCom.Udp
{
    public static class MobyComAckBuilder
    {
        public static byte[] BuildAck(byte[] receivedPayload)
        {
            if (receivedPayload == null || receivedPayload.Length < 12)
                throw new ArgumentException("Invalid MobyCom payload for ACK");

            byte[] ack = new byte[18];

            // Header / Version / Type
            ack[0] = 0x21;
            ack[1] = 0x02;
            ack[2] = 0x01; // ACK

            // ⚠️ CORRIGIDO: Flags depende do TIPO de pacote recebido, não do flags original
            // O byte [11] do pacote recebido indica o comando:
            //   0x00 = Heartbeat → ACK com Flags = 0x18
            //   0x2A = Event     → ACK com Flags = 0x08
            byte command = receivedPayload[11];
            ack[3] = (command == 0x00) ? (byte)0x18 : (byte)0x08;

            // DeviceId — bytes [4..7] literal (little-endian)
            ack[4] = receivedPayload[4];
            ack[5] = receivedPayload[5];
            ack[6] = receivedPayload[6];
            ack[7] = receivedPayload[7];

            // FrameID — bytes [8..9] literal (big-endian)
            ack[8] = receivedPayload[8];
            ack[9] = receivedPayload[9];

            // Channel
            ack[10] = receivedPayload[10];

            // Direction / ACK flag
            ack[11] = 0x01;

            // Constante fixa (observada no servidor real)
            ack[12] = 0x99;
            ack[13] = 0x05;

            // Reservado
            ack[14] = 0x00;
            ack[15] = 0x00;

            // CRC16 (big-endian)
            ushort crc = Crc16CcittFalse(ack, 0, 16);
            ack[16] = (byte)(crc >> 8);
            ack[17] = (byte)(crc & 0xFF);

            return ack;
        }

        // CRC16-CCITT (FALSE) — polinômio 0x1021, init 0xFFFF
        private static ushort Crc16CcittFalse(byte[] data, int offset, int length)
        {
            const ushort poly = 0x1021;
            ushort crc = 0xFFFF;

            for (int i = offset; i < offset + length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int b = 0; b < 8; b++)
                {
                    crc = (crc & 0x8000) != 0
                        ? (ushort)((crc << 1) ^ poly)
                        : (ushort)(crc << 1);
                }
            }
            return crc;
        }
    }
}
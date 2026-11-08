using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Buds3Control
{
    internal enum NoiseMode : byte
    {
        Off = 0x01,
        Cancellation = 0x02,
        Transparency = 0x04,
        AncMax = 0x10,
        AncModerate = 0x20,
        AncMild = 0x40,
        AncSmart = 0x80
    }

    internal struct BatteryLevels
    {
        internal readonly byte? Left;
        internal readonly byte? Right;
        internal readonly byte? Case;

        internal BatteryLevels(byte? left, byte? right, byte? chargingCase)
        {
            Left = left;
            Right = right;
            Case = chargingCase;
        }
    }

    internal sealed class BudsSession : IDisposable
    {
        private readonly NetworkStream stream;
        private byte sequence;

        private BudsSession(Socket socket)
        {
            stream = new NetworkStream(socket, true);
        }

        internal static BudsSession Open()
        {
            var session = new BudsSession(Bluetooth.Connect());
            try
            {
                Thread.Sleep(300);
                byte[] capabilities = session.Request(0x0100);
                RequireSuccess(capabilities, "read the control capabilities");

                byte[] product = session.Request(0x0103);
                RequireSuccess(product, "read the device model");
                if (product.Length < 4 || product[1] != 0x14 || product[2] != 0x3C || product[3] != 0x06)
                {
                    throw new InvalidOperationException("The paired device did not identify itself as OnePlus Buds 3.");
                }
                if (capabilities.Length < 3 || (capabilities[2] & 1) == 0)
                {
                    throw new InvalidOperationException("This firmware does not advertise noise control.");
                }
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        internal NoiseMode? ReadMode()
        {
            byte[] reply = Request(0x010C, 0x01, 0x01);
            RequireSuccess(reply, "read the noise mode");
            if (reply.Length < 4 || reply.Length > 7 || reply[1] != 0x01 || reply[2] != 0x01)
            {
                throw new IOException("The earbuds sent an invalid noise mode reply.");
            }

            uint mask = 0;
            for (int i = 3; i < reply.Length; i++)
            {
                mask |= (uint)reply[i] << (8 * (i - 3));
            }
            switch (mask)
            {
                case 0x0001:
                case 0x0008:
                    return NoiseMode.Off;
                case 0x0002:
                    return NoiseMode.Cancellation;
                case 0x0010:
                    return NoiseMode.AncMax;
                case 0x0020:
                    return NoiseMode.AncModerate;
                case 0x0040:
                    return NoiseMode.AncMild;
                case 0x0080:
                    return NoiseMode.AncSmart;
                case 0x0004:
                case 0x0100:
                    return NoiseMode.Transparency;
                default:
                    return null;
            }
        }

        internal static NoiseMode? ModeGroup(NoiseMode? mode)
        {
            switch (mode)
            {
                case NoiseMode.AncMild:
                case NoiseMode.AncModerate:
                case NoiseMode.AncMax:
                case NoiseMode.AncSmart:
                    return NoiseMode.Cancellation;
                default:
                    return mode;
            }
        }

        internal BatteryLevels ReadBattery()
        {
            byte[] reply = Request(0x0106);
            if (reply.Length < 2 || reply[0] != 0 || reply.Length < 2 + reply[1] * 2)
            {
                return default(BatteryLevels);
            }

            var levels = new byte?[3];
            for (int i = 2; i < 2 + reply[1] * 2; i += 2)
            {
                int part = reply[i] - 1;
                byte level = (byte)(reply[i + 1] & 0x7F); // Bit 7 is the charging flag.
                if (part >= 0 && part < levels.Length && level >= 1 && level <= 100)
                {
                    levels[part] = level;
                }
            }
            return new BatteryLevels(levels[0], levels[1], levels[2]);
        }

        internal string SetMode(NoiseMode target, out NoiseMode? current)
        {
            byte[] reply = Request(0x0404, 0x01, 0x01, (byte)target);
            if (reply.Length == 0)
            {
                throw new IOException("The earbuds sent an empty noise mode acknowledgement.");
            }

            current = ReadMode();
            if (reply[0] == 0x0E)
            {
                return "Put at least one earbud in your ear and try again.";
            }
            if (reply[0] != 0)
            {
                return String.Format("The earbuds rejected the mode change (0x{0:X2}).", reply[0]);
            }

            for (int attempt = 0; attempt < 2 && !MatchesTarget(current, target); attempt++)
            {
                Thread.Sleep(200);
                current = ReadMode();
            }
            return MatchesTarget(current, target) ? null : "The earbuds did not confirm the requested mode.";
        }

        private static bool MatchesTarget(NoiseMode? current, NoiseMode target)
        {
            return target == NoiseMode.Cancellation ? ModeGroup(current) == target : current == target;
        }

        private byte[] Request(ushort command, params byte[] payload)
        {
            sequence = (byte)(sequence % 254 + 1);
            var request = new byte[9 + payload.Length];
            request[0] = 0xAA;
            request[1] = (byte)(7 + payload.Length);
            request[4] = (byte)command;
            request[5] = (byte)(command >> 8);
            request[6] = sequence;
            request[7] = (byte)payload.Length;
            Buffer.BlockCopy(payload, 0, request, 9, payload.Length);
            stream.Write(request, 0, request.Length);

            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < 3000)
            {
                if (ReadByte() != 0xAA) continue;

                int length = ReadLength();
                if (length < 2 || length > 4096)
                {
                    throw new IOException("The earbuds sent an invalid frame length.");
                }

                var body = new byte[length];
                for (int offset = 0; offset < length;)
                {
                    int count = stream.Read(body, offset, length - offset);
                    if (count == 0) throw new EndOfStreamException("The earbuds closed the control link.");
                    offset += count;
                }

                // The supported replies are short, whole messages. Ignore unsolicited events.
                if (body[0] != 0 || body.Length < 7 || body[4] != sequence ||
                    (body[2] | body[3] << 8) != (command | 0x8000)) continue;

                int size = body[5] | body[6] << 8;
                if (size != length - 7)
                {
                    throw new IOException("The earbuds sent an invalid reply length.");
                }
                var reply = new byte[size];
                Buffer.BlockCopy(body, 7, reply, 0, size);
                return reply;
            }
            throw new IOException("The earbuds did not reply in time.");
        }

        private int ReadLength()
        {
            int length = 0;
            for (int shift = 0; shift <= 14; shift += 7)
            {
                int digit = ReadByte();
                length |= (digit & 0x7F) << shift;
                if ((digit & 0x80) == 0) return length;
            }
            throw new IOException("The earbuds sent an invalid length prefix.");
        }

        private int ReadByte()
        {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("The earbuds closed the control link.");
            return value;
        }

        private static void RequireSuccess(byte[] reply, string action)
        {
            if (reply.Length == 0) throw new IOException("The earbuds sent an empty reply.");
            if (reply[0] != 0)
            {
                throw new InvalidOperationException(String.Format("The earbuds couldn't {0} (0x{1:X2}).", action, reply[0]));
            }
        }

        public void Dispose()
        {
            stream.Dispose();
        }
    }
}

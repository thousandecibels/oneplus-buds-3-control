using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Buds3Control
{
    internal static class Bluetooth
    {
        internal const string DeviceName = "OnePlus Buds 3";
        private const AddressFamily BluetoothFamily = (AddressFamily)32;
        private const ProtocolType Rfcomm = (ProtocolType)3;

        private static readonly Guid[] ControlServices =
        {
            new Guid("0000079A-D102-11E1-9B23-00025B00A5A5"),
            new Guid("00001107-D102-11E1-9B23-00025B00A5A5")
        };

        internal static Socket Connect()
        {
            ulong address = FindPairedBuds();
            SocketException lastError = null;
            foreach (Guid service in ControlServices)
            {
                var socket = new Socket(BluetoothFamily, SocketType.Stream, Rfcomm);
                try
                {
                    socket.ReceiveTimeout = 3000;
                    socket.SendTimeout = 3000;
                    socket.Connect(new RfcommEndpoint(address, service));
                    return socket;
                }
                catch (SocketException ex)
                {
                    socket.Dispose();
                    if (ex.SocketErrorCode == SocketError.AccessDenied)
                    {
                        throw;
                    }
                    lastError = ex;
                }
            }

            throw new InvalidOperationException(
                "Couldn't open the Buds 3 control link. Wake the earbuds and close any other earbud control app.",
                lastError);
        }

        private static ulong FindPairedBuds()
        {
            var search = new DeviceSearch
            {
                Size = Marshal.SizeOf(typeof(DeviceSearch)),
                ReturnAuthenticated = true,
                ReturnRemembered = true,
                ReturnConnected = true
            };
            var info = new DeviceInfo { Size = Marshal.SizeOf(typeof(DeviceInfo)) };
            var paired = new HashSet<ulong>();
            var connected = new HashSet<ulong>();
            IntPtr handle = BluetoothFindFirstDevice(ref search, ref info);
            int error;
            if (handle != IntPtr.Zero)
            {
                try
                {
                    do
                    {
                        if ((info.Authenticated || info.Remembered) &&
                            String.Equals(info.Name, DeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            paired.Add(info.Address);
                            if (info.Connected)
                            {
                                connected.Add(info.Address);
                            }
                        }
                    }
                    while (BluetoothFindNextDevice(handle, ref info));
                    error = Marshal.GetLastWin32Error();
                }
                finally
                {
                    BluetoothFindDeviceClose(handle);
                }
            }
            else
            {
                error = Marshal.GetLastWin32Error();
            }

            // ERROR_NO_MORE_ITEMS and ERROR_NOT_FOUND are also returned when no radio has cached devices.
            if (error != 0 && error != 259 && error != 1168)
            {
                throw new Win32Exception(error);
            }
            if (paired.Count == 0)
            {
                throw new InvalidOperationException("Pair OnePlus Buds 3 in Windows Bluetooth settings and turn Bluetooth on.");
            }
            if (connected.Count == 1)
            {
                foreach (ulong address in connected) return address;
            }
            if (paired.Count == 1)
            {
                foreach (ulong address in paired) return address;
            }
            throw new InvalidOperationException("Windows found multiple pairs of OnePlus Buds 3. Connect only the pair you want to control.");
        }

        private sealed class RfcommEndpoint : EndPoint
        {
            private readonly ulong address;
            private readonly Guid service;

            internal RfcommEndpoint(ulong address, Guid service)
            {
                this.address = address;
                this.service = service;
            }

            public override AddressFamily AddressFamily { get { return BluetoothFamily; } }

            public override SocketAddress Serialize()
            {
                // SOCKADDR_BTH is packed. Port zero asks Winsock to resolve the channel by service UUID.
                var result = new SocketAddress(BluetoothFamily, 30);
                byte[] addressBytes = BitConverter.GetBytes(address);
                byte[] serviceBytes = service.ToByteArray();
                for (int i = 0; i < addressBytes.Length; i++) result[2 + i] = addressBytes[i];
                for (int i = 0; i < serviceBytes.Length; i++) result[10 + i] = serviceBytes[i];
                return result;
            }

            public override EndPoint Create(SocketAddress socketAddress)
            {
                if (socketAddress.Family != BluetoothFamily || socketAddress.Size < 30)
                {
                    throw new ArgumentException("Invalid Bluetooth socket address.", "socketAddress");
                }
                var addressBytes = new byte[8];
                var serviceBytes = new byte[16];
                for (int i = 0; i < addressBytes.Length; i++) addressBytes[i] = socketAddress[2 + i];
                for (int i = 0; i < serviceBytes.Length; i++) serviceBytes[i] = socketAddress[10 + i];
                return new RfcommEndpoint(BitConverter.ToUInt64(addressBytes, 0), new Guid(serviceBytes));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceSearch
        {
            internal int Size;
            internal bool ReturnAuthenticated;
            internal bool ReturnRemembered;
            internal bool ReturnUnknown;
            internal bool ReturnConnected;
            internal bool IssueInquiry;
            internal byte TimeoutMultiplier;
            internal IntPtr Radio;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DeviceInfo
        {
            internal int Size;
            internal ulong Address;
            internal uint ClassOfDevice;
            internal bool Connected;
            internal bool Remembered;
            internal bool Authenticated;
            // Two native SYSTEMTIME values, each occupying 16 bytes.
            private ulong lastSeenLow, lastSeenHigh, lastUsedLow, lastUsedHigh;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            internal string Name;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstDevice(ref DeviceSearch search, ref DeviceInfo info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextDevice(IntPtr handle, ref DeviceInfo info);

        [DllImport("bthprops.cpl")]
        private static extern bool BluetoothFindDeviceClose(IntPtr handle);
    }
}

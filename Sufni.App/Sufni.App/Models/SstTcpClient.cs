using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Serilog;

namespace Sufni.App.Models;

public static class SstTcpClient
{
    private static readonly ILogger logger = Log.ForContext(typeof(SstTcpClient));

    public static async Task<byte[]> GetFile(IPEndPoint ipEndPoint, int fileId)
    {
        using Socket client = new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(ipEndPoint);

        // Get identifier as little-endian byte array
        var id = BitConverter.GetBytes(fileId);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(id);
        }

        // Request file
        await client.SendAsync(new byte[]
        {
            0x03, 0x00, 0x00, 0x00,    // 3: file request command
            id[0], id[1], id[2], id[3] // file id
        }, SocketFlags.None);

        // Receive size
        var sizeBuffer = new byte[8];
        await client.ReceiveAsync(sizeBuffer);
        var size = BitConverter.ToInt32(sizeBuffer.AsSpan()[..4]); // We won't be able to process file larger than
                                                                   // int max anyway, so this is OK.

        // Send header OK signal
        await client.SendAsync(new byte[] { 0x04, 0x00, 0x00, 0x00 });

        // Receive data
        var buffer = new byte[size];
        var totalRead = 0;
        do
        {
            var read = client.Receive(buffer, totalRead, size - totalRead, SocketFlags.None);
            if (read == 0)
            {
                logger.Warning(
                    "Server at {Endpoint} closed connection while receiving file {FileId} ({TotalRead}/{Size} bytes)",
                    ipEndPoint,
                    fileId,
                    totalRead,
                    size);
                throw new Exception("Server closed connection while receiving data.");
            }
            totalRead += read;
        } while (totalRead != size);

        // Send file received signal. Server will close connection after receiving this.
        await client.SendAsync(new byte[] { 0x05, 0x00, 0x00, 0x00 });

        return buffer;
    }

    public static async Task TrashFile(IPEndPoint ipEndPoint, int fileId)
    {
        using Socket client = new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(ipEndPoint);

        // Get identifier as little-endian byte array
        var id = BitConverter.GetBytes(-fileId);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(id);
        }

        // Request file with negative ID
        await client.SendAsync(new byte[]
        {
            0x03, 0x00, 0x00, 0x00,    // 3: file request command
            id[0], id[1], id[2], id[3] // negative file id
        }, SocketFlags.None);

        // Wait for server to acknowledge file deletion
        var statusBuffer = new byte[4];
        await client.ReceiveAsync(statusBuffer);
        var status = BitConverter.ToInt32(statusBuffer.AsSpan()[..4]);
        Debug.Assert(status == 10);

        // Send file received signal. Server will close connection after receiving this.
        await client.SendAsync(new byte[] { 0x05, 0x00, 0x00, 0x00 });
    }
}
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PAudio.Windows.App.Logging;

namespace P2PAudio.Windows.App.Services;

public sealed class MdnsUdpReceiverDiscoveryService : IUdpReceiverDiscoveryService
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private static readonly TimeSpan ScanWindow = TimeSpan.FromSeconds(3);
    private const int MdnsPort = 5353;
    private const int QueryRepeatCount = 2;
    private const int QueryRepeatDelayMs = 750;
    private const ushort QueryTypePtr = 12;
    private const ushort QueryClassIn = 1;
    private const ushort QueryClassUnicastResponseMask = 0x8000;
    private const ushort RecordTypeA = 1;
    private const ushort RecordTypePtr = 12;
    private const ushort RecordTypeTxt = 16;
    private const ushort RecordTypeAaaa = 28;
    private const ushort RecordTypeSrv = 33;
    private const string ServiceType = "_p2paudio-udp._udp.local.";

    public async Task<IReadOnlyList<UdpReceiverEndpoint>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var instances = new Dictionary<string, ServiceInstance>(StringComparer.OrdinalIgnoreCase);
        var hostAddresses = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var query = BuildQueryPacket();
        var multicastEndpoint = new IPEndPoint(MulticastAddress, MdnsPort);

        using var udpClient = CreateDiscoveryClient();
        udpClient.MulticastLoopback = false;

        for (var attempt = 0; attempt < QueryRepeatCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await udpClient.SendAsync(query, query.Length, multicastEndpoint);
            if (attempt + 1 < QueryRepeatCount)
            {
                await Task.Delay(QueryRepeatDelayMs, cancellationToken);
            }
        }

        var deadlineUtc = DateTime.UtcNow + ScanWindow;
        while (DateTime.UtcNow < deadlineUtc)
        {
            var remaining = deadlineUtc - DateTime.UtcNow;
            using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveCts.CancelAfter(remaining);

            try
            {
                var response = await udpClient.ReceiveAsync(receiveCts.Token);
                ParseResponse(
                    response.Buffer,
                    response.RemoteEndPoint.Address,
                    instances,
                    hostAddresses
                );
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return instances.Values
            .Where(instance => instance.Port > 0)
            .Where(instance => MatchesTxt(instance.TxtProperties, "role", "listener"))
            .Where(instance => MatchesTxt(instance.TxtProperties, "codec", "opus"))
            .Select(instance => ToEndpoint(instance, hostAddresses))
            .Where(endpoint => endpoint is not null)
            .Select(endpoint => endpoint!)
            .GroupBy(endpoint => $"{endpoint.Host}:{endpoint.Port}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(endpoint => endpoint.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesTxt(IReadOnlyDictionary<string, string?> properties, string key, string expectedValue)
    {
        return !properties.TryGetValue(key, out var value) ||
               string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static UdpReceiverEndpoint? ToEndpoint(
        ServiceInstance instance,
        IReadOnlyDictionary<string, HashSet<string>> hostAddresses)
    {
        var host = string.Empty;
        if (!string.IsNullOrWhiteSpace(instance.TargetHost) &&
            hostAddresses.TryGetValue(instance.TargetHost, out var addresses))
        {
            host = addresses.FirstOrDefault(address => !string.IsNullOrWhiteSpace(address)) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            host = instance.SourceAddresses.FirstOrDefault() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(instance.DisplayName)
            ? host
            : instance.DisplayName;
        return new UdpReceiverEndpoint(
            DisplayName: displayName,
            ServiceName: instance.ServiceName,
            Host: host,
            Port: instance.Port
        );
    }

    private static void ParseResponse(
        byte[] message,
        IPAddress sourceAddress,
        IDictionary<string, ServiceInstance> instances,
        IDictionary<string, HashSet<string>> hostAddresses)
    {
        try
        {
            if (message.Length < 12)
            {
                return;
            }

            var questionCount = ReadUInt16(message, 4);
            var answerCount = ReadUInt16(message, 6);
            var authorityCount = ReadUInt16(message, 8);
            var additionalCount = ReadUInt16(message, 10);
            var offset = 12;

            for (var i = 0; i < questionCount; i++)
            {
                _ = ReadName(message, ref offset);
                offset += 4;
            }

            var totalRecords = answerCount + authorityCount + additionalCount;
            for (var i = 0; i < totalRecords; i++)
            {
                var name = NormalizeDnsName(ReadName(message, ref offset));
                var type = ReadUInt16(message, offset);
                var recordClass = ReadUInt16(message, offset + 2);
                _ = recordClass;
                offset += 4;
                offset += 4; // TTL
                var dataLength = ReadUInt16(message, offset);
                offset += 2;
                var dataOffset = offset;
                offset += dataLength;

                if (offset > message.Length)
                {
                    return;
                }

                switch (type)
                {
                    case RecordTypePtr:
                    {
                        var ptrOffset = dataOffset;
                        var instanceName = NormalizeDnsName(ReadName(message, ref ptrOffset));
                        if (!string.Equals(name, ServiceType, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }

                        var instance = GetOrCreateInstance(instances, instanceName);
                        instance.ServiceName = instanceName;
                        instance.DisplayName = DeriveDisplayName(instanceName);
                        instance.SourceAddresses.Add(sourceAddress.ToString());
                        break;
                    }

                    case RecordTypeSrv:
                    {
                        var srvOffset = dataOffset;
                        srvOffset += 4; // priority + weight
                        var port = ReadUInt16(message, srvOffset);
                        srvOffset += 2;
                        var targetHost = NormalizeDnsName(ReadName(message, ref srvOffset));

                        var instance = GetOrCreateInstance(instances, name);
                        instance.ServiceName = name;
                        if (string.IsNullOrWhiteSpace(instance.DisplayName))
                        {
                            instance.DisplayName = DeriveDisplayName(name);
                        }
                        instance.Port = port;
                        instance.TargetHost = targetHost;
                        instance.SourceAddresses.Add(sourceAddress.ToString());
                        break;
                    }

                    case RecordTypeTxt:
                    {
                        var instance = GetOrCreateInstance(instances, name);
                        instance.ServiceName = name;
                        if (string.IsNullOrWhiteSpace(instance.DisplayName))
                        {
                            instance.DisplayName = DeriveDisplayName(name);
                        }

                        var txtOffset = dataOffset;
                        var txtLimit = dataOffset + dataLength;
                        while (txtOffset < txtLimit)
                        {
                            var partLength = message[txtOffset++];
                            if (partLength == 0 || txtOffset + partLength > txtLimit)
                            {
                                break;
                            }

                            var part = Encoding.UTF8.GetString(message, txtOffset, partLength);
                            txtOffset += partLength;
                            var separatorIndex = part.IndexOf('=');
                            if (separatorIndex < 0)
                            {
                                instance.TxtProperties[part] = null;
                            }
                            else
                            {
                                instance.TxtProperties[part[..separatorIndex]] = part[(separatorIndex + 1)..];
                            }
                        }
                        break;
                    }

                    case RecordTypeA:
                    {
                        if (dataLength != 4)
                        {
                            break;
                        }

                        var address = new IPAddress(message.AsSpan(dataOffset, 4)).ToString();
                        AddAddress(hostAddresses, name, address);
                        break;
                    }

                    case RecordTypeAaaa:
                    {
                        if (dataLength != 16)
                        {
                            break;
                        }

                        var address = new IPAddress(message.AsSpan(dataOffset, 16)).ToString();
                        AddAddress(hostAddresses, name, address);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.W(
                "MdnsUdpReceiverDiscoveryService",
                "mdns_parse_failed",
                "Failed to parse mDNS discovery response",
                new Dictionary<string, object?>
                {
                    ["message"] = ex.Message,
                    ["source"] = sourceAddress.ToString()
                }
            );
        }
    }

    private static void AddAddress(
        IDictionary<string, HashSet<string>> hostAddresses,
        string hostName,
        string address)
    {
        if (!hostAddresses.TryGetValue(hostName, out var addresses))
        {
            addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            hostAddresses[hostName] = addresses;
        }

        addresses.Add(address);
    }

    private static ServiceInstance GetOrCreateInstance(
        IDictionary<string, ServiceInstance> instances,
        string instanceName)
    {
        if (!instances.TryGetValue(instanceName, out var instance))
        {
            instance = new ServiceInstance(instanceName);
            instances[instanceName] = instance;
        }

        return instance;
    }

    private static byte[] BuildQueryPacket()
    {
        var header = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4, 2), 1);
        var bytes = new List<byte>(64);
        bytes.AddRange(header);
        WriteName(bytes, ServiceType);
        WriteUInt16(bytes, QueryTypePtr);
        WriteUInt16(bytes, (ushort)(QueryClassIn | QueryClassUnicastResponseMask));
        return [.. bytes];
    }

    private static UdpClient CreateDiscoveryClient()
    {
        try
        {
            return CreateMulticastDiscoveryClient();
        }
        catch (SocketException ex)
        {
            AppLogger.I(
                "MdnsUdpReceiverDiscoveryService",
                "mdns_multicast_bind_failed",
                "Falling back to unicast mDNS responses",
                new Dictionary<string, object?>
                {
                    ["message"] = ex.Message
                }
            );
            return CreateUnicastFallbackClient();
        }
    }

    private static UdpClient CreateMulticastDiscoveryClient()
    {
        var udpClient = new UdpClient(AddressFamily.InterNetwork)
        {
            ExclusiveAddressUse = false
        };
        try
        {
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
            udpClient.JoinMulticastGroup(MulticastAddress);
            return udpClient;
        }
        catch
        {
            udpClient.Dispose();
            throw;
        }
    }

    private static UdpClient CreateUnicastFallbackClient()
    {
        var udpClient = new UdpClient(AddressFamily.InterNetwork);
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        return udpClient;
    }

    private static void WriteName(ICollection<byte> bytes, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            bytes.Add((byte)label.Length);
            foreach (var value in Encoding.ASCII.GetBytes(label))
            {
                bytes.Add(value);
            }
        }

        bytes.Add(0);
    }

    private static void WriteUInt16(ICollection<byte> bytes, ushort value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value & 0xFF));
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset, 2));
    }

    private static string ReadName(byte[] message, ref int offset)
    {
        var labels = new List<string>();
        var jumped = false;
        var nextOffset = offset;
        var safety = 0;

        while (nextOffset < message.Length && safety++ < 128)
        {
            var length = message[nextOffset];
            if (length == 0)
            {
                nextOffset++;
                if (!jumped)
                {
                    offset = nextOffset;
                }
                break;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (nextOffset + 1 >= message.Length)
                {
                    throw new InvalidOperationException("Invalid DNS compression pointer.");
                }

                var pointer = ((length & 0x3F) << 8) | message[nextOffset + 1];
                if (!jumped)
                {
                    offset = nextOffset + 2;
                }
                nextOffset = pointer;
                jumped = true;
                continue;
            }

            nextOffset++;
            if (nextOffset + length > message.Length)
            {
                throw new InvalidOperationException("Invalid DNS label length.");
            }

            labels.Add(Encoding.ASCII.GetString(message, nextOffset, length));
            nextOffset += length;
            if (!jumped)
            {
                offset = nextOffset;
            }
        }

        return labels.Count == 0
            ? "."
            : $"{string.Join(".", labels)}.";
    }

    private static string NormalizeDnsName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.EndsWith(".", StringComparison.Ordinal) ? value : $"{value}.";
    }

    private static string DeriveDisplayName(string serviceName)
    {
        var normalized = NormalizeDnsName(serviceName);
        if (normalized.EndsWith(ServiceType, StringComparison.OrdinalIgnoreCase))
        {
            return normalized[..^ServiceType.Length].TrimEnd('.');
        }

        return normalized.TrimEnd('.');
    }

    private sealed class ServiceInstance
    {
        public ServiceInstance(string serviceName)
        {
            ServiceName = serviceName;
        }

        public string DisplayName { get; set; } = string.Empty;

        public string ServiceName { get; set; }

        public string TargetHost { get; set; } = string.Empty;

        public int Port { get; set; }

        public Dictionary<string, string?> TxtProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> SourceAddresses { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

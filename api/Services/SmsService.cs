using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using api.Services.Interfaces;
using api.Services.Models;
using HeboTech.ATLib.Messaging;
using HeboTech.ATLib.Numbering;
using Microsoft.Extensions.Options;

namespace api.Services;

public sealed class SmsService(IOptions<SmsServiceOptions> options, ILogger<SmsService> logger) : ISmsService
{
    private static readonly Regex CmgLineRegex = new("\\+CMGS:\\s*(\\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CmgrPduHeaderRegex = new("\\+CMGR:\\s*(?<stat>\\d+),[^,]*,(?<len>\\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CmgdCountRegex = new("\\+CMGD:\\s*(\\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CmglPduHeaderRegex = new("\\+CMGL:\\s*(?<index>\\d+),(?<stat>\\d+),[^,]*,(?<len>\\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SmsServiceOptions _options = options.Value;
    private readonly SemaphoreSlim _portLock = new(1, 1);

    // Populated by the most recent ListMessagesAsync call so DeleteMessageAsync can remove every
    // SIM slot belonging to a reassembled multi-part (concatenated) message, not just the first part.
    private Dictionary<int, int[]> _lastIndexGroups = new();
    private byte _concatReference;

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAtCommandAsync("AT", cancellationToken);
        return response.Any(x => x.Equals("OK", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SmsDeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
    {
        var manufacturer = (await ExecuteAtCommandAsync("AT+CGMI", cancellationToken)).FirstOrDefault(x => !IsTerminalResponse(x)) ?? string.Empty;
        var model = (await ExecuteAtCommandAsync("AT+CGMM", cancellationToken)).FirstOrDefault(x => !IsTerminalResponse(x)) ?? string.Empty;
        var revision = (await ExecuteAtCommandAsync("AT+CGMR", cancellationToken)).FirstOrDefault(x => !IsTerminalResponse(x)) ?? string.Empty;
        var imei = (await ExecuteAtCommandAsync("AT+CGSN", cancellationToken)).FirstOrDefault(x => !IsTerminalResponse(x)) ?? string.Empty;
        var imsi = (await ExecuteAtCommandAsync("AT+CIMI", cancellationToken)).FirstOrDefault(x => !IsTerminalResponse(x)) ?? string.Empty;

        string? phoneNumber = null;
        try
        {
            var cnumResponse = await ExecuteAtCommandAsync("AT+CNUM", cancellationToken);
            phoneNumber = ParsePhoneNumberFromCnum(cnumResponse);
        }
        catch (InvalidOperationException)
        {
            logger.LogDebug("AT+CNUM not supported or no phone number available on this SIM/modem.");
        }

        int? signalQuality = null;
        var csqResponse = await ExecuteAtCommandAsync("AT+CSQ", cancellationToken);
        var csq = csqResponse.FirstOrDefault(x => x.StartsWith("+CSQ:", StringComparison.OrdinalIgnoreCase));
        if (csq is not null)
        {
            var part = csq.Split(':', 2).ElementAtOrDefault(1)?.Trim();
            var rssiText = part?.Split(',', 2).FirstOrDefault()?.Trim();
            signalQuality = int.TryParse(rssiText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rssi) ? rssi : null;
        }

        var cregResponse = await ExecuteAtCommandAsync("AT+CREG?", cancellationToken);
        var networkRegistration = cregResponse.FirstOrDefault(x => x.StartsWith("+CREG:", StringComparison.OrdinalIgnoreCase));

        var cscaResponse = await ExecuteAtCommandAsync("AT+CSCA?", cancellationToken);
        var smsCenterNumber = cscaResponse.FirstOrDefault(x => x.StartsWith("+CSCA:", StringComparison.OrdinalIgnoreCase));

        string? operatorSelection = null;
        string? operatorName = null;
        try
        {
            var copsResponse = await ExecuteAtCommandAsync("AT+COPS?", cancellationToken);
            var copsLine = copsResponse.FirstOrDefault(x => x.StartsWith("+COPS:", StringComparison.OrdinalIgnoreCase));
            operatorSelection = copsLine;
            if (copsLine is not null)
            {
                var m = Regex.Match(copsLine, "\\+COPS:\\s*\\d+,\\d+,\"(?<name>[^\"]+)\"", RegexOptions.CultureInvariant);
                if (m.Success)
                {
                    operatorName = m.Groups["name"].Value;
                }
            }
        }
        catch (InvalidOperationException)
        {
            logger.LogDebug("AT+COPS? not supported by this modem/network.");
        }

        string? simIccid = null;
        try
        {
            var iccidResponse = await ExecuteAtCommandAsync("AT+CCID", cancellationToken);
            var ccidLine = iccidResponse.FirstOrDefault(x => x.StartsWith("+CCID", StringComparison.OrdinalIgnoreCase));
            if (ccidLine is not null)
            {
                var parts = ccidLine.Split(':', 2);
                simIccid = parts.Length == 2 ? parts[1].Trim() : null;
            }
        }
        catch (InvalidOperationException)
        {
            logger.LogDebug("AT+CCID not supported by this modem/SIM.");
        }

        return new SmsDeviceInfo
        {
            Manufacturer = manufacturer,
            Model = model,
            Revision = revision,
            Imei = imei,
            Imsi = imsi,
            PhoneNumber = phoneNumber,
            SignalQuality = signalQuality,
            NetworkRegistration = networkRegistration,
            SmsCenterNumber = smsCenterNumber,
            OperatorSelection = operatorSelection,
            OperatorName = operatorName,
            SimIccid = simIccid
        };
    }

    private static string? ParsePhoneNumberFromCnum(IReadOnlyList<string> response)
    {
        var cnumLine = response.FirstOrDefault(x => x.StartsWith("+CNUM:", StringComparison.OrdinalIgnoreCase));
        if (cnumLine is null)
        {
            return null;
        }

        var match = Regex.Match(cnumLine, "\\+CNUM:\\s*\"[^\"]*\",\"(?<number>[^\"]*)\"", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var number = match.Groups["number"].Value.Trim();
        return string.IsNullOrWhiteSpace(number) ? null : number;
    }

    public async Task<int?> GetSignalQualityAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAtCommandAsync("AT+CSQ", cancellationToken);
        var csq = response.FirstOrDefault(x => x.StartsWith("+CSQ:", StringComparison.OrdinalIgnoreCase));
        if (csq is null)
        {
            return null;
        }

        var part = csq.Split(':', 2).ElementAtOrDefault(1)?.Trim();
        var rssiText = part?.Split(',', 2).FirstOrDefault()?.Trim();
        return int.TryParse(rssiText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rssi) ? rssi : null;
    }

    public async Task<string?> GetNetworkRegistrationAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAtCommandAsync("AT+CREG?", cancellationToken);
        return response.FirstOrDefault(x => x.StartsWith("+CREG:", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<SmsSendResult> SendSmsAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        return await SendSmsToModemAsync(to, message, cancellationToken);
    }

    public async Task<SmsSendResult> SendSmsToModemAsync(string to, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to))
        {
            throw new ArgumentException("Recipient number cannot be empty.", nameof(to));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message cannot be empty.", nameof(message));
        }

        await _portLock.WaitAsync(cancellationToken);
        try
        {
            using var serialPort = CreatePort();
            serialPort.Open();

            await EnsurePduModeAsync(serialPort, cancellationToken);

            var phoneNumber = PhoneNumberFactory.CreateCommonIsdn(to.Trim());
            var submitRequest = new SmsSubmitRequest(phoneNumber, message, CharacterSet.UCS2)
            {
                MessageReferenceNumber = _concatReference++
            };
            var pdus = SmsSubmitEncoder.Encode(submitRequest, includeEmptySmscLength: false).ToList();

            int? modemReference = null;
            var allLines = new List<string>();

            foreach (var hex in pdus)
            {
                await WriteLineAsync(serialPort, $"AT+CMGS={hex.Length / 2}", cancellationToken);
                await WaitForPromptAsync(serialPort, cancellationToken);

                var pduBytes = Encoding.ASCII.GetBytes(hex);
                serialPort.Write(pduBytes, 0, pduBytes.Length);
                serialPort.Write([26], 0, 1);

                var lines = await ReadUntilTerminalAsync(serialPort, cancellationToken);
                EnsureNoError(lines);
                allLines.AddRange(lines);

                var cmgsLine = lines.FirstOrDefault(x => x.StartsWith("+CMGS:", StringComparison.OrdinalIgnoreCase));
                if (cmgsLine is not null)
                {
                    var match = CmgLineRegex.Match(cmgsLine);
                    if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    {
                        modemReference ??= id;
                    }
                }
            }

            return new SmsSendResult
            {
                MessageId = Guid.NewGuid(),
                Status = "Sent",
                QueuedAt = DateTime.Now,
                ModemReference = modemReference,
                ModemResponse = allLines
            };
        }
        finally
        {
            _portLock.Release();
        }
    }

    public async Task<IReadOnlyList<SmsMessage>> ListMessagesAsync(SmsMessageStatus status = SmsMessageStatus.All, CancellationToken cancellationToken = default)
    {
        await _portLock.WaitAsync(cancellationToken);
        try
        {
            using var serialPort = CreatePort();
            serialPort.Open();
            await EnsurePduModeAsync(serialPort, cancellationToken);

            var statusArg = status switch
            {
                SmsMessageStatus.ReceivedUnread => "0",
                SmsMessageStatus.ReceivedRead => "1",
                SmsMessageStatus.StoredUnsent => "2",
                SmsMessageStatus.StoredSent => "3",
                _ => "4"
            };

            var lines = await ExecuteAtCommandWithOpenPortAsync(serialPort, $"AT+CMGL={statusArg}", cancellationToken);
            var raw = ParseCmglPdu(lines);
            return ReassembleMessages(raw);
        }
        finally
        {
            _portLock.Release();
        }
    }

    public async Task<SmsMessage?> ReadMessageAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        await _portLock.WaitAsync(cancellationToken);
        try
        {
            using var serialPort = CreatePort();
            serialPort.Open();
            await EnsurePduModeAsync(serialPort, cancellationToken);

            var lines = await ExecuteAtCommandWithOpenPortAsync(serialPort, $"AT+CMGR={index}", cancellationToken);
            return ParseCmgrPdu(lines, index);
        }
        finally
        {
            _portLock.Release();
        }
    }

    public async Task<IReadOnlyList<SmsMessage>> ReadUnreadMessagesAsync(CancellationToken cancellationToken = default)
    {
        return await ListMessagesAsync(SmsMessageStatus.ReceivedUnread, cancellationToken);
    }

    public async Task DeleteMessageAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var indices = _lastIndexGroups.TryGetValue(index, out var group) ? group : [index];
        foreach (var i in indices)
        {
            var response = await ExecuteAtCommandAsync($"AT+CMGD={i}", cancellationToken);
            EnsureNoError(response);
        }
    }

    public async Task<int> DeleteAllMessagesAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAtCommandAsync("AT+CMGD=1,4", cancellationToken);
        EnsureNoError(response);
        _lastIndexGroups = new Dictionary<int, int[]>();

        var countLine = response.FirstOrDefault(x => x.StartsWith("+CMGD:", StringComparison.OrdinalIgnoreCase));
        if (countLine is null)
        {
            return 0;
        }

        var match = CmgdCountRegex.Match(countLine);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
    }

    public async Task<string?> GetSmsCenterNumberAsync(CancellationToken cancellationToken = default)
    {
        var response = await ExecuteAtCommandAsync("AT+CSCA?", cancellationToken);
        return response.FirstOrDefault(x => x.StartsWith("+CSCA:", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<string>> ExecuteRawCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command cannot be empty.", nameof(command));
        }

        return await ExecuteAtCommandAsync(command, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> ExecuteAtCommandAsync(string command, CancellationToken cancellationToken)
    {
        await _portLock.WaitAsync(cancellationToken);
        try
        {
            using var serialPort = CreatePort();
            serialPort.Open();
            return await ExecuteAtCommandWithOpenPortAsync(serialPort, command, cancellationToken);
        }
        finally
        {
            _portLock.Release();
        }
    }

    private async Task<IReadOnlyList<string>> ExecuteAtCommandWithOpenPortAsync(SerialPort serialPort, string command, CancellationToken cancellationToken)
    {
        await WriteLineAsync(serialPort, command, cancellationToken);
        var lines = await ReadUntilTerminalAsync(serialPort, cancellationToken);
        EnsureNoError(lines);
        return lines;
    }

    private static async Task WriteLineAsync(SerialPort port, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => port.Write(value + "\r"), cancellationToken);
    }

    private async Task EnsurePduModeAsync(SerialPort serialPort, CancellationToken cancellationToken)
    {
        await ExecuteAtCommandWithOpenPortAsync(serialPort, "ATE0", cancellationToken);
        await ExecuteAtCommandWithOpenPortAsync(serialPort, "AT+CMEE=2", cancellationToken);
        await ExecuteAtCommandWithOpenPortAsync(serialPort, "AT+CMGF=0", cancellationToken);
        await ExecuteAtCommandWithOpenPortAsync(serialPort, "AT+CPMS=\"SM\",\"SM\",\"SM\"", cancellationToken);
    }

    /// <summary>
    /// Actively scans for the literal '&gt;' data-entry prompt that AT+CMGS emits before it will accept
    /// the PDU/message body. Different modem firmwares emit this prompt after very different delays, so
    /// a fixed blind wait (the previous approach) was a reliability landmine; scanning for the real byte
    /// makes sending robust across modem models.
    /// </summary>
    private async Task WaitForPromptAsync(SerialPort serialPort, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(_options.CommandTimeoutMs);
        var started = DateTime.UtcNow;
        var buffer = new StringBuilder();

        while (DateTime.UtcNow - started < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = await Task.Run(serialPort.ReadExisting, cancellationToken);
            if (!string.IsNullOrEmpty(chunk))
            {
                buffer.Append(chunk);
                if (buffer.ToString().Contains('>'))
                {
                    return;
                }
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException($"Modem did not present the '>' data-entry prompt on port '{_options.PortName}' before timeout.");
    }

    private async Task<IReadOnlyList<string>> ReadUntilTerminalAsync(SerialPort serialPort, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(_options.CommandTimeoutMs);
        var started = DateTime.UtcNow;
        var lines = new List<string>();
        var buffer = new StringBuilder();

        while (DateTime.UtcNow - started < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = await Task.Run(serialPort.ReadExisting, cancellationToken);
            if (!string.IsNullOrEmpty(chunk))
            {
                buffer.Append(chunk);
                while (TryTakeLine(buffer, out var line))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var normalized = line.Trim();
                        lines.Add(normalized);
                        if (IsTerminalResponse(normalized))
                        {
                            return lines;
                        }
                    }
                }
            }

            await Task.Delay(25, cancellationToken);
        }

        throw new TimeoutException($"No terminal response received for port '{_options.PortName}' before timeout.");
    }

    private static bool TryTakeLine(StringBuilder buffer, out string line)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var c = buffer[i];
            if (c == '\n')
            {
                line = buffer.ToString(0, i).Trim('\r');
                buffer.Remove(0, i + 1);
                return true;
            }
        }

        line = string.Empty;
        return false;
    }

    private static bool IsTerminalResponse(string line)
    {
        return line.Equals("OK", StringComparison.OrdinalIgnoreCase)
            || line.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("+CME ERROR", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("+CMS ERROR", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoError(IReadOnlyList<string> lines)
    {
        var errorLine = lines.FirstOrDefault(x => x.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
                                                || x.StartsWith("+CME ERROR", StringComparison.OrdinalIgnoreCase)
                                                || x.StartsWith("+CMS ERROR", StringComparison.OrdinalIgnoreCase));
        if (errorLine is not null)
        {
            throw new InvalidOperationException($"Modem command failed: {errorLine}");
        }
    }

    private static string StatusName(string stat) => stat switch
    {
        "0" => "REC UNREAD",
        "1" => "REC READ",
        "2" => "STO UNSENT",
        "3" => "STO SENT",
        _ => stat
    };

    private IReadOnlyList<RawPduMessage> ParseCmglPdu(IReadOnlyList<string> lines)
    {
        var messages = new List<RawPduMessage>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!line.StartsWith("+CMGL:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = CmglPduHeaderRegex.Match(line);
            if (!match.Success || i + 1 >= lines.Count)
            {
                continue;
            }

            var index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture);
            var stat = match.Groups["stat"].Value;
            var pduLine = lines[i + 1];

            try
            {
                var deliver = DecodeDeliverPdu(pduLine);
                messages.Add(new RawPduMessage(index, StatusName(stat), deliver));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse PDU for message at index {Index}; skipping it.", index);
            }

            i++;
        }

        return messages;
    }

    private SmsMessage? ParseCmgrPdu(IReadOnlyList<string> lines, int index)
    {
        var headerIndex = -1;
        var match = Match.Empty;
        for (var i = 0; i < lines.Count; i++)
        {
            var m = CmgrPduHeaderRegex.Match(lines[i]);
            if (m.Success)
            {
                headerIndex = i;
                match = m;
                break;
            }
        }

        if (headerIndex < 0 || headerIndex + 1 >= lines.Count)
        {
            return null;
        }

        var stat = match.Groups["stat"].Value;
        var pduLine = lines[headerIndex + 1];

        try
        {
            var deliver = DecodeDeliverPdu(pduLine);
            return new SmsMessage
            {
                Index = index,
                Status = StatusName(stat),
                Originator = deliver.SenderNumber?.ToString(),
                Timestamp = deliver.Timestamp,
                Body = deliver.Message,
                RawHeader = lines[headerIndex]
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse PDU for message at index {Index}.", index);
            return null;
        }
    }

    private static SmsDeliver DecodeDeliverPdu(string hexPdu)
    {
        var bytes = Convert.FromHexString(hexPdu.Trim());
        return SmsDecoder.Decode(bytes) as SmsDeliver
            ?? throw new InvalidOperationException("PDU is not an SMS-DELIVER message.");
    }

    /// <summary>
    /// Groups raw SIM entries into logical messages: single-part messages pass through unchanged,
    /// while parts belonging to the same concatenated (UDH) message are ordered by sequence number and
    /// merged into one SmsMessage with the combined body. The resulting index-group map lets
    /// DeleteMessageAsync remove every SIM slot for a reassembled message, not just its first part.
    /// </summary>
    private IReadOnlyList<SmsMessage> ReassembleMessages(IReadOnlyList<RawPduMessage> raw)
    {
        var indexGroups = new Dictionary<int, int[]>();
        var result = new List<SmsMessage>();

        foreach (var r in raw.Where(r => r.Deliver.TotalNumberOfParts <= 1))
        {
            indexGroups[r.Index] = [r.Index];
            result.Add(new SmsMessage
            {
                Index = r.Index,
                Status = r.Status,
                Originator = r.Deliver.SenderNumber?.ToString(),
                Timestamp = r.Deliver.Timestamp,
                Body = r.Deliver.Message,
                RawHeader = $"+CMGL: {r.Index},{r.Status}"
            });
        }

        var grouped = raw.Where(r => r.Deliver.TotalNumberOfParts > 1)
            .GroupBy(r => (r.Deliver.SenderNumber?.ToString(), r.Deliver.MessageReference, r.Deliver.TotalNumberOfParts));

        foreach (var group in grouped)
        {
            var ordered = group.OrderBy(r => r.Deliver.PartNumber).ToList();
            var representativeIndex = ordered[0].Index;
            var allIndices = ordered.Select(r => r.Index).ToArray();
            indexGroups[representativeIndex] = allIndices;

            var first = ordered[0];
            result.Add(new SmsMessage
            {
                Index = representativeIndex,
                Status = first.Status,
                Originator = first.Deliver.SenderNumber?.ToString(),
                Timestamp = first.Deliver.Timestamp,
                Body = string.Concat(ordered.Select(r => r.Deliver.Message)),
                RawHeader = $"+CMGL: {representativeIndex},{first.Status} (parts: {string.Join(',', allIndices)})"
            });
        }

        _lastIndexGroups = indexGroups;
        return result;
    }

    private sealed record RawPduMessage(int Index, string Status, SmsDeliver Deliver);

    private SerialPort CreatePort()
    {
        if (string.IsNullOrWhiteSpace(_options.PortName))
        {
            throw new InvalidOperationException("SmsService configuration 'PortName' is required.");
        }

        if (_options.BaudRate <= 0)
        {
            throw new InvalidOperationException("SmsService configuration 'BaudRate' must be greater than zero.");
        }

        if (!PortExists(_options.PortName))
        {
            logger.LogError(
                "Serial port '{PortName}' was not found on this host. Verify the modem is connected and that SERIAL_PORT (or SmsService:PortName) points to the correct device. Available ports: {AvailablePorts}",
                _options.PortName,
                string.Join(", ", SerialPort.GetPortNames()));
            throw new SerialPortNotFoundException(_options.PortName);
        }

        logger.LogInformation("Opening SMS modem on {PortName} at {BaudRate} baud", _options.PortName, _options.BaudRate);

        return new SerialPort(_options.PortName, _options.BaudRate, _options.Parity, _options.DataBits, _options.StopBits)
        {
            Encoding = Encoding.ASCII,
            NewLine = "\r\n",
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = _options.ReadTimeoutMs,
            WriteTimeout = _options.WriteTimeoutMs,
            Handshake = _options.Handshake
        };
    }

    private static bool PortExists(string portName)
    {
        // On Windows, COM ports aren't filesystem paths, so enumerate them via the OS port list.
        // On Linux/macOS, serial ports are device files (e.g. /dev/ttyUSB0), so a direct existence check works.
        return OperatingSystem.IsWindows()
            ? SerialPort.GetPortNames().Any(p => p.Equals(portName, StringComparison.OrdinalIgnoreCase))
            : File.Exists(portName);
    }
}

public sealed class SerialPortNotFoundException(string portName)
    : InvalidOperationException($"Serial port '{portName}' was not found.")
{
    public string PortName { get; } = portName;
}

public sealed class SmsServiceOptions
{
    // Left blank by default: the OS-specific default and SERIAL_PORT env var override are applied in Program.cs.
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 115200;
    public Parity Parity { get; set; } = Parity.None;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public Handshake Handshake { get; set; } = Handshake.None;
    public int CommandTimeoutMs { get; set; } = 10000;
    public int ReadTimeoutMs { get; set; } = 5000;
    public int WriteTimeoutMs { get; set; } = 5000;
}

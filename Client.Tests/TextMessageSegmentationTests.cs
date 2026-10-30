using System.Text;
using JamaaTech.Smpp.Net.Client;
using JamaaTech.Smpp.Net.Lib;
using JamaaTech.Smpp.Net.Lib.Protocol;

namespace Elyfe.Smpp.Client.Tests;

public class TextMessageSegmentationTests
{
    [Theory]
    [InlineData(DataCoding.SMSCDefault, 'A', 160, 1, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, 'A', 161, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '^', 80, 1, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '^', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '^', 160, 3, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '€', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '{', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '}', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '[', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, ']', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '\\', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '~', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '|', 81, 2, 160, 153)]
    [InlineData(DataCoding.SMSCDefault, '\f', 81, 2, 160, 153)]
    [InlineData(DataCoding.ASCII, 'A', 160, 1, 160, 153)]
    [InlineData(DataCoding.ASCII, 'A', 161, 2, 160, 153)]
    [InlineData(DataCoding.Latin1, 'é', 140, 1, 140, 134)]
    [InlineData(DataCoding.Latin1, 'é', 141, 2, 140, 134)]
    [InlineData(DataCoding.UCS2, '漢', 70, 1, 140, 134)]
    [InlineData(DataCoding.UCS2, '漢', 71, 2, 140, 134)]
    public void GetPDUs_RespectsEncodedPageBudgets(
        DataCoding coding, char character, int length, int expectedPages, int singleBudget, int multipartBudget)
    {
        AssertWirePages(new string(character, length), coding, new SmppEncodingService(),
            expectedPages, singleBudget, multipartBudget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetPDUs_DoesNotSplitSurrogatePairsAtMultipartBoundary(bool littleEndian)
    {
        var encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        var service = new SmppEncodingService(encoding);
        AssertWirePages(new string('漢', 66) + "😀" + new string('漢', 3),
            DataCoding.UCS2, service, 2, 140, 134);
    }

    [Fact]
    public void GetPDUs_PreservesConfiguredUtf8CharacterBoundaries()
    {
        AssertWirePages(new string('漢', 71), DataCoding.UCS2,
            new SmppEncodingService(Encoding.UTF8), 2, 140, 134);
    }

    [Fact]
    public void GetPDUs_PreservesConfiguredUtf8SupplementaryCharacterBoundary()
    {
        AssertWirePages(new string('A', 132) + "😀" + new string('A', 10),
            DataCoding.UCS2, new SmppEncodingService(Encoding.UTF8), 2, 140, 134);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetPDUs_PreservesConfiguredUtf7IndependentPageState(bool supplementaryBoundary)
    {
        // Exercise a stateful encoding, not a recommendation to use UTF-7 on SMS.
#pragma warning disable SYSLIB0001
        var encoding = Encoding.UTF7;
#pragma warning restore SYSLIB0001
        var text = supplementaryBoundary
            ? new string('漢', 48) + "😀+ASCII" + new string('漢', 48)
            : new string('漢', 100);
        AssertWirePages(text, DataCoding.UCS2, new SmppEncodingService(encoding), 3, 140, 134);
    }

    [Theory]
    [InlineData(80, 1)]
    [InlineData(81, 2)]
    [InlineData(160, 3)]
    public void MultiPartTextMessage_OnlyLastPageHasResponse(int carets, int expectedPages)
    {
        var message = new TestableMultiPartTextMessage { Text = new string('^', carets) };
        var responses = new List<bool>();
        var reconstructed = new StringBuilder();
        foreach (var pdu in message.GetWirePages())
        {
            responses.Add(pdu.HasResponse);
            reconstructed.Append(pdu.GetMessageText());
        }
        Assert.Equal(expectedPages, responses.Count);
        for (var i = 0; i < responses.Count; i++)
            Assert.Equal(i == responses.Count - 1, responses[i]);
        Assert.Equal(message.Text, reconstructed.ToString());
    }

    private sealed class TestableMultiPartTextMessage : MultiPartTextMessage
    {
        public IEnumerable<SendSmPDU> GetWirePages() =>
            GetPDUs(DataCoding.SMSCDefault, new SmppEncodingService());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(70)]
    public void GetPDUs_PreservesEmbeddedUnicodeNull(int prefixLength)
    {
        var service = new SmppEncodingService();
        var message = new TestableTextMessage { Text = new string('漢', prefixLength) + "\0漢" };
        var reconstructed = new StringBuilder();
        foreach (var pdu in message.GetWirePages(DataCoding.UCS2, service))
        {
            var wire = pdu.GetMessageBytes();
            var offset = (pdu.EsmClass & EsmClass.UdhiIndicator) != 0 ? wire[0] + 1 : 0;
            reconstructed.Append(service.UCS2Encoding.GetString(wire, offset, wire.Length - offset));
        }
        Assert.Equal(message.Text, reconstructed.ToString());
    }

    [Theory]
    [InlineData(70, 70)]
    [InlineData(71, 67)]
    public void GetPDUs_PreservesPublicUnicodeMaxMessageLength(int length, int expectedMaximum)
    {
        var message = new TestableTextMessage { Text = new string('漢', length) };
        foreach (var pdu in message.GetWirePages(DataCoding.UCS2, new SmppEncodingService()))
            _ = pdu.GetMessageBytes();
        Assert.Equal(expectedMaximum, message.MaxMessageLength);
    }

    [Theory]
    [InlineData(DataCoding.ASCII, 160)]
    [InlineData(DataCoding.Latin1, 140)]
    public void GetPDUs_PreservesEmbeddedSingleByteNull(DataCoding coding, int prefixLength)
    {
        var service = new SmppEncodingService();
        var message = new TestableTextMessage { Text = new string('A', prefixLength) + "\0B" };
        var reconstructed = new List<byte>();
        foreach (var pdu in message.GetWirePages(coding, service))
        {
            var wire = pdu.GetMessageBytes();
            var offset = (pdu.EsmClass & EsmClass.UdhiIndicator) != 0 ? wire[0] + 1 : 0;
            reconstructed.AddRange(wire.AsSpan(offset).ToArray());
        }
        Assert.Equal(service.GetBytesFromString(message.Text, coding), reconstructed.ToArray());
    }

    [Fact]
    public void GetPDUs_ReenumerationResetsHeadersAndPageBudget()
    {
        var message = new TestableTextMessage { Text = new string('^', 81) };
        var pages = message.GetWirePages(DataCoding.SMSCDefault, new SmppEncodingService());
        for (var pass = 0; pass < 2; pass++)
        {
            var sequence = 0;
            foreach (var pdu in pages)
            {
                var wire = pdu.GetMessageBytes();
                Assert.Equal(EsmClass.UdhiIndicator, pdu.EsmClass & EsmClass.UdhiIndicator);
                Assert.Equal(new byte[] { 5, 0, 3 }, wire[..3]);
                Assert.Equal(2, (int)wire[4]);
                Assert.Equal(++sequence, (int)wire[5]);
            }
            Assert.Equal(2, sequence);
            Assert.Equal(153, message.MaxMessageLength);
        }

        message.Text = "short";
        var single = Assert.Single(pages);
        Assert.Equal((EsmClass)0, single.EsmClass & EsmClass.UdhiIndicator);
        Assert.Equal("short", single.GetMessageText());
        Assert.Equal(160, message.MaxMessageLength);
    }

    [Fact]
    public void GsmEncoding_RespectsInputAndOutputRanges()
    {
        var encoding = new JamaaTech.Smpp.Net.Lib.Util.GSMEncoding();
        var source = "AA^B".ToCharArray();
        Assert.Equal(3, encoding.GetByteCount(source, 2, 2));
        var bytes = new byte[5];
        Assert.Equal(3, encoding.GetBytes(source, 2, 2, bytes, 1));
        Assert.Equal(new byte[] { 0, 0x1B, 0x14, 0x42, 0 }, bytes);
        var characters = new[] { '!', '!', '!', '!' };
        Assert.Equal(2, encoding.GetChars(bytes, 1, 3, characters, 1));
        Assert.Equal(new[] { '!', '^', 'B', '!' }, characters);
    }

    [Fact]
    public void GsmEncoding_DoesNotDecodeEscapeBeyondRequestedRange()
    {
        var encoding = new JamaaTech.Smpp.Net.Lib.Util.GSMEncoding();
        var bytes = new byte[] { 0x41, 0x1B, 0x14 };
        Assert.Equal(0, encoding.GetCharCount(bytes, 1, 1));
        var characters = new[] { '!' };
        Assert.Equal(0, encoding.GetChars(bytes, 1, 1, characters, 0));
        Assert.Equal('!', characters[0]);
    }

    private static void AssertWirePages(string text, DataCoding coding, SmppEncodingService service,
        int expectedPages, int singleBudget, int multipartBudget)
    {
        var message = new TestableTextMessage { Text = text, SourceAddress = "test", DestinationAddress = "1234" };
        var payloads = new List<byte[]>();
        var decoded = new StringBuilder();
        foreach (var pdu in message.GetWirePages(coding, service))
        {
            // GetPDUs reuses SubmitSm: capture the wire payload before advancing the iterator.
            var wire = pdu.GetMessageBytes().ToArray();
            payloads.Add(wire);
            var hasUdh = (pdu.EsmClass & EsmClass.UdhiIndicator) != 0;
            Assert.Equal(expectedPages > 1, hasUdh);
            var headerLength = hasUdh ? wire[0] + 1 : 0;
            var payload = wire.AsSpan(headerLength).ToArray();
            Assert.InRange(payload.Length, 1, hasUdh ? multipartBudget : singleBudget);
            if (coding == DataCoding.SMSCDefault)
                Assert.NotEqual((byte)0x1B, payload[^1]);
            if (coding == DataCoding.UCS2 && service.UCS2Encoding.CodePage is 1200 or 1201)
                Assert.Equal(0, payload.Length % 2);
            var pageText = service.GetStringFromBytes(payload, coding);
            Assert.DoesNotContain("\uFFFD", pageText);
            Assert.False(pageText.Length > 0 && char.IsHighSurrogate(pageText[^1]));
            Assert.False(pageText.Length > 0 && char.IsLowSurrogate(pageText[0]));
            Assert.Equal(payload, service.GetBytesFromString(pageText, coding));
            decoded.Append(pageText);
            if (hasUdh)
            {
                Assert.Equal(expectedPages, (int)wire[4]);
                Assert.Equal(payloads.Count, (int)wire[5]);
            }
        }

        Assert.Equal(expectedPages, payloads.Count);
        Assert.Equal(text, decoded.ToString());
    }

    private sealed class TestableTextMessage : TextMessage
    {
        public IEnumerable<SendSmPDU> GetWirePages(DataCoding coding, SmppEncodingService service) =>
            GetPDUs(coding, service);
    }
}

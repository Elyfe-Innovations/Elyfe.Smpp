using JamaaTech.Smpp.Net.Lib.Protocol.Tlv;
using Microsoft.Extensions.Logging;
using System.Text;

namespace JamaaTech.Smpp.Net.Lib.Logging;

public static class LoggingExtensions
{
  private static readonly ILogger Logger = SmppLog.For(typeof(LoggingExtensions));

  public static Func<object, SmppEncodingService, string> DumpString { get; set; } = DumpStringDefault;

  public static string DumpStringWithTry(object obj, SmppEncodingService encodingService = null)
  {
    try
    {
      return DumpStringDefault(obj, encodingService);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to dump {ObjectType} for logging", obj?.GetType().Name);
      return null;
    }
  }

  public static string DumpStringDefault(object obj, SmppEncodingService encodingService = null)
  {
    var sb = new StringBuilder();
    sb.Append(obj.GetType().Name).Append(" -- ");

    foreach (var property in obj.GetType().GetProperties())
    {
      object value = "--";

      try
      {
        value = property.GetValue(obj, null);
      }
      catch
      {
        // A property getter that throws must not break the dump; leave the placeholder.
      }

      if (value is byte[])
        value = BytesToString(value as byte[], encodingService);
      else if (value is TlvCollection) value = TlvCollectionToString(value as TlvCollection, encodingService);

      sb.Append(property.Name).Append(':').Append(value).Append(' ');
    }

    return sb.ToString();
  }

  private static string TlvCollectionToString(TlvCollection tlvCollection, SmppEncodingService encodingService)
  {
    var tags = new StringBuilder();
    tags.Append("[");
    foreach (var tlv in tlvCollection)
      tags.Append(tlv.Tag).Append(':').Append(BytesToString(tlv.RawValue, encodingService)).Append(' ');
    tags.Append("]");

    return tags.ToString();
  }

  private static string BytesToString(byte[] value, SmppEncodingService encodingService)
  {
    try
    {
      if (encodingService != null)
        return encodingService.GetCStringFromBytes(value);

      return BytesToStringHex(value);
    }
    catch (Exception)
    {
      return BytesToStringHex(value);
    }
  }

  private static string BytesToStringHex(byte[] value)
  {
    return Compat.EncodingCompat.ToHexString(value);
  }
}
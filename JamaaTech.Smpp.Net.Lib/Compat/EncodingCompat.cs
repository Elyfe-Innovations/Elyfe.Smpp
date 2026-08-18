using System.Text;

namespace JamaaTech.Smpp.Net.Lib.Compat;

/// <summary>
/// Encoding and formatting APIs that exist on net5+ but not on netstandard2.1, kept in one
/// place so the call sites stay free of conditional compilation.
/// </summary>
internal static class EncodingCompat
{
  /// <summary>ISO-8859-1 (Latin-1).</summary>
  public static Encoding Latin1 { get; } =
#if NET5_0_OR_GREATER
    Encoding.Latin1;
#else
    Encoding.GetEncoding(28591 /*"iso-8859-1"*/);
#endif

  /// <summary>Formats <paramref name="bytes"/> as lowercase hex, without separators.</summary>
  public static string ToHexString(byte[] bytes)
  {
    if (bytes == null) throw new ArgumentNullException(nameof(bytes));

#if NET5_0_OR_GREATER
    return Convert.ToHexString(bytes).ToLowerInvariant();
#else
    var hex = new StringBuilder(bytes.Length * 2);
    foreach (var b in bytes) hex.Append(b.ToString("x2"));
    return hex.ToString();
#endif
  }
}

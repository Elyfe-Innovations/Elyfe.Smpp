/************************************************************************
 * Copyright (C) 2007 Jamaa Technologies
 *
 * This file is part of Jamaa SMPP Library.
 *
 * Jamaa SMPP Library is free software. You can redistribute it and/or modify
 * it under the terms of the Microsoft Reciprocal License (Ms-RL)
 *
 * You should have received a copy of the Microsoft Reciprocal License
 * along with Jamaa SMPP Library; See License.txt for more details.
 *
 * Author: Benedict J. Tesha
 * benedict.tesha@jamaatech.com, www.jamaatech.com
 *
 ************************************************************************/

namespace JamaaTech.Smpp.Net.Lib.Util;

public static class Latin1Encoding
{
  #region Variables

  private static readonly System.Text.Encoding vEncoding = Compat.EncodingCompat.Latin1;

  #endregion

  #region Methods

  public static byte[] GetBytes(string str)
  {
    return vEncoding.GetBytes(str);
  }

  public static string GetString(byte[] bytes)
  {
    return vEncoding.GetString(bytes);
  }

  #endregion
}
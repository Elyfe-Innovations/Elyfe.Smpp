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

using JamaaTech.Smpp.Net.Lib.Util;

namespace JamaaTech.Smpp.Net.Lib.Protocol;

public sealed class SubmitSmResp : ResponsePDU
{
  #region Variables

  private string _MessageId;

  #endregion

  #region Constructors

  internal SubmitSmResp(PDUHeader header, SmppEncodingService smppEncodingService)
    : base(header, smppEncodingService)
  {
    _MessageId = "";
  }

  #endregion

  #region properties

  public override SmppEntityType AllowedSource => SmppEntityType.SMSC;

  public override SmppSessionState AllowedSession => SmppSessionState.Transmitter;

  public string MessageID
  {
    get => _MessageId;
    set => _MessageId = value;
  }

  #endregion

  #region Methods

  protected override byte[] GetBodyData()
  {
    return EncodeCString(_MessageId, vSmppEncodingService);
  }

  protected override void Parse(ByteBuffer buffer)
  {
    if (buffer == null) throw new ArgumentNullException(nameof(buffer));
    //Note that the body part may have not been returned by
    //the SMSC if the command status is not 0
    if (buffer.Length == 0) return;
    if (string.IsNullOrEmpty(_MessageId))
      _MessageId = DecodeCString(buffer, vSmppEncodingService);
    else
      _MessageId += $":{DecodeCString(buffer, vSmppEncodingService)}";
    //This pdu has no optional parameters,
    //after preceding statements, the buffer must remain with no data
    if (buffer.Length > 0) throw new TooManyBytesException();
  }

  #endregion
}
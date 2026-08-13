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

public sealed class CancelSm : SmOperationPDU
{
  #region Variables

  private SmppAddress vDestinationAddress;

  #endregion

  #region Constuctors

  public CancelSm(SmppEncodingService smppEncodingService)
    : base(new PDUHeader(CommandType.CancelSm), smppEncodingService)
  {
    vDestinationAddress = new SmppAddress();
  }

  internal CancelSm(PDUHeader header, SmppEncodingService smppEncodingService)
    : base(header, smppEncodingService)
  {
    vDestinationAddress = new SmppAddress();
  }

  #endregion

  #region Properties

  public override SmppEntityType AllowedSource => SmppEntityType.ESME;

  public override SmppSessionState AllowedSession => SmppSessionState.Transmitter;

  public SmppAddress DestionationAddress => vDestinationAddress;

  #endregion

  #region Methods

  public override ResponsePDU CreateDefaultResponse()
  {
    var header = new PDUHeader(CommandType.CancelSmResp, vHeader.SequenceNumber);
    return new CancelSmResp(header, vSmppEncodingService);
  }

  protected override byte[] GetBodyData()
  {
    var buffer = new ByteBuffer(64);
    buffer.Append(EncodeCString(vMessageID, vSmppEncodingService));
    buffer.Append(_sourceAddress.GetBytes(vSmppEncodingService));
    buffer.Append(vDestinationAddress.GetBytes(vSmppEncodingService));
    return buffer.ToBytes();
  }

  protected override void Parse(ByteBuffer buffer)
  {
    if (buffer == null) throw new ArgumentNullException(nameof(buffer));
    vMessageID = DecodeCString(buffer, vSmppEncodingService);
    _sourceAddress = SmppAddress.Parse(buffer, vSmppEncodingService);
    vDestinationAddress = SmppAddress.Parse(buffer, vSmppEncodingService);
    //If this pdu has no option parameters
    //If there is still something in the buffer, we then have more than required bytes
    if (buffer.Length > 0) throw new TooManyBytesException();
  }

  #endregion
}
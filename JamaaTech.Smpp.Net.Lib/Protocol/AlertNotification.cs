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
using JamaaTech.Smpp.Net.Lib.Protocol.Tlv;

namespace JamaaTech.Smpp.Net.Lib.Protocol;

public class AlertNotification : SmPDU
{
  #region Variables

  private SmppAddress vEsmeAddress;

  #endregion

  #region Constructors

  public AlertNotification(SmppEncodingService smppEncodingService)
    : base(new PDUHeader(CommandType.AlertNotification), smppEncodingService)
  {
    vEsmeAddress = new SmppAddress();
  }

  internal AlertNotification(PDUHeader header, SmppEncodingService smppEncodingService)
    : base(header, smppEncodingService)
  {
    vEsmeAddress = new SmppAddress();
  }

  #endregion

  #region Properties

  public override bool HasResponse => false;

  public override SmppEntityType AllowedSource => SmppEntityType.SMSC;

  public override SmppSessionState AllowedSession => SmppSessionState.Receiver;

  public SmppAddress ESMEAddress => vEsmeAddress;

  #endregion

  #region Methods

  public override ResponsePDU CreateDefaultResponse()
  {
    return null;
  }

  protected override byte[] GetBodyData()
  {
    var sourceAddrBytes = _sourceAddress.GetBytes(vSmppEncodingService);
    var esmeAddresBytes = vEsmeAddress.GetBytes(vSmppEncodingService);
    var buffer = new ByteBuffer(sourceAddrBytes.Length + esmeAddresBytes.Length);
    buffer.Append(sourceAddrBytes);
    buffer.Append(esmeAddresBytes);
    return buffer.ToBytes();
  }

  protected override void Parse(ByteBuffer buffer)
  {
    if (buffer == null) throw new ArgumentNullException(nameof(buffer));
    _sourceAddress = SmppAddress.Parse(buffer, vSmppEncodingService);
    vEsmeAddress = SmppAddress.Parse(buffer, vSmppEncodingService);
    //If there are some bytes left,
    //construct a tlv collection
    if (buffer.Length > 0) vTlv = TlvCollection.Parse(buffer, vSmppEncodingService);
  }

  #endregion
}
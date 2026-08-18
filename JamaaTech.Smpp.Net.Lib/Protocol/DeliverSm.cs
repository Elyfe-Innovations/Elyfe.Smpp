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

public class DeliverSm : SingleDestinationPDU
{
  #region Variables

  private byte vProtocolId;
  private PriorityFlag vPriorityFlag;
  private string vScheduleDeliveryTime;
  private string vValidityPeriod;
  private bool vReplaceIfPresent;
  private byte vSmDefalutMessageId;
  private byte[] vMessageBytes;

  #endregion

  #region Properties

  public override SmppEntityType AllowedSource => SmppEntityType.ESME;

  public override SmppSessionState AllowedSession => SmppSessionState.Transmitter;

  public byte ProtocolID
  {
    get => vProtocolId;
    set => vProtocolId = value;
  }

  public PriorityFlag PriorityFlag
  {
    get => vPriorityFlag;
    set => vPriorityFlag = value;
  }

  public string ScheduleDeliveryTime
  {
    get => vScheduleDeliveryTime;
    set => vScheduleDeliveryTime = value;
  }

  public string ValidityPeriod
  {
    get => vValidityPeriod;
    set => vValidityPeriod = value;
  }

  public bool ReplaceIfPresent
  {
    get => vReplaceIfPresent;
    set => vReplaceIfPresent = value;
  }

  public byte SmDefaultMessageId
  {
    get => vSmDefalutMessageId;
    set => vSmDefalutMessageId = value;
  }

  #endregion

  #region Constructors

  public DeliverSm(PDUHeader header, SmppEncodingService smppEncodingService)
    : base(header, smppEncodingService)
  {
    vServiceType = Protocol.ServiceType.DEFAULT;
    vProtocolId = 0;
    vPriorityFlag = PriorityFlag.Level0;
    vScheduleDeliveryTime = "";
    vValidityPeriod = "";
    vRegisteredDelivery = RegisteredDelivery.None;
    vReplaceIfPresent = false;
    vDataCoding = DataCoding.ASCII;
    vSmDefalutMessageId = 0;
  }

  public DeliverSm(SmppEncodingService smppEncodingService)
    : this(new PDUHeader(CommandType.SubmitSm), smppEncodingService)
  {
  }

  #endregion

  #region Methods

  public override ResponsePDU CreateDefaultResponse()
  {
    var header = new PDUHeader(CommandType.DeliverSmResp, vHeader.SequenceNumber);
    return new DeliverSmResp(header, vSmppEncodingService);
  }

  protected override byte[] GetBodyData()
  {
    var buffer = new ByteBuffer(256);
    buffer.Append(EncodeCString(vServiceType, vSmppEncodingService));
    buffer.Append(_sourceAddress.GetBytes(vSmppEncodingService));
    buffer.Append(vDestinationAddress.GetBytes(vSmppEncodingService));
    buffer.Append((byte)vEsmClass);
    buffer.Append(vProtocolId);
    buffer.Append((byte)vPriorityFlag);
    buffer.Append(EncodeCString(vScheduleDeliveryTime, vSmppEncodingService));
    buffer.Append(EncodeCString(vValidityPeriod, vSmppEncodingService));
    buffer.Append((byte)vRegisteredDelivery);
    buffer.Append(vReplaceIfPresent ? (byte)1 : (byte)0);
    buffer.Append((byte)vDataCoding);
    buffer.Append(vSmDefalutMessageId);
    //Check if vMessageBytes is not null
    if (vMessageBytes == null)
      //Check whether optional field is used
      if (vTlv.GetTlvByTag(Tag.message_payload) == null)
        //Create an empty message
        vMessageBytes = new byte[] { 0x00 };
    if (vMessageBytes == null)
    {
      buffer.Append(0);
    }
    else
    {
      buffer.Append((byte)vMessageBytes.Length);
      buffer.Append(vMessageBytes);
    }

    return buffer.ToBytes();
  }

  protected override void Parse(ByteBuffer buffer)
  {
    if (buffer == null) throw new ArgumentNullException(nameof(buffer));
    vServiceType = DecodeCString(buffer, vSmppEncodingService);
    _sourceAddress = SmppAddress.Parse(buffer, vSmppEncodingService);
    vDestinationAddress = SmppAddress.Parse(buffer, vSmppEncodingService);
    vEsmClass = (EsmClass)GetByte(buffer);
    vProtocolId = GetByte(buffer);
    vPriorityFlag = (PriorityFlag)GetByte(buffer);
    vScheduleDeliveryTime = DecodeCString(buffer, vSmppEncodingService);
    vValidityPeriod = DecodeCString(buffer, vSmppEncodingService);
    vRegisteredDelivery = (RegisteredDelivery)GetByte(buffer);
    vReplaceIfPresent = GetByte(buffer) == 0 ? false : true;
    vDataCoding = (DataCoding)GetByte(buffer);
    vSmDefalutMessageId = GetByte(buffer);
    int length = GetByte(buffer);
    if (length == 0)
    {
      vMessageBytes = null;
    }
    else
    {
      if (length > buffer.Length)
        throw new NotEnoughBytesException("Pdu encoutered less bytes than indicated by message length");
      vMessageBytes = buffer.Remove(length);
    }

    if (buffer.Length > 0) vTlv = TlvCollection.Parse(buffer, vSmppEncodingService);
  }

  public override byte[] GetMessageBytes()
  {
    if (vMessageBytes != null) return vMessageBytes;
    //Otherwise, check if the 'message_payload' field is used
    var tlv = vTlv.GetTlvByTag(Tag.message_payload);
    if (tlv == null) return null;
    return tlv.RawValue;
  }

  public override void SetMessageBytes(byte[] message)
  {
    if (message != null && message.Length > 254)
      throw new ArgumentException("Message length cannot be greater than 254 bytes");
    vMessageBytes = message;
  }

  #endregion
}